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

    public Task<Song?> GetSongByIdAsync(int id)
    {
        var collection = _database.GetCollection<Song>("songs");

        // Use Include to automatically load Tags DbRef
        var song = collection
            .Include(x => x.Tags)
            .FindById(id);

        return Task.FromResult<Song?>(song);
    }

    public Task<Song?> GetByIdAsync(int id)
    {
        return GetSongByIdAsync(id);
    }

    public Task<Song?> GetByFilePathAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Task.FromResult<Song?>(null);

        var collection = _database.GetCollection<Song>("songs");
        var song = collection
            .Include(x => x.Tags)
            .FindOne(x => x.FilePath == filePath);
        return Task.FromResult<Song?>(song);
    }

    public Task<Song> CreateOrGetSongAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration, bool hasValidFilePath = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("FilePath cannot be empty", nameof(filePath));

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
            if (existingSong.HasValidFilePath != hasValidFilePath)
            {
                existingSong.HasValidFilePath = hasValidFilePath;
                updated = true;
            }
            if (updated)
            {
                existingSong.UpdatedAt = DateTime.UtcNow;
                collection.Update(existingSong);
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
            HasValidFilePath = hasValidFilePath,
            Tags = new List<Tag>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        collection.Insert(newSong);
        return Task.FromResult(newSong);
    }

    public Task<List<Song>> GetAllSongsAsync()
    {
        var collection = _database.GetCollection<Song>("songs");

        // Use Include to automatically load Tags DbRef
        var songs = collection
            .Include(x => x.Tags)
            .FindAll()
            .ToList();

        return Task.FromResult(songs);
    }

    public Task<List<Song>> GetSongsByIdsAsync(IEnumerable<int> songIds)
    {
        if (songIds == null)
            throw new ArgumentNullException(nameof(songIds));

        var ids = songIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Task.FromResult(new List<Song>());

        var collection = _database.GetCollection<Song>("songs");

        // Single batch query instead of N queries
        var songs = collection
            .Include(x => x.Tags)
            .Find(x => ids.Contains(x.Id))
            .ToList();

        return Task.FromResult(songs);
    }

    public Task UpdateAsync(Song song)
    {
        if (song == null)
            throw new ArgumentNullException(nameof(song));
        if (string.IsNullOrWhiteSpace(song.Name))
            throw new ArgumentException("Song name cannot be empty", nameof(song));

        var collection = _database.GetCollection<Song>("songs");
        song.UpdatedAt = DateTime.UtcNow;
        collection.Update(song);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        var collection = _database.GetCollection<Song>("songs");
        collection.Delete(id);
        return Task.CompletedTask;
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
        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindById(songId);

        if (song != null)
        {
            song.PlayCount++;
            song.LastPlayedAt = DateTime.UtcNow;
            song.UpdatedAt = DateTime.UtcNow;
            collection.Update(song);
        }

        return Task.CompletedTask;
    }

    public Task SetRatingAsync(int songId, int rate)
    {
        if (rate < 0 || rate > 10)
            throw new ArgumentException("Rating must be between 0 and 10", nameof(rate));

        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindById(songId);

        if (song != null)
        {
            song.Rating = rate;
            song.UpdatedAt = DateTime.UtcNow;
            collection.Update(song);
        }

        return Task.CompletedTask;
    }

    public Task AddTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

        var songCollection = _database.GetCollection<Song>("songs");
        var song = songCollection
            .Include(x => x.Tags)
            .FindById(songId);

        if (song != null)
        {
            // Get or create tag
            var tagRepo = new LiteDbTagRepository(_database);
            var tag = tagRepo.CreateOrGetAsync(tagName).Result;

            // Add tag reference to song if not already present
            if (!song.Tags.Any(t => t.Id == tag.Id))
            {
                song.Tags.Add(tag);
                song.UpdatedAt = DateTime.UtcNow;
                songCollection.Update(song);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveTagAsync(int songId, string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name cannot be empty", nameof(tagName));

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
            }
        }

        return Task.CompletedTask;
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

    public Task AddTagsAsync(int songId, IEnumerable<string> tagNames)
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
            var tagRepo = new LiteDbTagRepository(_database);
            bool updated = false;

            foreach (var tagName in tagNameList)
            {
                // Check if tag already exists on song
                if (song.Tags.Any(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var tag = tagRepo.CreateOrGetAsync(tagName).Result;
                song.Tags.Add(tag);
                updated = true;
            }

            if (updated)
            {
                song.UpdatedAt = DateTime.UtcNow;
                songCollection.Update(song);  // Single update for all tags
            }
        }

        return Task.CompletedTask;
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
