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
    /// Remove a song from a playlist at the given index.
    /// </summary>
    /// <returns>True if remove was successful, false otherwise.</returns>
    Task<bool> RemoveSongAsync(int playlistId, int songIndex);

    /// <summary>
    /// Reorder a song within a playlist.
    /// </summary>
    /// <returns>True if reorder was successful, false otherwise.</returns>
    Task<bool> ReorderSongAsync(int playlistId, int fromIndex, int toIndex);

    /// <summary>
    /// Mark a song as played in a playlist.
    /// </summary>
    /// <returns>True if mark was successful, false otherwise.</returns>
    Task<bool> MarkSongAsPlayedAsync(int playlistId, int songIndex);

    /// <summary>
    /// Reset the played status for all songs in a playlist.
    /// </summary>
    /// <returns>True if reset was successful, false otherwise.</returns>
    Task<bool> ResetAllSongsPlayedStatusAsync(int playlistId);
}
