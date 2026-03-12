using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Playlist;
using MidiBard.Playlist.Helpers;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

internal class PlaylistManager
{
    private Plugin Plugin { get; }

    private Playlist.Playlist? _currentPlaylist;

    private readonly CurrentSongController _currentSongController;
    private readonly PlaylistCrudHelper _crudHelper;
    private readonly PlaylistSongStateManager _stateManager;
    private readonly SongFileOperationHelper _fileHelper;
    private readonly SongMetadataManager _metadataManager;
    private readonly PlaylistUiHelper _uiHelper;

    public PlaylistSong? CurrentPlayingSong => _currentSongController.CurrentPlayingSong;

    // Guard against double-increment when Playback_Finished and Stop() both call SetCurrentSongAsPlayed
    // concurrently (e.g. user clicks Stop during PerformWaiting after song finishes naturally).
    private volatile bool _currentSongPlayedMarked = false;

    public Playlist.Playlist? CurrentPlaylist
    {
        get => _currentPlaylist;
        set
        {
            _currentPlaylist = value;
            if (_currentPlaylist != null && !_currentPlaylist.IsTemp)
                Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
        }
    }

    public int CurrentSongIndex
    {
        get => _currentSongController.GetCurrentSongIndex(_currentPlaylist);
        set
        {
            if (value < 0 || _currentPlaylist == null || value >= _currentPlaylist.Songs.Count)
            {
                _currentSongController.Clear();
                return;
            }
            _currentSongController.SetCurrentSongByIndex(value, _currentPlaylist);
        }
    }

    /// <summary>
    /// Get the current song index based on identity reference (not stored directly).
    /// Returns -1 if no song is currently playing.
    /// </summary>
    public int GetCurrentSongIndex()
    {
        return _currentSongController.GetCurrentSongIndex(_currentPlaylist);
    }

    public PlaylistManager(Plugin plugin)
    {
        Plugin = plugin;

        // Initialize helpers (composition pattern)
        _currentSongController = new CurrentSongController();

        _crudHelper = new PlaylistCrudHelper(
            _currentSongController,
            OnPlaylistLoaded);

        _stateManager = new PlaylistSongStateManager(
            _currentSongController,
            Plugin,
            BroadcastToIpc);

        _fileHelper = new SongFileOperationHelper(
            _currentSongController,
            RemoveSongAsync);

        _metadataManager = new SongMetadataManager();

        _uiHelper = new PlaylistUiHelper(
            _currentSongController,
            Plugin,
            async (index) => await LoadPlaybackPrivate());

        DalamudApi.PluginLog.Debug("[PlaylistManager] All helpers initialized");

        // Load last used playlist
        _ = LoadLastPlaylistAsync();
    }

    // ==================== Helper Callbacks ====================

    private void OnPlaylistLoaded(int playlistId)
    {
        Plugin.IpcProvider.LoadPlaylist(playlistId);
    }

    private void BroadcastToIpc(Action ipcAction)
    {
        ipcAction?.Invoke();
    }

