using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages playlist CRUD operations (Create, Read, Update, Delete).
/// Delegates to IPlaylistService for persistence.
/// </summary>
internal class PlaylistCrudHelper
{
    private readonly CurrentSongController _songController;
    private readonly Action<int> _onPlaylistLoaded; // Callback for IPC broadcast

    public PlaylistCrudHelper(
        CurrentSongController songController,
        Action<int> onPlaylistLoaded)
    {
        _songController = songController;
        _onPlaylistLoaded = onPlaylistLoaded;
    }

    /// <summary>
    /// Load last used playlist from database (or create default if none exists).
    /// </summary>
    public async Task<Playlist?> LoadLastPlaylistAsync()
    {
        try
        {
            var playlists = await ServiceContainer.PlaylistService.GetAllAsync();

            if (playlists.Count == 0)
            {
                var created = await ServiceContainer.PlaylistService.CreateAsync("Default");
                if (created != null) return created;

                // CreateAsync failed (e.g. duplicate key — "Default" already exists
                // but GetAllAsync returned empty due to a deserialization issue).
                // Retry loading to return whatever is in the database.
                playlists = await ServiceContainer.PlaylistService.GetAllAsync();
                if (playlists.Count > 0)
                    return await ServiceContainer.PlaylistService.GetByIdAsync(playlists[0].Id);

                return null;
            }

            // Load first playlist
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlists[0].Id);
            _songController.Clear();
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error loading last playlist");
            return null;
        }
    }

    /// <summary>
    /// Load playlist by ID from database.
    /// </summary>
    public async Task<Playlist?> LoadPlaylistByIdAsync(int playlistId)
    {
        try
        {
            DalamudApi.PluginLog.Warning($"LoadPlaylistByIdAsync({playlistId})");
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
            _songController.Clear(); // Reset song reference when loading new playlist
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error loading playlist {playlistId}");
            return null;
        }
    }

    /// <summary>
    /// Reload current playlist from database.
    /// </summary>
    public async Task<Playlist?> ReloadAsync(int currentPlaylistId)
    {
        return await LoadPlaylistByIdAsync(currentPlaylistId);
    }

    /// <summary>
    /// Switch to a different playlist.
    /// </summary>
    public async Task<Playlist?> SwitchToPlaylistAsync(int playlistId)
    {
        var playlist = await LoadPlaylistByIdAsync(playlistId);
        if (playlist != null)
        {
            _onPlaylistLoaded(playlist.Id);
        }
        return playlist;
    }

    /// <summary>
    /// Load a playlist as the current one (for UI button).
    /// </summary>
    public async Task<Playlist?> LoadPlaylistToCurrentAsync(int playlistId)
    {
        try
        {
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
            if (playlist != null)
            {
                _songController.Clear();
                _onPlaylistLoaded(playlistId);
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
    /// Get all playlists (for UI).
    /// </summary>
    public async Task<List<Playlist>> GetAllPlaylistsAsync()
    {
        try
        {
            return await ServiceContainer.PlaylistService.GetAllAsync();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "Error getting all playlists");
            return new List<Playlist>();
        }
    }

    /// <summary>
    /// Get songs for a specific playlist (for UI).
    /// </summary>
    public async Task<List<Song>> GetPlaylistSongsAsync(int playlistId)
    {
        try
        {
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
            if (playlist?.Songs == null)
                return new List<Song>();

            return playlist.Songs
                .Where(ps => ps.Song != null)
                .Select(ps => ps.Song)
                .ToList();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error getting playlist songs for {playlistId}");
            return new List<Song>();
        }
    }

    /// <summary>
    /// Create a new playlist.
    /// </summary>
    public async Task<Playlist?> CreatePlaylistAsync(string name)
    {
        try
        {
            return await ServiceContainer.PlaylistService.CreateAsync(name);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error creating playlist {name}");
            return null;
        }
    }

    /// <summary>
    /// Delete a playlist.
    /// </summary>
    public async Task<bool> DeletePlaylistAsync(int playlistId)
    {
        try
        {
            await ServiceContainer.PlaylistService.DeleteAsync(playlistId);
            DalamudApi.PluginLog.Information($"Deleted playlist {playlistId}");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error deleting playlist {playlistId}");
            return false;
        }
    }

    /// <summary>
    /// Get playlist by ID.
    /// </summary>
    public async Task<Playlist?> GetPlaylistByIdAsync(int playlistId)
    {
        try
        {
            return await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error getting playlist {playlistId}");
            return null;
        }
    }

    /// <summary>
    /// Clear all songs from a playlist.
    /// </summary>
    public async Task<bool> ClearPlaylistAsync(int playlistId, int currentPlaylistId)
    {
        try
        {
            await ServiceContainer.PlaylistService.ClearAsync(playlistId);

            // If this is the current playlist, reload it
            if (currentPlaylistId == playlistId)
            {
                await LoadPlaylistByIdAsync(playlistId);
                _onPlaylistLoaded(playlistId);
            }

            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"Error clearing playlist {playlistId}");
            return false;
        }
    }
}
