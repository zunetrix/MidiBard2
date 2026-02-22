using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MidiBard.Playlist;

public interface ISongRepository
{
    Task<Song?> GetSongByIdAsync(int id);
    Task<Song?> GetByIdAsync(int id);
    Task<Song?> GetByFilePathAsync(string filePath);
    Task<Song> CreateOrGetSongAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration);
    Task<List<Song>> GetAllSongsAsync();
    Task UpdateAsync(Song song);
    Task DeleteAsync(int id);

    // Song-specific operations
    Task IncrementPlayCountAsync(int songId);
    Task SetRatingAsync(int songId, int rate);
    Task AddTagAsync(int songId, string tag);
    Task RemoveTagAsync(int songId, string tag);

    // Batch operations (optimized for multiple items)
    Task<List<Song>> GetSongsByIdsAsync(IEnumerable<int> songIds);
    Task AddTagsAsync(int songId, IEnumerable<string> tagNames);
    Task RemoveTagsAsync(int songId, IEnumerable<string> tagNames);
}
