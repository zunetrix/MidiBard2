using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbSongRepository : ISongRepository
{
    private readonly LiteDatabase _database;

    public LiteDbSongRepository(LiteDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Get a song by ID WITH complete tag details (full load).
    /// Use this when you need the song with all tag information.
    /// Use GetSongByIdLightAsync() for lightweight lookups (metadata only).
    /// </summary>
    public Task<Song?> GetSongByIdAsync(int id)
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Full load: Include all tags with the song
            var song = collection
                .Include(x => x.Tags)
                .FindById(id);

            if (song != null)
                DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded song {SongId} (with all tags)", id);

            return Task.FromResult<Song?>(song);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting song {SongId}", id);
            throw;
        }
    }

    /// <summary>
    /// Get a song by ID WITHOUT tag details (lightweight).
    /// Use this for quick lookups when you don't need tag information.
    /// Use GetSongByIdAsync() for complete song data with tags.
    /// </summary>
    public Task<Song?> GetSongByIdLightAsync(int id)
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Lightweight: Don't load tags
            var song = collection.FindById(id);

            if (song != null)
                DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded song {SongId} (lightweight - without tags)", id);

            return Task.FromResult<Song?>(song);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting song {SongId}", id);
            throw;
        }
    }

    public Task<Song?> GetByIdAsync(int id)
    {
        return GetSongByIdAsync(id);
    }

    /// <summary>
    /// Get a song by file path WITHOUT tag details (lightweight).
    /// Use this for quick lookups when you don't need tag information.
    /// Use GetByFilePathWithTagsAsync() for complete song data with tags.
    /// </summary>
    public Task<Song?> GetByFilePathAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult<Song?>(null);

        try
        {
            var collection = _database.GetCollection<Song>("songs");
            var song = collection.FindOne(x => x.FilePath == filePath);

            if (song != null)
                DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded song by file path (lightweight - without tags): {FilePath}", filePath);

            return Task.FromResult<Song?>(song);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting song by file path {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Get a song by file path WITH complete tag details (full load).
    /// Use this when you need the song with all tag information.
    /// Use GetByFilePathAsync() for lightweight lookups (metadata only).
    /// </summary>
    public Task<Song?> GetByFilePathWithTagsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult<Song?>(null);

        try
        {
            var collection = _database.GetCollection<Song>("songs");
            var song = collection
                .Include(x => x.Tags)
                .FindOne(x => x.FilePath == filePath);

            if (song != null)
                DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded song by file path (with all tags): {FilePath}", filePath);

            return Task.FromResult<Song?>(song);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting song by file path with tags {FilePath}", filePath);
            throw;
        }
    }

    public Task<Song> CreateOrGetSongAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration, bool hasValidFilePath = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath cannot be empty", nameof(filePath));

        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Try to find existing song
            var existingSong = collection.FindOne(x => x.FilePath == filePath);
            if (existingSong != null)
            {
                // Update fields if different
                bool updated = false;
                if (existingSong.Duration != duration)
                {
                    existingSong.Duration = duration;
                    updated = true;
                }
                if (existingSong.Name != name)
                {
                    existingSong.Name = name;
                    updated = true;
                }
                if (existingSong.Artist != artist)
                {
                    existingSong.Artist = artist;
                    updated = true;
                }
                if (existingSong.ReleaseYear != releaseYear)
                {
                    existingSong.ReleaseYear = releaseYear;
                    updated = true;
                }
                if (existingSong.IsValid != hasValidFilePath)
                {
                    existingSong.IsValid = hasValidFilePath;
                    updated = true;
                }
                if (updated)
                {
                    existingSong.UpdatedAt = DateTime.UtcNow;
                    collection.Update(existingSong);
                    DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Updated existing song {SongPath}", filePath);
                }
                return Task.FromResult(existingSong);
            }

            // Create new song
            var newSong = new Song
            {
                FilePath = filePath,
                Name = name,
                Artist = artist,
                ReleaseYear = releaseYear,
                Duration = duration,
                IsValid = hasValidFilePath,
                Tags = new List<Tag>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            collection.Insert(newSong);
            DalamudApi.PluginLog.Information("[LiteDbSongRepository] Created new song {SongPath}", filePath);
            return Task.FromResult(newSong);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error creating/getting song {SongPath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Get all songs WITHOUT tag details (lightweight).
    /// Use this for list operations, counts, or when you don't need tag information.
    /// Use GetAllSongsWithTagsAsync() for complete song data with tags.
    /// </summary>
    public Task<List<Song>> GetAllSongsAsync()
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Lightweight: Don't load tags
            // Useful for song lists, dropdowns, counts
            var songs = collection.FindAll().ToList();

            DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded {SongCount} songs (lightweight - without tags)", songs.Count);
            return Task.FromResult(songs);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting all songs");
            throw;
        }
    }

    /// <summary>
    /// Get all songs WITH complete tag details (full load).
    /// Use this when displaying songs on screen with all their tag information.
    /// Use GetAllSongsAsync() for lightweight list operations (counts, dropdowns, etc).
    /// </summary>
    public Task<List<Song>> GetAllSongsWithTagsAsync()
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Full load: Include all tags with each song
            // Useful for detail display screens
            var songs = collection
                .Include(x => x.Tags)
                .FindAll()
                .ToList();

            DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded {SongCount} songs (with all tags)", songs.Count);
            return Task.FromResult(songs);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting all songs with tags");
            throw;
        }
    }

    /// <summary>
    /// Get specific songs by their IDs WITHOUT tag details (lightweight).
    /// Use this for batch operations that don't need tag information.
    /// Use GetSongsByIdsWithTagsAsync() for complete song data with tags.
    /// </summary>
    public Task<List<Song>> GetSongsByIdsAsync(IEnumerable<int> songIds)
    {
        if (songIds == null)
            throw new ArgumentNullException(nameof(songIds));

        var ids = songIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Task.FromResult(new List<Song>());

        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Lightweight: Single batch query without tags
            var songs = collection
                .Find(x => ids.Contains(x.Id))
                .ToList();

            DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded {SongCount} songs by IDs (lightweight - without tags)", songs.Count);
            return Task.FromResult(songs);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting songs by ids");
            throw;
        }
    }

    /// <summary>
    /// Get specific songs by their IDs WITH complete tag details (full load).
    /// Use this when you need songs with full tag information for detail display.
    /// Use GetSongsByIdsAsync() for lightweight batch operations (counts, basic info).
    /// </summary>
    public Task<List<Song>> GetSongsByIdsWithTagsAsync(IEnumerable<int> songIds)
    {
        if (songIds == null)
            throw new ArgumentNullException(nameof(songIds));

        var ids = songIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Task.FromResult(new List<Song>());

        try
        {
            var collection = _database.GetCollection<Song>("songs");

            // Full load: Single batch query with tags
            var songs = collection
                .Include(x => x.Tags)
                .Find(x => ids.Contains(x.Id))
                .ToList();

            DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Loaded {SongCount} songs by IDs (with all tags)", songs.Count);
            return Task.FromResult(songs);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error getting songs by ids with tags");
            throw;
        }
    }

    public Task UpdateAsync(Song song)
    {
        if (song == null)
            throw new ArgumentNullException(nameof(song));
        if (string.IsNullOrWhiteSpace(song.Name))
            throw new ArgumentException("Song name cannot be empty", nameof(song));

        try
        {
            var collection = _database.GetCollection<Song>("songs");
            song.UpdatedAt = DateTime.UtcNow;
            collection.Update(song);
            DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Updated song {SongId}: {SongName}", song.Id, song.Name);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error updating song {SongId}", song.Id);
            throw;
        }
    }

    public Task DeleteAsync(int id)
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");
            collection.Delete(id);
            DalamudApi.PluginLog.Information("[LiteDbSongRepository] Deleted song {SongId}", id);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error deleting song {SongId}", id);
            throw;
        }
    }

    public Task DeleteAllAsync()
    {
        var collection = _database.GetCollection<Song>("songs");
        collection.DeleteAll();
        return Task.CompletedTask;
    }

    // ==================== Song-specific Operations ====================

    public Task IncrementPlayCountAsync(int songId)
    {
        try
        {
            var collection = _database.GetCollection<Song>("songs");
            var song = collection.FindById(songId);

            if (song != null)
            {
                song.PlayCount++;
                song.LastPlayedAt = DateTime.UtcNow;
                song.UpdatedAt = DateTime.UtcNow;
                collection.Update(song);
                DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Incremented play count for song {SongId}: {PlayCount}", songId, song.PlayCount);
            }
            else
            {
                DalamudApi.PluginLog.Warning("[LiteDbSongRepository] Song {SongId} not found for play count increment", songId);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error incrementing play count for song {SongId}", songId);
            throw;
        }
    }

    public Task SetRatingAsync(int songId, int rate)
    {
        if (rate < 0 || rate > 10)
            throw new ArgumentException("Rating must be between 0 and 10", nameof(rate));

        try
        {
            var collection = _database.GetCollection<Song>("songs");
            var song = collection.FindById(songId);

            if (song != null)
            {
                song.Rating = rate;
                song.UpdatedAt = DateTime.UtcNow;
                collection.Update(song);
                DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Set rating for song {SongId}: {Rating}", songId, rate);
            }
            else
            {
                DalamudApi.PluginLog.Warning("[LiteDbSongRepository] Song {SongId} not found for rating update", songId);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error setting rating for song {SongId}", songId);
            throw;
        }
    }

    public async Task AddTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

        try
        {
            var songCollection = _database.GetCollection<Song>("songs");
            var song = songCollection
                .Include(x => x.Tags)
                .FindById(songId);

            if (song != null)
            {
                // Get or create tag (properly awaited)
                var tagRepo = new LiteDbTagRepository(_database);
                var tag = await tagRepo.CreateOrGetAsync(tagName);

                // Add tag reference to song if not already present
                if (!song.Tags.Any(t => t.Id == tag.Id))
                {
                    song.Tags.Add(tag);
                    song.UpdatedAt = DateTime.UtcNow;
                    songCollection.Update(song);
                }
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error adding tag {TagName} to song {SongId}", tagName, songId);
            throw;
        }
    }

    public Task RemoveTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

        try
        {
            var songCollection = _database.GetCollection<Song>("songs");
            var song = songCollection
                .Include(x => x.Tags)
                .FindById(songId);

            if (song != null)
            {
                var tagToRemove = song.Tags.FirstOrDefault(t =>
                    t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));

                if (tagToRemove != null)
                {
                    song.Tags.Remove(tagToRemove);
                    song.UpdatedAt = DateTime.UtcNow;
                    songCollection.Update(song);
                    DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Removed tag {TagName} from song {SongId}", tagName, songId);
                }
                else
                {
                    DalamudApi.PluginLog.Debug("[LiteDbSongRepository] Tag {TagName} not found on song {SongId}", tagName, songId);
                }
            }
            else
            {
                DalamudApi.PluginLog.Warning("[LiteDbSongRepository] Song {SongId} not found for tag removal", songId);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error removing tag {TagName} from song {SongId}", tagName, songId);
            throw;
        }
    }

    /// <summary>
    /// Remove tag from song by tag ID - more efficient than by name lookup
    /// </summary>
    public Task RemoveTagByIdAsync(int songId, int tagId)
    {
        var songCollection = _database.GetCollection<Song>("songs");
        var song = songCollection
            .Include(x => x.Tags)
            .FindById(songId);

        if (song != null)
        {
            var tagToRemove = song.Tags.FirstOrDefault(t => t.Id == tagId);

            if (tagToRemove != null)
            {
                song.Tags.Remove(tagToRemove);
                song.UpdatedAt = DateTime.UtcNow;
                songCollection.Update(song);
            }
        }

        return Task.CompletedTask;
    }

    public async Task AddTagsAsync(int songId, IEnumerable<string> tagNames)
    {
        if (tagNames == null)
            throw new ArgumentNullException(nameof(tagNames));

        var tagNameList = tagNames.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (tagNameList.Count == 0)
            return;

        try
        {
            // Single query to load song with tags
            var songCollection = _database.GetCollection<Song>("songs");
            var song = songCollection
                .Include(x => x.Tags)
                .FindById(songId);

            if (song != null)
            {
                var tagRepo = new LiteDbTagRepository(_database);
                bool updated = false;

                foreach (var tagName in tagNameList)
                {
                    // Check if tag already exists on song
                    if (song.Tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Properly await async call
                    var tag = await tagRepo.CreateOrGetAsync(tagName);
                    song.Tags.Add(tag);
                    updated = true;
                }

                if (updated)
                {
                    song.UpdatedAt = DateTime.UtcNow;
                    songCollection.Update(song);  // Single update for all tags
                }
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbSongRepository] Error adding tags to song {SongId}", songId);
            throw;
        }
    }

    public Task RemoveTagsAsync(int songId, IEnumerable<string> tagNames)
    {
        if (tagNames == null)
            throw new ArgumentNullException(nameof(tagNames));

        var tagNameList = tagNames.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (tagNameList.Count == 0)
            return Task.CompletedTask;

        // Single query to load song with tags
        var songCollection = _database.GetCollection<Song>("songs");
        var song = songCollection
            .Include(x => x.Tags)
            .FindById(songId);

        if (song != null)
        {
            var tagsToRemove = song.Tags
                .Where(t => tagNameList.Any(tn => tn.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (tagsToRemove.Count > 0)
            {
                foreach (var tag in tagsToRemove)
                {
                    song.Tags.Remove(tag);
                }
                song.UpdatedAt = DateTime.UtcNow;
                songCollection.Update(song);  // Single update for all tags
            }
        }

        return Task.CompletedTask;
    }
}
