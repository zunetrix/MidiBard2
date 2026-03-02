using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Util;

namespace MidiBard;

internal class PlaylistManager
{
    private Plugin Plugin { get; }
    private readonly IPlaylistService? _playlistService;
    private readonly IPlaylistSongService? _playlistSongService;
    private readonly ISongService? _songService;
    private readonly IMidiFileService? _midiFileService;

    private Playlist.Playlist? _currentPlaylist;
    private PlaylistSong? _currentPlayingSong = null;

    // Derives from current playlist songs (single source of truth)
    public List<SongEntry> FilePathList => _currentPlaylist?.Songs
        .Select((ps, index) => new SongEntry
        {
            FilePath = ps.Song?.FilePath ?? "",
            SongLength = ps.Song?.Duration ?? TimeSpan.Zero,
            IsFilePlayed = ps.IsPlayed
        })
        .ToList() ?? new();

    // Compatibility property for existing code
    public Playlist.Playlist? CurrentPlaylist
    {
        get => _currentPlaylist;
        set
        {
            _currentPlaylist = value;
            if (_currentPlaylist != null)
                Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
        }
    }

    public int CurrentSongIndex
    {
        get => GetCurrentSongIndex();
        set
        {
            if (value < 0 || _currentPlaylist == null || value >= _currentPlaylist.Songs.Count)
            {
                _currentPlayingSong = null;
                return;
            }
            _currentPlayingSong = _currentPlaylist.Songs[value];
        }
    }

    /// <summary>
    /// Get the current song index based on identity reference (not stored directly).
    /// Returns -1 if no song is currently playing.
    /// </summary>
    public int GetCurrentSongIndex()
    {
        if (_currentPlayingSong == null || _currentPlaylist == null)
            return -1;
        return _currentPlaylist.Songs.IndexOf(_currentPlayingSong);
    }

    /// <summary>
    /// Get the currently playing song by reference (survives reordering/mutations).
    /// </summary>
    public PlaylistSong? CurrentPlayingSong => _currentPlayingSong;

    public PlaylistManager(Plugin plugin)
    {
        Plugin = plugin;

        // Auto-resolve services from registry
        _playlistService = ServiceContainer.GetService<IPlaylistService>();
        _playlistSongService = ServiceContainer.GetService<IPlaylistSongService>();
        _songService = ServiceContainer.GetService<ISongService>();
        _midiFileService = ServiceContainer.GetService<IMidiFileService>();

        DalamudApi.PluginLog.Debug("[PlaylistManager] Services auto-resolved from ServiceContainer");

        // Load last used playlist
        _ = LoadLastPlaylistAsync();
    }

