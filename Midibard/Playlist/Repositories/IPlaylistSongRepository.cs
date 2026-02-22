using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

/// <summary>
/// Repository interface for PlaylistSong (join table) operations.
/// Handles the relationship between Playlists and Songs.
/// </summary>
public interface IPlaylistSongRepository
{
    /// <summary>
    /// Get a specific PlaylistSong entry by ID
    /// </summary>
    Task<PlaylistSong?> GetByIdAsync(int id);

    /// <summary>
    /// Get all PlaylistSong entries for a specific playlist (non-deleted)
    /// </summary>
    Task<List<PlaylistSong>> GetByPlaylistIdAsync(int playlistId);

    /// <summary>
    /// Get a specific PlaylistSong entry by playlist and song IDs
    /// </summary>
    Task<PlaylistSong?> GetByPlaylistAndSongAsync(int playlistId, int songId);

    /// <summary>
    /// Create a new PlaylistSong entry
    /// </summary>
    Task<PlaylistSong> CreateAsync(PlaylistSong playlistSong);

    /// <summary>
    /// Update an existing PlaylistSong entry
    /// </summary>
    Task UpdateAsync(PlaylistSong playlistSong);

    /// <summary>
    /// Soft delete a PlaylistSong entry
    /// </summary>
    Task DeleteAsync(int id);

    /// <summary>
    /// Mark a song as played
    /// </summary>
    Task MarkAsPlayedAsync(int id);

    /// <summary>
    /// Reset played status (set IsPlayed to false)
    Task ResetPlayedStatusAsync(int id);

    /// <summary>
    /// Reorder songs within a playlist
    /// </summary>
    Task ReorderAsync(int playlistId, int songId, int newOrder);

    /// <summary>
    /// Get count of non-deleted PlaylistSong entries for a playlist
    /// </summary>
    Task<int> GetPlaylistSongCountAsync(int playlistId);
}
