using System;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages song metadata operations: rating, tagging, playlist-song state updates.
/// Encapsulates all metadata mutation logic separate from state changes.
/// </summary>
internal class SongMetadataManager
{
    public SongMetadataManager() {}

    /// <summary>
    /// Update song metadata (name, artist, rating, etc).
    /// </summary>
    public async Task UpdateSongAsync(Song song)
    {
        if (song == null) return;

        try
        {
            await ServiceContainer.SongService.UpdateAsync(song);
            DalamudApi.PluginLog.Debug($"[SongMetadataManager] Updated song {song.Id}");
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
            await ServiceContainer.SongService.UpdateAsync(playlistSong.Song);

            // 2. Update playlist-song state via service
            var playlist = await ServiceContainer.PlaylistService.GetByIdAsync(playlistId);
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
                await ServiceContainer.PlaylistService.UpdateAsync(playlist);

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
            await ServiceContainer.SongService.AddTagAsync(songId, tag);
            DalamudApi.PluginLog.Debug($"[SongMetadataManager] Added tag '{tag}' to song {songId}");
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
            await ServiceContainer.SongService.RemoveTagAsync(songId, tag);
            DalamudApi.PluginLog.Debug($"[SongMetadataManager] Removed tag '{tag}' from song {songId}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongMetadataManager] Error removing tag from song {songId}");
        }
    }

    /// <summary>
    /// Remove tag from a song by tag ID.
    /// </summary>
    public async Task RemoveTagFromSongByIdAsync(int songId, int tagId)
    {
        try
        {
            var tag = await ServiceContainer.TagService.GetByIdAsync(tagId);
            if (tag == null)
            {
                DalamudApi.PluginLog.Warning($"[SongMetadataManager] Tag {tagId} not found");
                return;
            }

            await ServiceContainer.SongService.RemoveTagAsync(songId, tag.Name);
            DalamudApi.PluginLog.Debug($"[SongMetadataManager] Removed tag '{tag.Name}' (id={tagId}) from song {songId}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[SongMetadataManager] Error removing tag from song {songId}");
        }
    }
}