    /// <summary>
    /// Reload the current playlist from database
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_currentPlaylist != null)
        {
            await LoadPlaylistByIdAsync(_currentPlaylist.Id);
        }
    }

    /// <summary>
    /// Load last used playlist from database
    /// </summary>
    private async Task LoadLastPlaylistAsync()
    {
        try
        {
            var playlists = await _playlistService.GetAllAsync();

            if (playlists.Count == 0)
            {
                // Create default playlist
                _currentPlaylist = await _playlistService.CreateAsync("Default");
            }
            else
            {
                // Load first playlist (or implement last used logic)
                await LoadPlaylistByIdAsync(playlists[0].Id);
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error loading last playlist");
            // Create default playlist as fallback
            _currentPlaylist = new Playlist.Playlist { Name = "Default" };
        }
    }

    /// <summary>
    /// Load playlist by ID from database
    /// </summary>
    public async Task LoadPlaylistByIdAsync(int playlistId)
    {
        try
        {
            DalamudApi.PluginLog.Warning($"LoadPlaylistByIdAsync({playlistId})");
            _currentPlaylist = await _playlistService.GetByIdAsync(playlistId);
            _currentPlayingSong = null;  // Reset song reference when loading new playlist
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error loading playlist {PlaylistId}", playlistId);
        }
    }

    /// <summary>
    /// Switch to a different playlist
    /// </summary>
    public async Task SwitchToPlaylistAsync(int playlistId)
    {
        await LoadPlaylistByIdAsync(playlistId);
        _currentPlayingSong = null;
        if (_currentPlaylist != null)
            Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
    }

    /// <summary>
    /// Get all playlists (for UI)
    /// </summary>
    public async Task<List<Playlist.Playlist>> GetAllPlaylistsAsync()
    {
        return await _playlistService.GetAllAsync();
    }

    /// <summary>
    /// Get songs for a specific playlist (for UI)
    /// </summary>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        try
        {
            var playlist = await _playlistService.GetByIdAsync(playlistId);
            if (playlist?.Songs == null)
                return new List<Song>();

            // Return songs from playlist (already loaded via service)
            return playlist.Songs
                .Where(ps => ps.Song != null)
                .Select(ps => ps.Song)
                .ToList();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error getting playlist songs for {PlaylistId}", playlistId);
            return new List<Song>();
        }
    }

    /// <summary>
    /// Create a new playlist
    /// </summary>
    public async Task<Playlist.Playlist> CreatePlaylistAsync(string name)
    {
        return await _playlistService.CreateAsync(name);
    }

    /// <summary>
    /// Add songs from folder to playlist
    /// </summary>
    public async Task AddSongsFromFolderAsync(int playlistId, string folderPath)
    {
        try
        {
            var playlist = await _playlistService.GetByIdAsync(playlistId);
            if (playlist == null) return;

            var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories))
                .ToList();

            foreach (var filePath in midiFiles)
            {
                try
                {
                    var duration = TimeSpan.Zero;
                    var midiFile = _midiFileService?.LoadMidiFile(filePath);
                    if (midiFile != null)
                    {
                        duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    }

                    var song = await _songService.GetOrCreateFromFileAsync(
                        filePath,
                        Path.GetFileNameWithoutExtension(filePath),
                        "", // Artist
                        0,  // ReleaseYear
                        duration
                    );

                    // Add to playlist via service
                    await _playlistSongService.AddSongAsync(playlistId, song);
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Warning(e, $"Error adding song: {filePath}");
                }
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error adding songs from folder");
        }
    }


    /// <summary>
    /// Delete a playlist
    /// </summary>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await _playlistService.DeleteAsync(playlistId);
    }

    /// <summary>
    /// Update a song
    /// </summary>
    public async Task UpdateSongAsync(Song song)
    {
        await _songService.UpdateAsync(song);
    }

    /// <summary>
    /// Update playlist song (PlaylistSong contains Song and IsPlayed)
    /// </summary>
    public async Task UpdatePlaylistSongAsync(PlaylistSong playlistSong, int playlistId)
    {
        if (playlistSong?.Song == null) return;

        try
        {
            // Debug logging
            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistId: {playlistId}");
            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Id: {playlistSong.Song.Id}");
            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Name: {playlistSong.Song.Name}");
            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Artist: {playlistSong.Song.Artist}");
            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.Song.Rating: {playlistSong.Song.Rating}");
            DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlistSong.IsPlayed: {playlistSong.IsPlayed}");

            // 1. Update song metadata via service
            await _songService.UpdateAsync(playlistSong.Song);

            // 2. Update playlist-song state via service
            var playlist = await _playlistService.GetByIdAsync(playlistId);
            if (playlist == null) return;

            var existingPs = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == playlistSong.Song.Id);
            if (existingPs != null)
            {
                DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] existingPs found, updating IsPlayed from {existingPs.IsPlayed} to {playlistSong.IsPlayed}");

                // Update the PlaylistSong state
                existingPs.IsPlayed = playlistSong.IsPlayed;
                if (playlistSong.AddedAt != default)
                    existingPs.AddedAt = playlistSong.AddedAt;

                // Persist playlist via service
                await _playlistService.UpdateAsync(playlist);

                DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] playlist updated successfully");
            }
            else
            {
                DalamudApi.PluginLog.Warning($"[UpdatePlaylistSongAsync] existingPs NOT FOUND for songId: {playlistSong.Song.Id}");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[UpdatePlaylistSongAsync] Error updating playlist song");
        }
    }

    /// <summary>
    /// Add tag to a song
    /// </summary>
    public async Task AddTagToSongAsync(int songId, string tag)
    {
        await _songService.AddTagAsync(songId, tag);
    }

    /// <summary>
    /// Remove tag from a song
    /// </summary>
    public async Task RemoveTagFromSongAsync(int songId, string tag)
    {
        await _songService.RemoveTagAsync(songId, tag);
    }

    /// <summary>
    /// Remove tag from a song by tag ID - more efficient
    /// </summary>
    public async Task RemoveTagFromSongByIdAsync(int songId, int tagId)
    {
        await _songService.RemoveTagAsync(songId, "");  // Note: Need to update ISongService interface to support RemoveTagByIdAsync
    }

    /// <summary>
    /// Remove song from playlist
    /// </summary>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await _playlistSongService.RemoveSongAsync(playlistId, songId);
    }

    /// <summary>
    /// Get song by ID
    /// </summary>
    public async Task<Song?> GetSongByIdAsync(int songId)
    {
        return await _songService.GetByIdAsync(songId);
    }

    /// <summary>
    /// Get playlist by ID
    /// </summary>
    public async Task<Playlist.Playlist?> GetPlaylistByIdAsync(int playlistId)
    {
        return await _playlistService.GetByIdAsync(playlistId);
    }

    /// <summary>
    /// Load a playlist as the current playlist (for UI button)
    /// </summary>
    public async Task LoadPlaylistToCurrentAsync(int playlistId)
    {
        try
        {
            _currentPlaylist = await _playlistService.GetByIdAsync(playlistId);
            _currentPlayingSong = null;  // Reset song reference when loading new playlist
            if (_currentPlaylist != null)
            {
                Plugin.IpcProvider.LoadPlaylist(playlistId);
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error loading playlist {PlaylistId} to current", playlistId);
        }
    }

    /// <summary>
    /// Clear all songs from a playlist
    /// </summary>
    public async Task ClearPlaylistAsync(int playlistId)
    {
        try
        {
            await _playlistService.ClearAsync(playlistId);

            // If this is the current playlist, reload it
            if (_currentPlaylist?.Id == playlistId)
            {
                await LoadPlaylistByIdAsync(playlistId);
                Plugin.IpcProvider.LoadPlaylist(playlistId);
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error clearing playlist {PlaylistId}", playlistId);
        }
    }

    // ==================== Compatibility Methods for Sync Calls ====================

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void RemoveSync(int songIndex)
    {
        _ = RemoveSongAsync(songIndex);
    }

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void MoveSongToIndexSync(int songIndex, int targetIndex)
    {
        _ = MoveSongToIndexAsync(songIndex, targetIndex);
    }

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void ChangeSongPlayedStatusSync(int songIndex, bool isFilePlayed)
    {
        _ = ChangeSongPlayedStatusAsync(songIndex, isFilePlayed);
    }

    /// <summary>
    /// Compatibility method - calls async version
    /// </summary>
    public void ResetAllSongsPlayedStatusSync()
    {
        _ = ResetAllSongsPlayedStatusAsync();
    }

    /// <summary>
    /// Compatibility method - load last playlist (was sync, now async)
    /// </summary>
    public void LoadLastPlaylist()
    {
        _ = ReloadAsync();
    }

    public void SortBy<TKey>(Func<SongEntry, TKey>? orderBy = null, bool descending = false) where TKey : IComparable
    {
        if (orderBy == null || _currentPlaylist == null) return;
        if (_currentPlaylist.Songs == null || _currentPlaylist.Songs.Count == 0) return;

        try
        {
            // Get songs for sorting - use FilePathList to match original behavior
            var songs = FilePathList.Select((entry, index) => new { entry, index }).ToList();

            var sorted = descending
                ? songs.OrderBy(x => orderBy(x.entry)).ToList()
                : songs.OrderByDescending(x => orderBy(x.entry)).ToList();

            // Reorder playlist songs based on sorted indices
            var newPlaylistSongs = new List<PlaylistSong>();
            foreach (var item in sorted)
            {
                var oldIndex = item.index;
                if (oldIndex < _currentPlaylist.Songs.Count)
                {
                    newPlaylistSongs.Add(_currentPlaylist.Songs[oldIndex]);
                }
            }

            // Update playlist with new song order
            _currentPlaylist.Songs = newPlaylistSongs;

            // Persist via service
            _ = _playlistService.UpdateAsync(_currentPlaylist);

            // Reload from database - this will refresh UI with correct order
            _ = LoadPlaylistByIdAsync(_currentPlaylist.Id);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error sorting playlist");
        }
    }

    public void Clear()
    {
        try
        {
            if (_currentPlaylist != null)
            {
                _currentPlaylist.Songs.Clear();
                _currentPlayingSong = null;
                if (_currentPlaylist != null)
                {
                    Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
                }
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error clearing playlist");
        }
    }

    public async Task RemoveSongAsync(int songIndex)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.PartyChatCommand.SendRemoveSong(songIndex);
            return;
        }

        try
        {
            // 1. Local modification
            if (!_currentPlaylist.RemoveSongAt(songIndex))
                return;

            // Adjust current song if needed
            // If removed song was current, update reference
            if (_currentPlayingSong != null &&
                _currentPlaylist.Songs.IndexOf(_currentPlayingSong) == -1)
            {
                // Current song was removed - move to next available
                _currentPlayingSong = songIndex < _currentPlaylist.Songs.Count
                    ? _currentPlaylist.Songs[songIndex]
                    : _currentPlaylist.Songs.LastOrDefault();
            }

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.RemoveSongAsync(_currentPlaylist.Id, songIndex);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song removal");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.RemoveTrackIndex(songIndex);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error removing song at index {SongIndex}", songIndex);
        }
    }

    public Task RemoveLocal(int songIndex)
    {
        // ✅ IPC Handler: Local-only removal (NO DB persist)
        // Called by secondary clients receiving RemoveTrackIndex via IPC
        if (_currentPlaylist == null)
            return Task.CompletedTask;

        if (!_currentPlaylist.RemoveSongAt(songIndex))
            return Task.CompletedTask;

        // Adjust current song if needed
        // If removed song was current, update reference
        if (_currentPlayingSong != null &&
            _currentPlaylist.Songs.IndexOf(_currentPlayingSong) == -1)
        {
            // Current song was removed - move to next available
            _currentPlayingSong = songIndex < _currentPlaylist.Songs.Count
                ? _currentPlaylist.Songs[songIndex]
                : _currentPlaylist.Songs.LastOrDefault();
        }

        DalamudApi.PluginLog.Debug("[PlaylistManager] Removed song locally (IPC update): index {SongIndex}", songIndex);
        return Task.CompletedTask;
    }


    public async Task MoveSongToIndexAsync(int songIndex, int targetIndex)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        if (songIndex == targetIndex)
            return;

        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.PartyChatCommand.SendChangeSongOrder(songIndex, targetIndex);
            return;
        }

        try
        {
            // Clamp target index
            targetIndex = Math.Clamp(targetIndex, 0, _currentPlaylist.Songs.Count - 1);

            // 1. Local modification
            if (!_currentPlaylist.MoveSongToIndex(songIndex, targetIndex))
                return;

            // NOTE: With reference-based tracking, current song identity survives reordering
            // No need to adjust - song reference remains same, just index changes

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.ReorderSongAsync(_currentPlaylist.Id, songIndex, targetIndex);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song reorder");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.MoveSongToIndex(songIndex, targetIndex);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error moving song from {From} to {To}", songIndex, targetIndex);
        }
    }

    public Task MoveSongToIndexLocal(int songIndex, int targetIndex)
    {
        // ✅ IPC Handler: Local-only reorder (NO DB persist)
        // Called by secondary clients receiving MoveSongToIndex via IPC
        if (_currentPlaylist == null)
            return Task.CompletedTask;

        // Clamp target index
        targetIndex = Math.Clamp(targetIndex, 0, _currentPlaylist.Songs.Count - 1);

        // 1. Local modification only
        if (!_currentPlaylist.MoveSongToIndex(songIndex, targetIndex))
            return Task.CompletedTask;

        // NOTE: With reference-based tracking, current song identity survives reordering
        // No need to adjust - song reference remains same, just index changes

        DalamudApi.PluginLog.Debug("[PlaylistManager] Moved song locally (IPC update): from {FromIndex} to {ToIndex}", songIndex, targetIndex);
        return Task.CompletedTask;
    }

    public void SetCurrentSongAsPlayed()
    {
        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            var progress = Plugin.CurrentBardPlayback.GetPlaybackProgress();
            var playedThresholdPercent = 0.85;
            if (progress >= playedThresholdPercent)
            {
                // Get current song index on-demand from identity reference
                int currentIndex = GetCurrentSongIndex();
                if (currentIndex >= 0)
                {
                    _ = ChangeSongPlayedStatusAsync(currentIndex, true);
                }
            }
        }
    }

    public async Task ChangeSongPlayedStatusAsync(int songIndex, bool isFilePlayed)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        try
        {
            // 1. Local modification
            if (!_currentPlaylist.MarkSongAsPlayed(songIndex))
                return;

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.MarkSongAsPlayedAsync(_currentPlaylist.Id, songIndex);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song played status");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.ChangeSongPlayedStatus(songIndex, isFilePlayed);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error changing song played status at index {SongIndex}", songIndex);
        }
    }

    public Task ChangeSongPlayedStatusLocal(int songIndex, bool isSongPlayed)
    {
        // ✅ IPC Handler: Local-only played status update (NO DB persist)
        // Called by secondary clients receiving ChangeSongPlayedStatus via IPC
        if (_currentPlaylist == null)
            return Task.CompletedTask;

        if (!IsValidSongIndex(songIndex))
            return Task.CompletedTask;

        // 1. Local modification only
        if (!_currentPlaylist.MarkSongAsPlayed(songIndex))
            return Task.CompletedTask;

        DalamudApi.PluginLog.Debug("[PlaylistManager] Marked song as played locally (IPC update): index {SongIndex}", songIndex);
        return Task.CompletedTask;
    }

    public async Task ResetAllSongsPlayedStatusAsync()
    {
        if (_currentPlaylist == null || _currentPlaylist.Songs.Count == 0)
            return;

        try
        {
            // 1. Local modification
            _currentPlaylist.ResetAllSongsPlayedStatus();

            // 2. Persist via service
            if (_playlistSongService == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistManager] PlaylistSongService not initialized");
                return;
            }

            var success = await _playlistSongService.ResetAllSongsPlayedStatusAsync(_currentPlaylist.Id);
            if (!success)
            {
                DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist reset played status");
                return;
            }

            // 3. Notify other clients
            Plugin.IpcProvider.ResetAllSongsPlayedStatus();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistManager] Error resetting all songs played status");
        }
    }

    public async Task ResetAllSongsPlayedStatusDbAsync()
    {
        try
        {
            // Delegate to service which handles batch reset
            if (_currentPlaylist != null)
            {
                await _playlistSongService.ResetAllSongsPlayedStatusAsync(_currentPlaylist.Id);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error when resetting played status for playlist [{0}]", _currentPlaylist?.Id);
        }
    }

    /// <summary>
    /// Sync song file data - validates file path and recalculates duration
    /// </summary>
    public async Task SyncSongFileDataAsync(Song song)
    {
        if (song == null) return;

        bool updated = false;

        // Validate file path
        var hasValidFilePath = !string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath);
        if (song.HasValidFilePath != hasValidFilePath)
        {
            song.HasValidFilePath = hasValidFilePath;
            updated = true;
        }

        // Update file last modified date if file exists
        if (hasValidFilePath)
        {
            try
            {
                var fileLastModified = File.GetLastWriteTimeUtc(song.FilePath);
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
                if (song.HasValidFilePath)
                {
                    song.HasValidFilePath = false;
                    updated = true;
                }
            }
        }

        if (updated)
        {
            await _songService.UpdateAsync(song);
        }
    }


    public void ResetAllSongsPlayedStatusLocal()
    {
        // ✅ IPC Handler: Local-only reset of played status (NO DB persist)
        // Called by secondary clients receiving ResetAllSongsPlayedStatus via IPC
        if (_currentPlaylist == null)
            return;

        // 1. Local modification only - use Playlist model method
        _currentPlaylist.ResetAllSongsPlayedStatus();

        DalamudApi.PluginLog.Debug("[PlaylistManager] Reset all songs played status locally (IPC update)");
    }

    public async Task AddAsync(IEnumerable<string> filePaths)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[PlaylistManager] Cannot add songs - current playlist is invalid");
            return;
        }

        var success = 0;
        var sw = Stopwatch.StartNew();

        if (_playlistSongService == null || _songService == null || _midiFileService == null)
        {
            DalamudApi.PluginLog.Warning("[PlaylistManager] Services not initialized");
            return;
        }

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
                        "", // Artist
                        0,  // ReleaseYear
                        songLength
                    );

                    if (song == null)
                    {
                        DalamudApi.PluginLog.Warning("[PlaylistManager] Failed to create/get song from file {FilePath}", path);
                        continue;
                    }

                    // 2. Add to current playlist (local + DB)
                    var playlistSong = new PlaylistSong
                    {
                        Song = song,
                        IsPlayed = false,
                        AddedAt = DateTime.UtcNow
                    };

                    _currentPlaylist.AddSong(playlistSong);

                    var addSuccess = await _playlistSongService.AddSongAsync(_currentPlaylist.Id, song);
                    if (!addSuccess)
                    {
                        DalamudApi.PluginLog.Warning("[PlaylistManager] Failed to add song to playlist {FilePath}", path);
                        // Remove from local state if DB failed
                        _currentPlaylist.RemoveSongAt(_currentPlaylist.Songs.Count - 1);
                        continue;
                    }

                    success++;
                }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Warning(ex, "[PlaylistManager] Error when adding song");
                }
            }

            // 3. Calculate durations in parallel for songs that don't have them
            var songsToCalc = _currentPlaylist.Songs
                .Where(ps => ps.Song != null && ps.Song.Duration == default)
                .Select(ps => ps.Song!)
                .ToList();

            if (songsToCalc.Count > 0)
            {
                await _midiFileService.CalculateAllDurationsAsync(songsToCalc);
            }
        });

        // 4. Notify other clients
        Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
        DalamudApi.PluginLog.Information("[PlaylistManager] File import complete in {Elapsed} ms! success: {Success}",
            sw.Elapsed.TotalMilliseconds, success);
    }

    internal void CalculateSongDuration(int songIndex)
    {
        if (!IsValidSongIndex(songIndex)) return;

        try
        {
            if (_currentPlaylist == null || songIndex >= _currentPlaylist.Songs.Count)
                return;

            var ps = _currentPlaylist.Songs[songIndex];
            var song = ps.Song;
            if (song == null) return;

            if (!File.Exists(song.FilePath))
            {
                _ = RemoveSongAsync(songIndex);
                ImGuiUtil.AddNotification(NotificationType.Warning, $"The song file no longer exists and has been removed from the playlist");
                return;
            }

            var midiFile = _midiFileService?.LoadMidiFile(song.FilePath);
            if (midiFile != null)
            {
                song.Duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                _ = _songService.UpdateAsync(song);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, $"error when getting song duration");
        }
    }

    internal bool IsValidSongIndex(int songIndex)
    {
        var isEmptyList = _currentPlaylist == null || _currentPlaylist.Songs == null || _currentPlaylist.Songs.Count == 0;
        var isInvalidIndex = songIndex < 0 || songIndex >= (_currentPlaylist?.Songs.Count ?? 0);

        if (isEmptyList || isInvalidIndex)
            return false;

        return true;
    }

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

    public async Task<bool> LoadPlayback(int? index = null, bool startPlaying = false, bool sync = true)
    {
        if (index is int songIndex)
        {
            // Use property setter which converts index to PlaylistSong reference
            CurrentSongIndex = songIndex;
        }

        if (sync)
        {
            // Get index on-demand from current song identity reference
            Plugin.IpcProvider.LoadPlayback(GetCurrentSongIndex());
        }

        if (await LoadPlaybackPrivate())
        {
            if (startPlaying)
            {
                Plugin.MidiPlayerControl.DoPlay();
            }

            return true;
        }

        return false;
    }

    public static string ExtractSongName(string input, string capturePattern, string capturedOutputReplacement, string findPattern, string replacement)
    {
        if (string.IsNullOrEmpty(capturePattern) || string.IsNullOrEmpty(capturedOutputReplacement))
            return input;

        try
        {
            return Regex.Replace(input, capturePattern, match =>
            {
                string result = capturedOutputReplacement;

                for (int i = match.Groups.Count - 1; i >= 1; i--)
                {
                    result = result.Replace($"${i}", match.Groups[i].Value);
                }

                result = Regex.Replace(result, @"\$\d+", "");

                if (!string.IsNullOrEmpty(findPattern))
                {
                    result = Regex.Replace(result, findPattern, replacement);
                }

                return result;
            });
        }
        catch
        {
            return input;
        }
    }

    public string GetPostSongName(int songIndex)
    {
        if (!IsValidSongIndex(songIndex))
        {
            return string.Empty;
        }

        if (_currentPlaylist == null || songIndex >= _currentPlaylist.Songs.Count)
            return string.Empty;

        var ps = _currentPlaylist.Songs[songIndex];
        var song = ps.Song;
        if (song == null) return string.Empty;

        var songName = ExtractSongName(
            song.Name ?? Path.GetFileName(song.FilePath),
            Plugin.Config.postSongNameCaptureRegex,
            Plugin.Config.postSongNameCaptureOutputFormat,
            Plugin.Config.postSongNameFindRegex,
            Plugin.Config.postSongNameReplacement);

        return songName;
    }

    public int FindSongIndex(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName) || _currentPlaylist == null)
            return -1;

        return _currentPlaylist.Songs.FindIndex(ps =>
            (ps.Song?.Name ?? Path.GetFileName(ps.Song?.FilePath ?? "")).Contains(songName, StringComparison.OrdinalIgnoreCase)
        );
    }

    public void SendSongToChat(int songIndex)
    {
        if (DalamudApi.PartyList.IsInParty() && !DalamudApi.PartyList.IsPartyLeader()) return;
        if (!IsValidSongIndex(songIndex)) return;

        if (Plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Paused)
        {
            var songName = GetPostSongName(songIndex);
            if (songName == "") return;

            var chatComand = Plugin.Config.GetChatCommand(Plugin.Config.SongNameChatTarget);

            var chatText = $"{chatComand}{songName}";
            Chat.SendMessage(chatText);
        }
    }

    private async Task<bool> LoadPlaybackPrivate()
    {
        try
        {
            // Use song identity reference instead of index
            if (_currentPlayingSong == null)
                return false;

            var song = _currentPlayingSong.Song;
            if (song == null) return false;

            return await Plugin.FilePlayback.LoadPlayback(song.FilePath);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
            return false;
        }
    }
}
