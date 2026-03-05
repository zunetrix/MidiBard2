using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

public interface ISongRepository
{
    /// <summary>
    /// Get a song by ID with full details including tags.
    /// </summary>
    Task<Song?> GetSongByIdAsync(int id);

    /// <summary>
    /// Get a song by ID without tag details (lightweight, for quick lookups).
    /// </summary>
    Task<Song?> GetSongByIdLightAsync(int id);

    /// <summary>
    /// Get a song by ID (wrapper for GetSongByIdAsync for compatibility).
    /// </summary>
    Task<Song?> GetByIdAsync(int id);

    /// <summary>
    /// Get a song by file path without tag details (lightweight).
    /// </summary>
    Task<Song?> GetByFilePathAsync(string filePath);

    /// <summary>
    /// Get a song by file path with full details including tags.
    /// </summary>
    Task<Song?> GetByFilePathWithTagsAsync(string filePath);

    Task<Song> CreateOrGetSongAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration, bool isValid = true, DateTime fileLastModifiedAt = default);

    /// <summary>
    /// Insert multiple songs at once. Returns the inserted songs with IDs assigned.
    /// </summary>
    Task<List<Song>> BulkInsertSongsAsync(IEnumerable<Song> songs);

    /// <summary>
    /// Get all songs without tag details (lightweight, useful for lists and counts).
    /// </summary>
    Task<List<Song>> GetAllSongsAsync();

    /// <summary>
    /// Get all songs with complete tag details (full load, useful for detail display).
    /// </summary>
    Task<List<Song>> GetAllSongsWithTagsAsync();

    Task UpdateAsync(Song song);
    Task DeleteAsync(int id);
    Task DeleteAllAsync();

    // Song-specific operations
    Task IncrementPlayCountAsync(int songId);
    Task SetRatingAsync(int songId, int rate);
    Task AddTagAsync(int songId, string tag);
    Task RemoveTagAsync(int songId, string tag);
    Task RemoveTagByIdAsync(int songId, int tagId);

    // Batch operations (optimized for multiple items)
    /// <summary>
    /// Get specific songs by their IDs without tag details (lightweight).
    /// </summary>
    Task<List<Song>> GetSongsByIdsAsync(IEnumerable<int> songIds);

    /// <summary>
    /// Get specific songs by their IDs with full tag details.
    /// </summary>
    Task<List<Song>> GetSongsByIdsWithTagsAsync(IEnumerable<int> songIds);

    Task AddTagsAsync(int songId, IEnumerable<string> tagNames);
    Task RemoveTagsAsync(int songId, IEnumerable<string> tagNames);

    /// <summary>
    /// Replace a file path prefix on all matching songs.
    /// Returns the songs with updated paths (not yet persisted).
    /// </summary>
    Task<List<Song>> BulkReplaceFilePathPrefixAsync(string oldPrefix, string newPrefix);

    /// <summary>
    /// Update multiple songs in a single DB operation.
    /// Returns the number of documents updated.
    /// </summary>
    Task<int> BulkUpdateAsync(IEnumerable<Song> songs);
}
