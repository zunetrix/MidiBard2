using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing songs within playlists.
/// </summary>
public interface IPlaylistSongService
{
    /// <summary>
    /// Add a song to a playlist.
    /// </summary>
    /// <returns>True if add was successful, false otherwise.</returns>
    Task<bool> AddSongAsync(int playlistId, Song song);

    /// <summary>
    /// Remove a song from a playlist by its song ID.
    /// </summary>
    /// <returns>True if remove was successful, false otherwise.</returns>
    Task<bool> RemoveSongAsync(int playlistId, int songId);

    /// <summary>
    /// Reorder a song within a playlist.
    /// </summary>
    /// <returns>True if reorder was successful, false otherwise.</returns>
    Task<bool> ReorderSongAsync(int playlistId, int fromIndex, int toIndex);

    /// <summary>
    /// Set played status for a song in a playlist.
    /// </summary>
    /// <returns>True if update was successful, false otherwise.</returns>
    Task<bool> SetSongPlayedStatusAsync(int playlistId, int songIndex, bool isPlayed, bool incrementPlayCount = false);

    /// <summary>
    /// Reset the played status for all songs in a playlist.
    /// </summary>
    /// <returns>True if reset was successful, false otherwise.</returns>
    Task<bool> ResetAllSongsPlayedStatusAsync(int playlistId);

    /// <summary>
    /// Add multiple songs to a playlist in a single batch operation.
    /// </summary>
    /// <returns>True if add was successful, false otherwise.</returns>
    Task<bool> BulkAddSongsAsync(int playlistId, IEnumerable<int> songIds);
}
