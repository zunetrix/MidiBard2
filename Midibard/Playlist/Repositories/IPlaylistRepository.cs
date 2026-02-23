using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

public interface IPlaylistRepository
{
    // Playlist operations
    Task<Playlist?> GetByIdAsync(int id);
    Task<List<Playlist>> GetAllAsync();
    Task<Playlist> CreateAsync(Playlist playlist);
    Task UpdateAsync(Playlist playlist);
    Task DeleteAsync(int id);

    // PlaylistSong operations (join table)
    Task AddSongToPlaylistAsync(int playlistId, int songId, int order);
    Task RemoveSongFromPlaylistAsync(int playlistId, int songId);
    Task ReorderSongAsync(int playlistId, int songId, int newOrder);
    Task MarkSongAsPlayedAsync(int playlistId, int songId);
    // Reset played status for all songs in a playlist
    Task ResetAllSongsPlayedStatusAsync(int playlistId);
    // Clear all songs from a playlist in a single batch operation
    Task RemoveAllSongsAsync(int playlistId);
    // Clear all songs from all playlists
    Task ClearAllPlaylistsAsync();
}
