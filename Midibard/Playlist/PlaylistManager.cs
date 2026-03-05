using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Playlist;
using MidiBard.Playlist.Helpers;
using MidiBard.Util;

namespace MidiBard;

internal class PlaylistManager
{
    private Plugin Plugin { get; }

    private Playlist.Playlist? _currentPlaylist;

    // Helper instances (composition pattern)
    private readonly CurrentSongController _currentSongController;
    private readonly PlaylistCrudHelper _crudHelper;
    private readonly PlaylistSongStateManager _stateManager;
    private readonly SongFileOperationHelper _fileHelper;
    private readonly SongMetadataManager _metadataManager;
    private readonly PlaylistUiHelper _uiHelper;

    public PlaylistSong? CurrentPlayingSong => _currentSongController.CurrentPlayingSong;

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
    public async Task<Playlist.Playlist> CreatePlaylistAsync(string name)
    {
        return await ServiceContainer.PlaylistService.CreateAsync(name);
    }

    /// <summary>
    /// Add songs from folder to playlist
    /// </summary>
    public async Task AddSongsFromFolderAsync(int playlistId, string folderPath)
    {
        try
        {
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
            if (playlist == null) return;

            var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories))
                .ToList();

            // Delegate to file helper
            await _fileHelper.AddSongsAsync(playlist, midiFiles.AsEnumerable());
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
        await ServiceContainer.PlaylistService.DeleteAsync(playlistId);
    }

    /// <summary>
    /// Update a song
    /// </summary>
    public async Task UpdateSongAsync(Song song)
    {
        await _metadataManager.UpdateSongAsync(song);
    }

    /// <summary>
    /// Update playlist song (PlaylistSong contains Song and IsPlayed)
    /// </summary>
    public async Task UpdatePlaylistSongAsync(PlaylistSong playlistSong, int playlistId)
    {
        await _metadataManager.UpdatePlaylistSongAsync(playlistSong, playlistId);
    }

    /// <summary>
    /// Add tag to a song
    /// </summary>
    public async Task AddTagToSongAsync(int songId, string tag)
    {
        await _metadataManager.AddTagToSongAsync(songId, tag);
    }

    /// <summary>
    /// Remove tag from a song
    /// </summary>
    public async Task RemoveTagFromSongAsync(int songId, string tag)
    {
        await _metadataManager.RemoveTagFromSongAsync(songId, tag);
    }

    /// <summary>
    /// Remove tag from a song by tag ID - more efficient
    /// </summary>
    public async Task RemoveTagFromSongByIdAsync(int songId, int tagId)
    {
        await _metadataManager.RemoveTagFromSongByIdAsync(songId, tagId);
    }

    /// <summary>
    /// Remove song from playlist
    /// </summary>
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        await ServiceContainer.PlaylistSongService.RemoveSongAsync(playlistId, songId);
    }

    /// <summary>
    /// Get song by ID
    /// </summary>
    public async Task<Song?> GetSongByIdAsync(int songId)
    {
        return await ServiceContainer.SongService.GetByIdAsync(songId);
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
    public async Task LoadPlaylistToCurrentAsync(int playlistId)
    {
        try
        {
            var playlist = await _crudHelper.LoadPlaylistToCurrentAsync(playlistId);
            if (playlist != null)
            {
                _currentPlaylist = playlist;
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error loading playlist {playlistId} to current");
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

    public void SortBy<TKey>(Func<PlaylistSong, TKey>? orderBy = null, bool descending = false) where TKey : IComparable
    {
        if (orderBy == null || _currentPlaylist == null) return;
        if (_currentPlaylist.Songs == null || _currentPlaylist.Songs.Count == 0) return;

        try
        {
            // 1. Local mutation: Use model's SortBy method directly
            _currentPlaylist.SortBy(orderBy, descending);

            // 2. Persist via service (replaces expensive DB reload)
            _ = ServiceContainer.PlaylistService.UpdateAsync(_currentPlaylist);

            // 3. Broadcast to other clients via IPC
            Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);

            DalamudApi.PluginLog.Debug("[PlaylistManager] Playlist sorted successfully");
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
                _currentSongController.Clear();
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
            Plugin.ChatWatcher.SendRemoveSong(songIndex);
            return;
        }

        // Delegate to state manager (handles local > persist > broadcast)
        await _stateManager.RemoveSongAsync(_currentPlaylist, songIndex);
    }

    public Task RemoveLocal(int songIndex)
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

        var pmdUseChatPlaylistSync = Plugin.Config.playOnMultipleDevices && Plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            Plugin.ChatWatcher.SendChangeSongOrder(songIndex, targetIndex);
            return;
        }

        // Delegate to state manager (handles local > persist > broadcast)
        await _stateManager.MoveSongToIndexAsync(_currentPlaylist, songIndex, targetIndex);
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
                // Use identity reference instead of index lookup (O(1) vs O(n))
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

        // Delegate to state manager (handles local > persist > broadcast)
        await _stateManager.ChangeSongPlayedStatusAsync(_currentPlaylist, songIndex, isFilePlayed);
    }


    public async Task ResetAllSongsPlayedStatusAsync()
    {
        if (_currentPlaylist == null || _currentPlaylist.Songs.Count == 0)
            return;

        // Delegate to state manager (handles local > persist > broadcast)
        await _stateManager.ResetAllSongsPlayedStatusAsync(_currentPlaylist);
    }

    /// <summary>
    /// Local-only update for IPC handlers: update played status without DB persist or broadcast
    /// </summary>
    public Task ChangeSongPlayedStatusLocal(int songIndex, bool isSongPlayed)
    {
        if (_currentPlaylist == null)
            return Task.CompletedTask;

        if (!IsValidSongIndex(songIndex))
            return Task.CompletedTask;

        // Delegate to state manager with flags for local-only update (no DB, no broadcast)
        return _stateManager.ChangeSongPlayedStatusAsync(
            _currentPlaylist, songIndex, isSongPlayed, persistToDb: false, broadcastToIpc: false);
    }

    /// <summary>
    /// Local-only update for IPC handlers: reset played status without DB persist or broadcast
    /// </summary>
    public Task ResetAllSongsPlayedStatusLocal()
    {
        if (_currentPlaylist == null)
            return Task.CompletedTask;

        // Delegate to state manager with flags for local-only update (no DB, no broadcast)
        return _stateManager.ResetAllSongsPlayedStatusAsync(
            _currentPlaylist, persistToDb: false, broadcastToIpc: false);
    }

    public async Task ResetAllSongsPlayedStatusDbAsync()
    {
        try
        {
            // Delegate to service which handles batch reset
            if (_currentPlaylist != null)
            {
                await ServiceContainer.PlaylistSongService.ResetAllSongsPlayedStatusAsync(_currentPlaylist.Id);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, $"error when resetting played status for playlist [{_currentPlaylist?.Id}]");
        }
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


    public async Task AddAsync(IEnumerable<string> filePaths)
    {
        if (_currentPlaylist == null || !_currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[PlaylistManager] Cannot add songs - current playlist is invalid");
            return;
        }

        // Delegate to file helper (handles file validation, creation, DB persistence)
        await _fileHelper.AddSongsAsync(_currentPlaylist, filePaths);

        // Notify other clients after file helper completes
        Plugin.IpcProvider.LoadPlaylist(_currentPlaylist.Id);
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

            var midiFile = ServiceContainer.MidiFileService.LoadMidiFile(song.FilePath);
            if (midiFile != null)
            {
                song.Duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                _ = ServiceContainer.SongService.UpdateAsync(song);
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

    public string GetPostSongName()
    {
        var song = _currentSongController.CurrentPlayingSong?.Song;
        if (song == null) return string.Empty;

        return song.GetFormattedName(
            Plugin.Config.postSongNameCaptureRegex,
            Plugin.Config.postSongNameCaptureOutputFormat,
            Plugin.Config.postSongNameFindRegex,
            Plugin.Config.postSongNameReplacement);
    }

    /// <summary>
    /// Get the formatted name for a specific playlist song at given index.
    /// Used when you need formatting for a song other than the currently playing one.
    /// </summary>
    public string GetPostSongName(int songIndex)
    {
        if (!IsValidSongIndex(songIndex))
            return string.Empty;

        var song = _currentPlaylist?.Songs[songIndex].Song;
        if (song == null) return string.Empty;

        return song.GetFormattedName(
            Plugin.Config.postSongNameCaptureRegex,
            Plugin.Config.postSongNameCaptureOutputFormat,
            Plugin.Config.postSongNameFindRegex,
            Plugin.Config.postSongNameReplacement);
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

            return await Plugin.FilePlayback.LoadPlayback(song.FilePath);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
            return false;
        }
    }
}
