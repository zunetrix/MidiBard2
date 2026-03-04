using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;

using MidiBard.Extensions.DryWetMidi;
using MidiBard.Playlist.Services;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages file I/O operations: song import, file validation, metadata syncing, and duration calculation.
/// Encapsulates all MIDI file handling and file system interactions.
/// </summary>
internal class SongFileOperationHelper
{
    private readonly ISongService? _songService;
    private readonly IPlaylistSongService? _playlistSongService;
    private readonly IMidiFileService? _midiFileService;
    private readonly CurrentSongController _songController;

    // Callback for removing song if file no longer exists
    private readonly Func<int, Task> _onRemoveSongCallback;

    public SongFileOperationHelper(
        ISongService? songService,
        IPlaylistSongService? playlistSongService,
        IMidiFileService? midiFileService,
        CurrentSongController songController,
        Func<int, Task> onRemoveSongCallback)
    {
        _songService = songService;
        _playlistSongService = playlistSongService;
        _midiFileService = midiFileService;
        _songController = songController;
        _onRemoveSongCallback = onRemoveSongCallback;
    }

    /// <summary>
    /// Import songs from file paths into current playlist.
    /// </summary>
    public async Task AddSongsAsync(Playlist? currentPlaylist, IEnumerable<string> filePaths)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[SongFileOperationHelper] Cannot add songs - current playlist is invalid");
            return;
        }

        if (_playlistSongService == null || _songService == null || _midiFileService == null)
        {
            DalamudApi.PluginLog.Warning("[SongFileOperationHelper] Services not initialized");
            return;
        }

        var successfulSongs = new List<Song>();
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            foreach (var (file, path) in CheckValidFiles(filePaths))
            {
                try
                {
                    var songLength = file.GetDurationTimeSpan() ?? TimeSpan.Zero;

                    // 1. Create or get song via service
                    var song = await _songService.GetOrCreateFromFileAsync(
                        path,
                        Path.GetFileNameWithoutExtension(path),
                        "",  // Artist
                        0,   // ReleaseYear
                        songLength
                    );

                    if (song == null)
                    {
                        DalamudApi.PluginLog.Warning($"[SongFileOperationHelper] Failed to create/get song from file {path}");
                        continue;
                    }

                    successfulSongs.Add(song);
                }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Warning(ex, "[SongFileOperationHelper] Error when adding song");
                }
            }

            // 2. Bulk add all songs to playlist in one DB operation
            if (successfulSongs.Count > 0)
            {
                var addSuccess = await _playlistSongService.BulkAddSongsAsync(currentPlaylist.Id, successfulSongs.Select(s => s.Id));
                if (addSuccess)
                {
                    foreach (var song in successfulSongs)
                    {
                        currentPlaylist.AddSong(new PlaylistSong
                        {
                            Song = song,
                            IsPlayed = false,
                            AddedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    DalamudApi.PluginLog.Warning("[SongFileOperationHelper] Bulk add to playlist failed");
                    successfulSongs.Clear();
                }
            }

            // 3. Calculate durations in parallel for songs that don't have them, then persist
            var songsToCalc = currentPlaylist.Songs
                .Where(ps => ps.Song != null && ps.Song.Duration == default)
                .Select(ps => ps.Song!)
                .ToList();

            if (songsToCalc.Count > 0)
            {
                await _midiFileService.CalculateAllDurationsAsync(songsToCalc);
                foreach (var s in songsToCalc)
                {
                    await _songService.UpdateAsync(s);
                }
            }
        });

        DalamudApi.PluginLog.Information($"[SongFileOperationHelper] File import complete in {sw.Elapsed.TotalMilliseconds} ms! success: {successfulSongs.Count}");
    }

    /// <summary>
    /// Sync song file metadata: validate file existence, update modification date, recalculate duration.
    /// </summary>
    public async Task SyncSongFileDataAsync(Song song)
    {
        if (song == null) return;

        bool updated = false;

        // Validate file path
        var hasValidFilePath = !string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath);
        if (song.IsValid != hasValidFilePath)
        {
            song.IsValid = hasValidFilePath;
            updated = true;
        }

        // Update file last modified date if file exists
        if (hasValidFilePath)
        {
            try
            {
                var fileLastModified = File.GetLastWriteTime(song.FilePath);
                if (song.FileLastModifiedAt != fileLastModified)
                {
                    song.FileLastModifiedAt = fileLastModified;
                    updated = true;
                }
            }
            catch
            {
                // Ignore errors getting file date
            }
        }

        // Recalculate duration if file exists
        if (hasValidFilePath)
        {
            try
            {
                var midiFile = _midiFileService?.LoadMidiFile(song.FilePath);
                if (midiFile != null)
                {
                    var newDuration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    if (song.Duration != newDuration)
                    {
                        song.Duration = newDuration;
                        updated = true;
                    }
                }
            }
            catch
            {
                // If we can't read the file, mark as invalid
                if (song.IsValid)
                {
                    song.IsValid = false;
                    updated = true;
                }
            }
        }

        if (updated && _songService != null)
        {
            await _songService.UpdateAsync(song);
        }
    }

    /// <summary>
    /// Calculate duration for a song at given index in current playlist.
    /// Removes song if file no longer exists.
    /// </summary>
    public async Task CalculateSongDurationAsync(Playlist? currentPlaylist, int songIndex)
    {
        if (currentPlaylist == null || !IsValidSongIndex(currentPlaylist, songIndex))
            return;

        try
        {
            var ps = currentPlaylist.Songs[songIndex];
            var song = ps.Song;
            if (song == null) return;

            if (!File.Exists(song.FilePath))
            {
                await _onRemoveSongCallback(songIndex);
                ImGuiUtil.AddNotification(NotificationType.Warning, $"The song file no longer exists and has been removed from the playlist");
                return;
            }

            var midiFile = _midiFileService?.LoadMidiFile(song.FilePath);
            if (midiFile != null)
            {
                song.Duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                if (_songService != null)
                {
                    _ = _songService.UpdateAsync(song);
                }
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "[SongFileOperationHelper] Error when getting song duration");
        }
    }

    // ==================== Helper Methods ====================

    /// <summary>
    /// Validate file paths and yield only valid MIDI files.
    /// </summary>
    private IEnumerable<(MidiFile, string)> CheckValidFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            if (!Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase))
                continue;

            var file = _midiFileService?.LoadMidiFile(path);
            if (file is not null)
                yield return (file, path);
        }
    }

    private bool IsValidSongIndex(Playlist playlist, int songIndex)
    {
        var isEmptyList = playlist == null || playlist.Songs == null || playlist.Songs.Count == 0;
        var isInvalidIndex = songIndex < 0 || songIndex >= (playlist?.Songs.Count ?? 0);

        return !isEmptyList && !isInvalidIndex;
    }
}
