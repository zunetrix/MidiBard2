using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing playlists.
/// </summary>
public interface IPlaylistService
{
    /// <summary>
    /// Get a playlist by ID.
    /// </summary>
    Task<Playlist?> GetByIdAsync(int id);

    /// <summary>
    /// Get all playlists.
    /// </summary>
    Task<List<Playlist>> GetAllAsync();

    /// <summary>
    /// Create a new playlist with the given name.
    /// </summary>
    Task<Playlist?> CreateAsync(string name);

    /// <summary>
    /// Update a playlist.
    /// </summary>
    /// <returns>True if update was successful, false otherwise.</returns>
    Task<bool> UpdateAsync(Playlist playlist);

    /// <summary>
    /// Delete a playlist.
    /// </summary>
    /// <returns>True if delete was successful, false otherwise.</returns>
    Task<bool> DeleteAsync(int id);

    /// <summary>
    /// Clear all songs from a playlist.
    /// </summary>
    /// <returns>True if clear was successful, false otherwise.</returns>
    Task<bool> ClearAsync(int id);
}
