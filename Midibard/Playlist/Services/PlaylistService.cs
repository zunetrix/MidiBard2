using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing playlists.
/// </summary>
public class PlaylistService : IPlaylistService
{
    private readonly IPlaylistRepository _repository;

    public PlaylistService(IPlaylistRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Playlist?> GetByIdAsync(int id)
    {
        try
        {
            var playlist = await _repository.GetByIdAsync(id);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistService] Playlist {id} not found");
                return null;
            }
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistService] Error getting playlist {id}");
            return null;
        }
    }

    public async Task<List<Playlist>> GetAllAsync()
    {
        try
        {
            return await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistService] Error getting all playlists");
            return new List<Playlist>();
        }
    }

    public async Task<Playlist?> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            DalamudApi.PluginLog.Warning("[PlaylistService] Cannot create playlist with empty name");
            return null;
        }

        var playlist = new Playlist
        {
            Name = name.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Songs = new List<PlaylistSong>()
        };

        try
        {
            await _repository.CreateAsync(playlist);
            DalamudApi.PluginLog.Information($"[PlaylistService] Created playlist: {name}");
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistService] Error creating playlist {name}");
            return null;
        }
    }

    public async Task<bool> UpdateAsync(Playlist playlist)
    {
        if (playlist == null)
        {
            DalamudApi.PluginLog.Warning("[PlaylistService] Cannot update null playlist");
            return false;
        }

        try
        {
            await _repository.UpdateAsync(playlist);
            DalamudApi.PluginLog.Debug($"[PlaylistService] Updated playlist {playlist.Id}");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistService] Error updating playlist {playlist.Id}");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            await _repository.DeleteAsync(id);
            DalamudApi.PluginLog.Information($"[PlaylistService] Deleted playlist {id}");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistService] Error deleting playlist {id}");
            return false;
        }
    }

    public async Task<bool> ClearAsync(int id)
    {
        try
        {
            var playlist = await GetByIdAsync(id);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistService] Cannot clear - playlist {id} not found");
                return false;
            }

            playlist.Songs.Clear();
            playlist.UpdatedAt = DateTime.UtcNow;
            return await UpdateAsync(playlist);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistService] Error clearing playlist {id}");
            return false;
        }
    }
}