    /// <summary>
    /// Reload the current playlist from database
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_currentPlaylist != null && !_currentPlaylist.IsTemp)
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
            var playlist = await _crudHelper.LoadLastPlaylistAsync();
            if (playlist != null)
            {
                _currentPlaylist = playlist;
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
            var playlist = await _crudHelper.LoadPlaylistByIdAsync(playlistId);
            if (playlist != null)
            {
                _currentPlaylist = playlist;
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error loading playlist {playlistId}");
        }
    }

    /// <summary>
    /// Load an in-memory temporary playlist from file paths. Not persisted to DB.
    /// If a temp playlist is already active, appends the new songs to it.
    /// </summary>
    public async Task LoadTempPlaylistAsync(IEnumerable<string> filePaths)
    {
        // Append to existing temp playlist, or create a fresh one
        var target = (_currentPlaylist?.IsTemp == true)
            ? _currentPlaylist
            : new Playlist.Playlist { Id = 0, Name = "Quick Load", IsTemp = true };

        var existingPaths = target.Songs
            .Where(ps => ps.Song != null)
            .Select(ps => ps.Song.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newPaths = filePaths
            .Where(p => File.Exists(p) && existingPaths.Add(p))
            .ToList();

        if (newPaths.Count == 0) return;

        var newSongs = await Task.Run(() => newPaths.Select(filePath =>
        {
            var duration = ServiceContainer.MidiFileService.LoadMidiFile(filePath)?.GetDurationTimeSpan() ?? TimeSpan.Zero;
            return new PlaylistSong
            {
                Song = new Song
                {
                    FilePath = filePath,
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Duration = duration,
                },
                IsPlayed = false,
            };
        }).ToList());

        target.Songs.AddRange(newSongs);

        if (!ReferenceEquals(_currentPlaylist, target))
        {
            _currentPlaylist = target;
            _currentSongController.Clear();
        }

        DalamudApi.PluginLog.Debug($"[PlaylistManager] Quick Load: +{newSongs.Count} songs (total {target.Songs.Count})");
    }

    /// <summary>
    /// Switch to a different playlist
    /// </summary>
    public async Task SwitchToPlaylistAsync(int playlistId)
    {
        await LoadPlaylistByIdAsync(playlistId);
        _currentSongController.Clear();
        if (_currentPlaylist != null)
            Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
    }

    /// <summary>
    /// Get all playlists (for UI)
    /// </summary>
    public async Task<List<Playlist.Playlist>> GetAllPlaylistsAsync()
    {
        return await ServiceContainer.PlaylistService.GetAllAsync();
    }

    /// <summary>
    /// Get songs for a specific playlist (for UI)
    /// </summary>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        return await _uiHelper.GetPlaylistSongsAsync(playlistId);
    }

    /// <summary>
    /// Create a new playlist
    /// </summary>
    public async Task<Playlist.Playlist?> CreatePlaylistAsync(string name)
    {
        return await ServiceContainer.PlaylistService.CreateAsync(name);
    }

    /// <summary>
    /// Delete a playlist
    /// </summary>
    public async Task DeletePlaylistAsync(int playlistId)
    {
        await ServiceContainer.PlaylistService.DeleteAsync(playlistId);
    }

    /// <summary>
    /// Update playlist metadata (e.g. rename).
    /// </summary>
    public async Task<bool> UpdatePlaylistAsync(Playlist.Playlist playlist)
    {
        return await ServiceContainer.PlaylistService.UpdateAsync(playlist);
    }

    /// <summary>
    /// Remove song from playlist
    /// </summary>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await ServiceContainer.PlaylistSongService.RemoveSongAsync(playlistId, songId);
    }

    /// <summary>
    /// Get playlist by ID
    /// </summary>
    public async Task<Playlist.Playlist?> GetPlaylistByIdAsync(int playlistId)
    {
        return await _uiHelper.GetPlaylistByIdAsync(playlistId);
    }

    /// <summary>
    /// Load a playlist as the current playlist (for UI button)
    /// </summary>
    public async Task<Playlist.Playlist?> LoadPlaylistToCurrentAsync(int playlistId)
    {
        try
        {
            var playlist = await _crudHelper.LoadPlaylistToCurrentAsync(playlistId);
            if (playlist != null)
            {
                CurrentPlaylist = playlist;
            }
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error loading playlist {playlistId} to current");
            return null;
        }
    }

    /// <summary>
    /// Clear all songs from a playlist
    /// </summary>
    public async Task ClearPlaylistAsync(int playlistId)
    {
        try
        {
            await ServiceContainer.PlaylistService.ClearAsync(playlistId);

            // If this is the current playlist, reload it
            if (_currentPlaylist?.Id == playlistId)
            {
                await LoadPlaylistByIdAsync(playlistId);
                Plugin.IpcProvider.LoadPlaylist(playlistId);
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error clearing playlist {playlistId}");
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
    public void ChangeSongPlayedStatusSync(int songIndex, bool isFilePlayed, bool incrementPlayCount = false)
    {
        _ = ChangeSongPlayedStatusAsync(songIndex, isFilePlayed, incrementPlayCount);
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

    public void SortBy<TKey>(Func<PlaylistSong, TKey>? orderBy = null, bool descending = false) where TKey : IComparable
    {
        if (orderBy == null || _currentPlaylist == null) return;
        if (_currentPlaylist.Songs == null || _currentPlaylist.Songs.Count == 0) return;

        if (_currentPlaylist.IsTemp)
        {
            _currentPlaylist.SortBy(ps => (IComparable)orderBy(ps), descending);
            return;
        }

        _ = _stateManager.SortByAsync(_currentPlaylist, ps => (IComparable)orderBy(ps), descending);
    }

    public void Clear()
    {
        if (_currentPlaylist?.IsTemp == true)
        {
            _currentPlaylist.Songs.Clear();
            _currentSongController.Clear();
            return;
        }
        _stateManager.ClearLocal(_currentPlaylist);
    }

    public async Task RemoveSongAsync(int songIndex)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(songIndex))
            return;

        var pmdUseChatPlaylistSync = !(_currentPlaylist?.IsTemp == true) && Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.ChatWatcher.SendRemoveSong(songIndex);
            return;
        }

        // Delegate to state manager (handles local > persist > broadcast)
        await _stateManager.RemoveSongAsync(_currentPlaylist, songIndex, persistToDb: !(_currentPlaylist?.IsTemp == true));
    }

    public Task RemoveSongLocal(int songIndex)
    {
        // IPC Handler: Local-only removal (NO DB persist)
        // Called by secondary clients receiving RemoveTrackIndex via IPC
        if (_currentPlaylist == null)
            return Task.CompletedTask;

        // Capture the song being removed to check later if it was current (O(1) identity check)
        if (songIndex < 0 || songIndex >= _currentPlaylist.Songs.Count)
            return Task.CompletedTask;

        PlaylistSong? removedSong = _currentPlaylist.Songs[songIndex];

        if (!_currentPlaylist.RemoveSongAt(songIndex))
            return Task.CompletedTask;

        // Adjust current song if needed
        // If removed song was current, update reference (O(1) identity comparison)
        if (_currentSongController.CurrentPlayingSong == removedSong)
        {
            // Current song was removed - move to next available
            var nextSong = songIndex < _currentPlaylist.Songs.Count
                ? _currentPlaylist.Songs[songIndex]
                : _currentPlaylist.Songs.LastOrDefault();
            _currentSongController.CurrentPlayingSong = nextSong;
        }

        DalamudApi.PluginLog.Debug($"[PlaylistManager] Removed song locally (IPC update): index {songIndex}");
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

        var pmdUseChatPlaylistSync = !(_currentPlaylist?.IsTemp == true) && Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.ChatWatcher.SendChangeSongOrder(songIndex, targetIndex);
            return;
        }

        // Delegate to state manager (handles local > persist > broadcast)
        await _stateManager.MoveSongToIndexAsync(_currentPlaylist, songIndex, targetIndex, persistToDb: !(_currentPlaylist?.IsTemp == true));
    }

    public Task MoveSongToIndexLocal(int songIndex, int targetIndex)
    {
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

        DalamudApi.PluginLog.Debug($"[PlaylistManager] Moved song locally (IPC update): from {songIndex} to {targetIndex}");
        return Task.CompletedTask;
    }

    public void SetCurrentSongAsPlayed()
    {
        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            var progress = Plugin.CurrentBardPlayback.GetPlaybackProgress();
            var playedThresholdPercent = 0.85;
            if (progress >= playedThresholdPercent && _currentSongController.CurrentPlayingSong != null)
            {
                // Guard: prevent double-increment when Playback_Finished and Stop() race
                // (e.g. user stops during PerformWaiting after song completes naturally)
                if (_currentSongPlayedMarked) return;
                _currentSongPlayedMarked = true;

                // Use identity reference instead of index lookup (O(1) vs O(n))
                int currentIndex = GetCurrentSongIndex();
                if (currentIndex >= 0)
                {
                    _ = ChangeSongPlayedStatusAsync(currentIndex, true, incrementPlayCount: true);
                }
            }
        }
    }

    /// <summary>
    /// Full update: modifies song in-memory, persists to DB, and broadcasts via IPC.
    /// </summary>
    public async Task ChangeSongPlayedStatusAsync(int songIndex, bool isFilePlayed, bool incrementPlayCount = false)
    {
        await _stateManager.ChangeSongPlayedStatusAsync(
            _currentPlaylist, songIndex, isFilePlayed,
            persistToDb: !(_currentPlaylist?.IsTemp == true),
            incrementPlayCount: incrementPlayCount);
    }


    /// <summary>
    /// Full update: resets all songs played status in-memory, persists to DB, and broadcasts via IPC.
    /// </summary>
    public async Task ResetAllSongsPlayedStatusAsync()
    {
        await _stateManager.ResetAllSongsPlayedStatusAsync(
            _currentPlaylist,
            persistToDb: !(_currentPlaylist?.IsTemp == true));
    }

    /// <summary>
    /// IPC handler: update played status locally only - no DB persist, no broadcast.
    /// Called when a secondary client receives a ChangeSongPlayedStatus IPC message.
    /// </summary>
    public Task ChangeSongPlayedStatusLocal(int songIndex, bool isSongPlayed)
    {
        return _stateManager.ChangeSongPlayedStatusAsync(
            _currentPlaylist, songIndex, isSongPlayed, persistToDb: false, broadcastToIpc: false);
    }

    /// <summary>
    /// IPC handler: reset all played statuses locally only - no DB persist, no broadcast.
    /// Called when a secondary client receives a ResetAllSongsPlayedStatus IPC message.
    /// </summary>
    public Task ResetAllSongsPlayedStatusLocal()
    {
        return _stateManager.ResetAllSongsPlayedStatusAsync(
            _currentPlaylist, persistToDb: false, broadcastToIpc: false);
    }

    /// <summary>
    /// Sync song file data - validates file path and recalculates duration
    /// </summary>
    public async Task SyncSongFileDataAsync(Song song)
    {
        if (song == null) return;

        // Delegate to file helper (handles validation, metadata sync, and persistence)
        await _fileHelper.SyncSongFileDataAsync(song);
    }

    /// <summary>
    /// Computes updated file data for a song in-memory without persisting.
    /// Returns true if any field changed. Use with BulkUpdateAsync for batch saves.
    /// </summary>
    public Task<bool> ComputeSyncSongFileDataAsync(Song song)
    {
        return _fileHelper.ComputeSyncFileDataAsync(song);
    }

    public async Task AddSongsAsync(IEnumerable<string> filePaths)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[PlaylistManager] Cannot add songs - current playlist is invalid");
            return;
        }

        if (_currentPlaylist.IsTemp)
        {
            // Temp playlist: add songs directly in memory, no DB writes
            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath)) continue;
                var song = new Song
                {
                    FilePath = filePath,
                    Name = Path.GetFileNameWithoutExtension(filePath),
                };
                _currentPlaylist.Songs.Add(new PlaylistSong { Song = song });
            }
            return;
        }

        // Delegate to file helper (handles file validation, creation, DB persistence)
        await _fileHelper.AddSongsAsync(_currentPlaylist, filePaths);

        // Notify other clients after file helper completes
        Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
    }

    internal bool IsValidSongIndex(int songIndex) => _currentPlaylist.IsValidSongIndex(songIndex);

    public string GetCurrentSongName() => _uiHelper.GetPostSongName();

    /// <summary>
    /// Get the formatted name for a specific playlist song at given index.
    /// Used when you need formatting for a song other than the currently playing one.
    /// </summary>
    public string GetPostSongName(int songIndex) => _uiHelper.GetPostSongName(songIndex, _currentPlaylist);

    public int FindSongIndex(string songName) => _uiHelper.FindSongIndex(songName, _currentPlaylist);

    public void SendSongToChat(int songIndex) => _uiHelper.SendSongToChat(songIndex, _currentPlaylist);

    public async Task<bool> LoadPlayback(int? index = null, bool startPlaying = false, bool sync = true)
    {
        int? playbackIndex = index;

        if (index is int songIndex)
        {
            // Use property setter which converts index to PlaylistSong reference
            CurrentSongIndex = songIndex;
        }
        else if (_currentSongController.CurrentPlayingSong != null && _currentPlaylist != null)
        {
            // Calculate index once instead of on-demand lookup in GetCurrentSongIndex
            playbackIndex = _currentPlaylist.Songs.IndexOf(_currentSongController.CurrentPlayingSong);
        }

        if (sync && playbackIndex >= 0)
        {
            // Use pre-calculated index instead of calling GetCurrentSongIndex() (O(n) lookup)
            Plugin.IpcProvider.LoadPlayback(playbackIndex.Value);
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

    private async Task<bool> LoadPlaybackPrivate()
    {
        try
        {
            // Use song identity reference instead of index
            if (_currentSongController.CurrentPlayingSong == null)
                return false;

            var song = _currentSongController.CurrentPlayingSong.Song;
            if (song == null) return false;

            // Reset played-mark guard so the new song can be marked played when it finishes
            _currentSongPlayedMarked = false;

            return await Plugin.FilePlayback.LoadPlayback(song.FilePath);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
            return false;
        }
    }
}
