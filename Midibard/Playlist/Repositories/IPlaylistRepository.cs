using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

public interface IPlaylistRepository
{
    // Playlist operations
    /// <summary>
    /// Get a playlist by ID with full song details loaded.
    /// </summary>
    Task<Playlist?> GetByIdAsync(int id);

    /// <summary>
    /// Get a playlist by ID without loading song details (lightweight).
    /// </summary>
    Task<Playlist?> GetByIdLightAsync(int id);

    /// <summary>
    /// Get all playlists without loading song details (lightweight).
    /// Use for lists, dropdowns, counts.
    /// </summary>
    Task<List<Playlist>> GetAllAsync();

    /// <summary>
    /// Get all playlists with full song details loaded.
    /// Use for detail display screens.
    /// </summary>
    Task<List<Playlist>> GetAllWithSongsAsync();

    Task<Playlist> CreateAsync(Playlist playlist);
    Task UpdateAsync(Playlist playlist);
    Task DeleteAsync(int id);

    // PlaylistSong operations (join table)
    Task AddSongToPlaylistAsync(int playlistId, int songId, int order);
    Task RemoveSongFromPlaylistAsync(int playlistId, int songId);
    Task ReorderSongAsync(int playlistId, int songId, int newOrder);
    Task SetSongPlayedStatusAsync(int playlistId, int songId, bool isPlayed);
    // Reset played status for all songs in a playlist
    Task ResetAllSongsPlayedStatusAsync(int playlistId);
    // Clear all songs from a playlist in a single batch operation
    Task RemoveAllSongsAsync(int playlistId);
    // Clear all songs from all playlists
    Task ClearAllPlaylistsAsync();

    // Batch reorder all songs in a playlist - more efficient than calling ReorderSongAsync for each
    Task ReorderAllSongsAsync(int playlistId, List<int> songIdsInOrder);

    // Add multiple songs to a playlist in a single update
    Task BulkAddSongsToPlaylistAsync(int playlistId, IEnumerable<int> songIds);
}
