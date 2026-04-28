using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist.Services;

/// <summary>
/// Service for managing songs globally.
/// </summary>
public interface ISongService
{
    /// <summary>
    /// Get a song by ID.
    /// </summary>
    Task<Song?> GetByIdAsync(int id);

    /// <summary>
    /// Get or create a song from a file path.
    /// </summary>
    Task<Song?> GetOrCreateFromFileAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration);

    /// <summary>
    /// Update a song.
    /// </summary>
    /// <returns>True if update was successful, false otherwise.</returns>
    Task<bool> UpdateAsync(Song song);

    /// <summary>
    /// Set the rating for a song.
    /// </summary>
    /// <returns>True if set was successful, false otherwise.</returns>
    Task<bool> SetRatingAsync(int songId, int rating);

    /// <summary>
    /// Add a tag to a song.
    /// </summary>
    /// <returns>True if add was successful, false otherwise.</returns>
    Task<bool> AddTagAsync(int songId, string tagName);

    /// <summary>
    /// Remove a tag from a song by name.
    /// </summary>
    /// <returns>True if remove was successful, false otherwise.</returns>
    Task<bool> RemoveTagAsync(int songId, string tagName);

    /// <summary>
    /// Validate the file path of a song.
    /// </summary>
    /// <returns>True if validation was successful, false otherwise.</returns>
    Task<bool> ValidateFileAsync(int songId);

    /// <summary>
    /// Get multiple songs by IDs.
    /// </summary>
    Task<List<Song>> GetByIdsAsync(IEnumerable<int> songIds);

    /// <summary>
    /// Get or calculate the duration for a song.
    /// </summary>
    Task<TimeSpan> GetOrCalculateDurationAsync(int songId);

    /// <summary>
    /// Delete a song with cascading cleanup of playlists.
    /// </summary>
    /// <returns>True if delete was successful, false otherwise.</returns>
    Task<bool> DeleteAsync(int songId);

    /// <summary>
    /// Replace a file path prefix on all matching songs.
    /// </summary>
    /// <returns>Number of songs updated.</returns>
    Task<int> BulkReplaceFilePathPrefixAsync(string oldPrefix, string newPrefix);

    /// <summary>
    /// Update multiple songs in a single DB operation.
    /// </summary>
    /// <returns>Number of documents updated.</returns>
    Task<int> BulkUpdateAsync(IEnumerable<Song> songs);
}
