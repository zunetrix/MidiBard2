using System;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Playlist.Services;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages song state within playlists: add, remove, reorder, mark played.
/// Consolidates local mutations, DB persistence, and IPC broadcasting with flags.
/// </summary>
internal class PlaylistSongStateManager
{
    private readonly IPlaylistSongService? _playlistSongService;
    private readonly ISongService? _songService;
    private readonly IMidiFileService? _midiFileService;
    private readonly CurrentSongController _songController;
    private readonly Plugin _plugin;

    // Callback for IPC broadcast
    private readonly Action<Action> _broadcastToIpc; // Delegate: pass closure with IPC call

    public PlaylistSongStateManager(
        IPlaylistSongService? playlistSongService,
        ISongService? songService,
        IMidiFileService? midiFileService,
        CurrentSongController songController,
        Plugin plugin,
        Action<Action> broadcastToIpc)
    {
        _playlistSongService = playlistSongService;
        _songService = songService;
        _midiFileService = midiFileService;
        _songController = songController;
        _plugin = plugin;
        _broadcastToIpc = broadcastToIpc;
    }

    /// <summary>
    /// Remove song at index from current playlist.
    /// </summary>
    public async Task RemoveSongAsync(Playlist? currentPlaylist, int songIndex, bool persistToDb = true, bool broadcastToIpc = true)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(currentPlaylist, songIndex))
            return;

        // Check for party chat sync
        var pmdUseChatPlaylistSync = _plugin.Config.playOnMultipleDevices && _plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            _plugin.ChatWatcher.SendRemoveSong(songIndex);
            return;
        }

        try
        {
            // Capture the song being removed to check later if it was current (O(1) identity check)
            PlaylistSong? removedSong = currentPlaylist.Songs[songIndex];
            var removedSongId = removedSong.Song?.Id;

            // 1. Local modification
            if (!currentPlaylist.RemoveSongAt(songIndex))
                return;

            // Adjust current song if needed
            if (_songController.CurrentPlayingSong == removedSong)
            {
                // Current song was removed - move to next available
                _songController.CurrentPlayingSong = songIndex < currentPlaylist.Songs.Count
                    ? currentPlaylist.Songs[songIndex]
                    : currentPlaylist.Songs.LastOrDefault();
            }

            // 2. Persist via service if requested
            if (persistToDb && _playlistSongService != null && removedSongId.HasValue)
            {
                var success = await _playlistSongService.RemoveSongAsync(currentPlaylist.Id, removedSongId.Value);
                if (!success)
                {
                    DalamudApi.PluginLog.Error("[PlaylistManager] Failed to persist song removal");
                    return;
                }
            }

            // 3. Notify other clients if requested
            if (broadcastToIpc)
            {
                _broadcastToIpc(() => _plugin.IpcProvider.RemoveTrackIndex(songIndex));
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongStateManager] Error removing song at index {songIndex}");
        }
    }

    /// <summary>
    /// Move song from one index to another.
    /// </summary>
    public async Task MoveSongToIndexAsync(Playlist? currentPlaylist, int songIndex, int targetIndex, bool persistToDb = true, bool broadcastToIpc = true)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(currentPlaylist, songIndex))
            return;

        if (songIndex == targetIndex)
            return;

        // Check for party chat sync
        var pmdUseChatPlaylistSync = _plugin.Config.playOnMultipleDevices && _plugin.Config.useChatPlaylistSync && DalamudApi.PartyList.Length > 1;
        if (pmdUseChatPlaylistSync)
        {
            _plugin.ChatWatcher.SendChangeSongOrder(songIndex, targetIndex);
            return;
        }

        try
        {
            // Clamp target index
            targetIndex = Math.Clamp(targetIndex, 0, currentPlaylist.Songs.Count - 1);

            // 1. Local modification
            if (!currentPlaylist.MoveSongToIndex(songIndex, targetIndex))
                return;

            // With reference-based tracking, current song identity survives reordering

            // 2. Persist via service if requested
            if (persistToDb && _playlistSongService != null)
            {
                var success = await _playlistSongService.ReorderSongAsync(currentPlaylist.Id, songIndex, targetIndex);
                if (!success)
                {
                    DalamudApi.PluginLog.Error("[PlaylistSongStateManager] Failed to persist song reorder");
                    return;
                }
            }

            // 3. Notify other clients if requested
            if (broadcastToIpc)
            {
                _broadcastToIpc(() => _plugin.IpcProvider.MoveSongToIndex(songIndex, targetIndex));
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongStateManager] Error moving song from {songIndex} to {targetIndex}");
        }
    }

    /// <summary>
    /// Mark song as played (or unplayed).
    /// </summary>
    public async Task ChangeSongPlayedStatusAsync(Playlist? currentPlaylist, int songIndex, bool isFilePlayed, bool persistToDb = true, bool broadcastToIpc = true)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValid)
            return;

        if (!IsValidSongIndex(currentPlaylist, songIndex))
            return;

        try
        {
            // 1. Local modification
            if (!currentPlaylist.MarkSongAsPlayed(songIndex))
                return;

            // 2. Persist via service if requested
            if (persistToDb && _playlistSongService != null)
            {
                var success = await _playlistSongService.MarkSongAsPlayedAsync(currentPlaylist.Id, songIndex);
                if (!success)
                {
                    DalamudApi.PluginLog.Error("[PlaylistSongStateManager] Failed to persist song played status");
                    return;
                }
            }

            // 3. Notify other clients if requested
            if (broadcastToIpc)
            {
                _broadcastToIpc(() => _plugin.IpcProvider.ChangeSongPlayedStatus(songIndex, isFilePlayed));
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongStateManager] Error changing song played status at index {songIndex}");
        }
    }

    /// <summary>
    /// Reset all songs' played status.
    /// </summary>
    public async Task ResetAllSongsPlayedStatusAsync(Playlist? currentPlaylist, bool persistToDb = true, bool broadcastToIpc = true)
    {
        if (currentPlaylist == null || currentPlaylist.Songs.Count == 0)
            return;

        try
        {
            // 1. Local modification
            currentPlaylist.ResetAllSongsPlayedStatus();

            // 2. Persist via service if requested
            if (persistToDb && _playlistSongService != null)
            {
                var success = await _playlistSongService.ResetAllSongsPlayedStatusAsync(currentPlaylist.Id);
                if (!success)
                {
                    DalamudApi.PluginLog.Error("[PlaylistSongStateManager] Failed to persist reset played status");
                    return;
                }
            }

            // 3. Notify other clients if requested
            if (broadcastToIpc)
            {
                _broadcastToIpc(() => _plugin.IpcProvider.ResetAllSongsPlayedStatus());
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistSongStateManager] Error resetting all songs played status");
        }
    }

    /// <summary>
    /// Sort playlist by custom key function.
    /// </summary>
    public async Task SortByAsync(Playlist? currentPlaylist, Func<PlaylistSong, IComparable>? orderBy = null, bool descending = false, IPlaylistService? playlistService = null)
    {
        if (orderBy == null || currentPlaylist == null) return;
        if (currentPlaylist.Songs == null || currentPlaylist.Songs.Count == 0) return;

        try
        {
            // 1. Local mutation
            currentPlaylist.SortBy(orderBy, descending);

            // 2. Persist via service
            if (playlistService != null)
                await playlistService.UpdateAsync(currentPlaylist);

            // 3. Broadcast to other clients
            _broadcastToIpc(() => _plugin.IpcProvider.LoadPlaylist(currentPlaylist.Id));

            DalamudApi.PluginLog.Debug("[PlaylistSongStateManager] Playlist sorted successfully");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error sorting playlist");
        }
    }

    /// <summary>
    /// Clear all songs from playlist locally and optionally persist.
    /// </summary>
    public void ClearLocal(Playlist? currentPlaylist)
    {
        try
        {
            if (currentPlaylist != null)
            {
                currentPlaylist.Songs.Clear();
                _songController.Clear();
                _broadcastToIpc(() => _plugin.IpcProvider.LoadPlaylist(currentPlaylist.Id));
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error clearing playlist");
        }
    }

    /// <summary>
    /// Set current song as played if played threshold is reached.
    /// </summary>
    public void SetCurrentSongAsPlayed()
    {
        if (_plugin.CurrentBardPlayback.IsLoaded && _songController.CurrentPlayingSong != null)
        {
            var progress = _plugin.CurrentBardPlayback.GetPlaybackProgress();
            var playedThresholdPercent = 0.85;
            if (progress >= playedThresholdPercent)
            {
                // Use identity reference instead of index lookup
                int currentIndex = _songController.GetCurrentSongIndex(_plugin.PlaylistManager.CurrentPlaylist);
                if (currentIndex >= 0)
                {
                    _ = ChangeSongPlayedStatusAsync(_plugin.PlaylistManager.CurrentPlaylist, currentIndex, true);
                }
            }
        }
    }

    // ==================== Helper Methods ====================

    private bool IsValidSongIndex(Playlist playlist, int songIndex)
    {
        var isEmptyList = playlist == null || playlist.Songs == null || playlist.Songs.Count == 0;
        var isInvalidIndex = songIndex < 0 || songIndex >= (playlist?.Songs.Count ?? 0);

        return !isEmptyList && !isInvalidIndex;
    }
}
