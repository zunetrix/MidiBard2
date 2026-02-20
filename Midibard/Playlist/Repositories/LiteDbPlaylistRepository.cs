using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

public class LiteDbPlaylistRepository : IPlaylistRepository
{
    private readonly LiteDatabase _database;

    public LiteDbPlaylistRepository(LiteDatabase database)
    {
        _database = database;
    }

    public Task<Playlist?> GetByIdAsync(int id)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(id);
        return Task.FromResult<Playlist?>(playlist);
    }

    public Task<Playlist?> GetByFilePathAsync(string filePath)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindOne(x => x.FilePath == filePath);
        return Task.FromResult<Playlist?>(playlist);
    }

    public Task<List<Playlist>> GetAllAsync()
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlists = collection.FindAll().ToList();
        return Task.FromResult(playlists);
    }

    public Task<Playlist> CreateAsync(Playlist playlist)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        playlist.CreatedAt = DateTime.UtcNow;
        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Insert(playlist);
        return Task.FromResult(playlist);
    }

    public Task UpdateAsync(Playlist playlist)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Update(playlist);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        collection.Delete(id);
        return Task.CompletedTask;
    }

    public Task<Song> AddSongAsync(int playlistId, Song song)
    {
        var playlist = _database.GetCollection<Playlist>("playlists").FindById(playlistId);
        if (playlist == null)
            throw new InvalidOperationException("Playlist not found");

        song.CreatedAt = DateTime.UtcNow;
        song.UpdatedAt = DateTime.UtcNow;
        playlist.Songs.Add(song);
        playlist.UpdatedAt = DateTime.UtcNow;

        _database.GetCollection<Playlist>("playlists").Update(playlist);
        return Task.FromResult(song);
    }

    public Task RemoveSongAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
        if (song != null)
        {
            playlist.Songs.Remove(song);
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task UpdateSongAsync(int playlistId, Song song)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var existingSong = playlist.Songs.FirstOrDefault(s => s.Id == song.Id);
        if (existingSong != null)
        {
            var index = playlist.Songs.IndexOf(existingSong);
            song.UpdatedAt = DateTime.UtcNow;
            playlist.Songs[index] = song;
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task ReorderSongAsync(int playlistId, int fromIndex, int toIndex)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        if (fromIndex < 0 || fromIndex >= playlist.Songs.Count)
            return Task.CompletedTask;

        if (toIndex < 0 || toIndex >= playlist.Songs.Count)
            return Task.CompletedTask;

        var song = playlist.Songs[fromIndex];
        playlist.Songs.RemoveAt(fromIndex);
        playlist.Songs.Insert(toIndex, song);
        playlist.UpdatedAt = DateTime.UtcNow;

        collection.Update(playlist);
        return Task.CompletedTask;
    }

    public Task MarkSongAsPlayedAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
        if (song != null)
        {
            song.IsSongPlayed = true;
            song.UpdatedAt = DateTime.UtcNow;
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task IncrementPlayCountAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
        if (song != null)
        {
            song.PlayCount++;
            song.LastPlayedAt = DateTime.UtcNow;
            song.UpdatedAt = DateTime.UtcNow;
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task SetSongRatingAsync(int playlistId, int songId, double rate)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
        if (song != null)
        {
            song.Rate = Math.Clamp(rate, 0, 5);
            song.UpdatedAt = DateTime.UtcNow;
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task AddSongTagAsync(int playlistId, int songId, string tag)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
        if (song != null)
        {
            if (!song.Tags.Contains(tag))
            {
                song.Tags.Add(tag);
                song.UpdatedAt = DateTime.UtcNow;
                playlist.UpdatedAt = DateTime.UtcNow;
                collection.Update(playlist);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveSongTagAsync(int playlistId, int songId, string tag)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null)
            return Task.CompletedTask;

        var song = playlist.Songs.FirstOrDefault(s => s.Id == songId);
        if (song != null)
        {
            if (song.Tags.Remove(tag))
            {
                song.UpdatedAt = DateTime.UtcNow;
                playlist.UpdatedAt = DateTime.UtcNow;
                collection.Update(playlist);
            }
        }

        return Task.CompletedTask;
    }
}
