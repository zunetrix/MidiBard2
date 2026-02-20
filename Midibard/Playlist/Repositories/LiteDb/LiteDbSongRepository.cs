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

    public Task<Song?> GetByIdAsync(int id)
    {
        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindById(id);
        return Task.FromResult<Song?>(song);
    }

    public Task<Song?> GetByFilePathAsync(string filePath)
    {
        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindOne(x => x.FilePath == filePath);
        return Task.FromResult<Song?>(song);
    }

    public Task<Song> CreateOrGetSongAsync(string filePath, string name, string artist, int releaseYear, TimeSpan duration)
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        collection.Insert(newSong);
        return Task.FromResult(newSong);
    }

    public Task<List<Song>> GetAllSongsAsync()
    {
        var collection = _database.GetCollection<Song>("songs");
        var songs = collection.FindAll().ToList();
        return Task.FromResult(songs);
    }

    public Task UpdateAsync(Song song)
    {
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

    public Task SetRatingAsync(int songId, double rate)
    {
        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindById(songId);

        if (song != null)
        {
            song.Rate = Math.Clamp(rate, 0, 5);
            song.UpdatedAt = DateTime.UtcNow;
            collection.Update(song);
        }

        return Task.CompletedTask;
    }

    public Task AddTagAsync(int songId, string tag)
    {
        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindById(songId);

        if (song != null)
        {
            if (!song.Tags.Contains(tag))
            {
                song.Tags.Add(tag);
                song.UpdatedAt = DateTime.UtcNow;
                collection.Update(song);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveTagAsync(int songId, string tag)
    {
        var collection = _database.GetCollection<Song>("songs");
        var song = collection.FindById(songId);

        if (song != null)
        {
            if (song.Tags.Remove(tag))
            {
                song.UpdatedAt = DateTime.UtcNow;
                collection.Update(song);
            }
        }

        return Task.CompletedTask;
    }
}
