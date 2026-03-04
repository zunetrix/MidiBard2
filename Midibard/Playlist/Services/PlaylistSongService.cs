using System;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing songs within playlists.
/// </summary>
public class PlaylistSongService : IPlaylistSongService
{
    private readonly IPlaylistRepository _playlistRepository;
    private readonly ISongRepository _songRepository;

    public PlaylistSongService(
        IPlaylistRepository playlistRepository,
        ISongRepository songRepository)
    {
        if (playlistRepository == null)
            throw new ArgumentNullException(nameof(playlistRepository));
        if (songRepository == null)
            throw new ArgumentNullException(nameof(songRepository));

        _playlistRepository = playlistRepository;
        _songRepository = songRepository;
    }

    public async Task<bool> AddSongAsync(int playlistId, Song song)
    {
        if (song == null)
        {
            DalamudApi.PluginLog.Warning("[PlaylistSongService] Cannot add null song");
            return false;
        }

        try
        {
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Playlist {playlistId} not found");
                return false;
            }

            var playlistSong = new PlaylistSong
            {
                Song = song,
                IsPlayed = false,
                AddedAt = DateTime.UtcNow
            };

            playlist.AddSong(playlistSong);
            await _playlistRepository.UpdateAsync(playlist);

            DalamudApi.PluginLog.Debug($"[PlaylistSongService] Added song {song.Id} to playlist {playlistId}");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongService] Error adding song {song.Id} to playlist {playlistId}");
            return false;
        }
    }

    public async Task<bool> RemoveSongAsync(int playlistId, int songId)
    {
        try
        {
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Playlist {playlistId} not found");
                return false;
            }

            var songIndex = playlist.Songs.FindIndex(ps => ps.Song?.Id == songId);
            if (songIndex < 0)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Song {songId} not found in playlist {playlistId}");
                return false;
            }

            if (!playlist.RemoveSongAt(songIndex))
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Failed to remove song {songId} from playlist {playlistId}");
                return false;
            }

            await _playlistRepository.UpdateAsync(playlist);
            DalamudApi.PluginLog.Debug($"[PlaylistSongService] Removed song {songId} from playlist {playlistId}");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongService] Error removing song from playlist {playlistId}");
            return false;
        }
    }

    public async Task<bool> ReorderSongAsync(int playlistId, int fromIndex, int toIndex)
    {
        try
        {
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Playlist {playlistId} not found");
                return false;
            }

            if (!playlist.MoveSongToIndex(fromIndex, toIndex))
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Invalid indices {fromIndex} -> {toIndex} for playlist {playlistId}");
                return false;
            }

            await _playlistRepository.UpdateAsync(playlist);
            DalamudApi.PluginLog.Debug($"[PlaylistSongService] Reordered song from {fromIndex} to {toIndex} in playlist {playlistId}");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongService] Error reordering song in playlist {playlistId}");
            return false;
        }
    }

    public async Task<bool> MarkSongAsPlayedAsync(int playlistId, int songIndex)
    {
        try
        {
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Playlist {playlistId} not found");
                return false;
            }

            if (!playlist.MarkSongAsPlayed(songIndex))
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Invalid song index {songIndex} for playlist {playlistId}");
                return false;
            }

            await _playlistRepository.UpdateAsync(playlist);

            // Record the play on the Song (increments PlayCount and sets LastPlayedAt)
            var songId = playlist.Songs[songIndex].Song?.Id;
            if (songId.HasValue)
                await _songRepository.IncrementPlayCountAsync(songId.Value);

            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongService] Error marking song as played in playlist {playlistId}");
            return false;
        }
    }

    public async Task<bool> ResetAllSongsPlayedStatusAsync(int playlistId)
    {
        try
        {
            var playlist = await _playlistRepository.GetByIdAsync(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[PlaylistSongService] Playlist {playlistId} not found");
                return false;
            }

            playlist.ResetAllSongsPlayedStatus();
            await _playlistRepository.UpdateAsync(playlist);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[PlaylistSongService] Error resetting played status for playlist {playlistId}");
            return false;
        }
    }
}
