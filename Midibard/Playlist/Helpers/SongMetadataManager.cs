using System;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Playlist.Services;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages song metadata operations: rating, tagging, playlist-song state updates.
/// Encapsulates all metadata mutation logic separate from state changes.
/// </summary>
internal class SongMetadataManager
{
    private readonly ISongService? _songService;
    private readonly IPlaylistService? _playlistService;

    public SongMetadataManager(
        ISongService? songService,
        IPlaylistService? playlistService)
    {
        _songService = songService;
        _playlistService = playlistService;
    }

    /// <summary>
    /// Update song metadata (name, artist, rating, etc).
    /// </summary>
    public async Task UpdateSongAsync(Song song)
    {
        if (song == null) return;

        try
        {
            if (_songService != null)
            {
                await _songService.UpdateAsync(song);
                DalamudApi.PluginLog.Debug($"[SongMetadataManager] Updated song {song.Id}");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongMetadataManager] Error updating song {song.Id}");
        }
    }

    /// <summary>
    /// Update playlist-song metadata (IsPlayed, AddedAt).
    /// </summary>
    public async Task UpdatePlaylistSongAsync(PlaylistSong playlistSong, int playlistId)
    {
        if (playlistSong?.Song == null) return;

        try
        {
            DalamudApi.PluginLog.Warning($"[SongMetadataManager] Updating playlist song: playlistId={playlistId}, songId={playlistSong.Song.Id}");

            // 1. Update song metadata via service
            if (_songService != null)
            {
                await _songService.UpdateAsync(playlistSong.Song);
            }

            // 2. Update playlist-song state via service
            if (_playlistService == null)
            {
                DalamudApi.PluginLog.Warning("[SongMetadataManager] PlaylistService not initialized");
                return;
            }

            var playlist = await _playlistService.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[SongMetadataManager] Playlist {playlistId} not found");
                return;
            }

            var existingPs = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == playlistSong.Song.Id);
            if (existingPs != null)
            {
                DalamudApi.PluginLog.Warning($"[SongMetadataManager] Updating IsPlayed from {existingPs.IsPlayed} to {playlistSong.IsPlayed}");

                // Update the PlaylistSong state
                existingPs.IsPlayed = playlistSong.IsPlayed;
                if (playlistSong.AddedAt != default)
                    existingPs.AddedAt = playlistSong.AddedAt;

                // Persist playlist via service
                await _playlistService.UpdateAsync(playlist);

                DalamudApi.PluginLog.Warning($"[SongMetadataManager] Playlist updated successfully");
            }
            else
            {
                DalamudApi.PluginLog.Warning($"[SongMetadataManager] PlaylistSong NOT FOUND for songId: {playlistSong.Song.Id}");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongMetadataManager] Error updating playlist song");
        }
    }

    /// <summary>
    /// Add tag to a song.
    /// </summary>
    public async Task AddTagToSongAsync(int songId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        try
        {
            if (_songService != null)
            {
                await _songService.AddTagAsync(songId, tag);
                DalamudApi.PluginLog.Debug($"[SongMetadataManager] Added tag '{tag}' to song {songId}");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongMetadataManager] Error adding tag to song {songId}");
        }
    }

    /// <summary>
    /// Remove tag from a song.
    /// </summary>
    public async Task RemoveTagFromSongAsync(int songId, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        try
        {
            if (_songService != null)
            {
                await _songService.RemoveTagAsync(songId, tag);
                DalamudApi.PluginLog.Debug($"[SongMetadataManager] Removed tag '{tag}' from song {songId}");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongMetadataManager] Error removing tag from song {songId}");
        }
    }

    /// <summary>
    /// Remove tag from a song by tag ID - more efficient than using tag name.
    /// </summary>
    public async Task RemoveTagFromSongByIdAsync(int songId, int tagId)
    {
        // Note: ISongService currently uses tag name, not ID
        // Consider updating ISongService interface to support RemoveTagByIdAsync
        try
        {
            if (_songService != null)
            {
                // Placeholder: would need ISongService enhancement
                DalamudApi.PluginLog.Warning("[SongMetadataManager] RemoveTagFromSongByIdAsync not fully implemented - requires ISongService update");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongMetadataManager] Error removing tag from song {songId}");
        }
    }
}
