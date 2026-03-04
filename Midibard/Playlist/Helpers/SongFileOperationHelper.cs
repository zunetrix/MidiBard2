using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages file I/O operations: song import, file validation, metadata syncing, and duration calculation.
/// Encapsulates all MIDI file handling and file system interactions.
/// </summary>
internal class SongFileOperationHelper
{
    private readonly CurrentSongController _songController;

    // Callback for removing song if file no longer exists
    private readonly Func<int, Task> _onRemoveSongCallback;

    public SongFileOperationHelper(
        CurrentSongController songController,
        Func<int, Task> onRemoveSongCallback)
    {
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
                    var song = await ServiceContainer.SongService.GetOrCreateFromFileAsync(
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
                var addSuccess = await ServiceContainer.PlaylistSongService.BulkAddSongsAsync(currentPlaylist.Id, successfulSongs.Select(s => s.Id));
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
                await ServiceContainer.MidiFileService.CalculateAllDurationsAsync(songsToCalc);
                foreach (var s in songsToCalc)
                {
                    await ServiceContainer.SongService.UpdateAsync(s);
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
                var midiFile = ServiceContainer.MidiFileService.LoadMidiFile(song.FilePath);
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

        if (updated)
        {
            await ServiceContainer.SongService.UpdateAsync(song);
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

            var midiFile = ServiceContainer.MidiFileService.LoadMidiFile(song.FilePath);
            if (midiFile != null)
            {
                song.Duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                _ = ServiceContainer.SongService.UpdateAsync(song);
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

            var file = ServiceContainer.MidiFileService.LoadMidiFile(path);
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
