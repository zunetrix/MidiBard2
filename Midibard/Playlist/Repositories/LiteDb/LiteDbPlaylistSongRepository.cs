using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

/// <summary>
/// LiteDB implementation of IPlaylistSongRepository
/// </summary>
public class LiteDbPlaylistSongRepository : IPlaylistSongRepository
{
    private readonly LiteDatabase _database;

    public LiteDbPlaylistSongRepository(LiteDatabase database)
    {
        _database = database;
    }

    public Task<PlaylistSong?> GetByIdAsync(int id)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSong = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .FindById(id);

        return Task.FromResult<PlaylistSong?>(playlistSong);
    }

    public Task<List<PlaylistSong>> GetByPlaylistIdAsync(int playlistId)
    {
        if (playlistId <= 0)
            throw new ArgumentException("Playlist ID must be positive", nameof(playlistId));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSongs = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .Find(x => x.Playlist != null && x.Playlist.Id == playlistId)
            .OrderBy(x => x.Order)
            .ToList();

        return Task.FromResult(playlistSongs);
    }

    public Task<PlaylistSong?> GetByPlaylistAndSongAsync(int playlistId, int songId)
    {
        if (playlistId <= 0)
            throw new ArgumentException("Playlist ID must be positive", nameof(playlistId));
        if (songId <= 0)
            throw new ArgumentException("Song ID must be positive", nameof(songId));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSong = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .FindOne(x => x.Playlist != null && x.Playlist.Id == playlistId
                        && x.Song != null && x.Song.Id == songId);

        return Task.FromResult<PlaylistSong?>(playlistSong);
    }

    public Task<PlaylistSong> CreateAsync(PlaylistSong playlistSong)
    {
        if (playlistSong == null)
            throw new ArgumentNullException(nameof(playlistSong));
        if (playlistSong.Playlist == null)
            throw new ArgumentException("Playlist cannot be null", nameof(playlistSong));
        if (playlistSong.Song == null)
            throw new ArgumentException("Song cannot be null", nameof(playlistSong));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        playlistSong.AddedAt = DateTime.UtcNow;
        collection.Insert(playlistSong);
        return Task.FromResult(playlistSong);
    }

    public Task UpdateAsync(PlaylistSong playlistSong)
    {
        if (playlistSong == null)
            throw new ArgumentNullException(nameof(playlistSong));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        collection.Update(playlistSong);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("ID must be positive", nameof(id));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        collection.Delete(id);

        return Task.CompletedTask;
    }

    public Task MarkAsPlayedAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("ID must be positive", nameof(id));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSong = collection.FindById(id);

        if (playlistSong != null)
        {
            playlistSong.IsPlayed = true;
            collection.Update(playlistSong);
        }

        return Task.CompletedTask;
    }

    public Task ResetPlayedStatusAsync(int id)
    {
        if (id <= 0)
            throw new ArgumentException("ID must be positive", nameof(id));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSong = collection.FindById(id);

        if (playlistSong != null)
        {
            playlistSong.IsPlayed = false;
            collection.Update(playlistSong);
        }

        return Task.CompletedTask;
    }

    public Task ReorderAsync(int playlistId, int songId, int newOrder)
    {
        if (playlistId <= 0)
            throw new ArgumentException("Playlist ID must be positive", nameof(playlistId));
        if (songId <= 0)
            throw new ArgumentException("Song ID must be positive", nameof(songId));
        if (newOrder < 0)
            throw new ArgumentException("Order must be non-negative", nameof(newOrder));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");

        var playlistSong = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .FindOne(x => x.Playlist != null && x.Playlist.Id == playlistId
                        && x.Song != null && x.Song.Id == songId);

        if (playlistSong == null)
            return Task.CompletedTask;

        var oldOrder = playlistSong.Order;

        // Get all songs in playlist with relationships loaded
        var allSongs = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .Find(x => x.Playlist != null && x.Playlist.Id == playlistId)
            .ToList();

        // Reorder based on direction
        foreach (var ps in allSongs)
        {
            if (ps.Id == playlistSong.Id)
            {
                ps.Order = newOrder;
            }
            else if (oldOrder < newOrder)
            {
                // Moving down: shift items between old and new position up
                if (ps.Order > oldOrder && ps.Order <= newOrder)
                    ps.Order--;
            }
            else
            {
                // Moving up: shift items between new and old position down
                if (ps.Order >= newOrder && ps.Order < oldOrder)
                    ps.Order++;
            }
            collection.Update(ps);
        }

        return Task.CompletedTask;
    }

    public Task<int> GetPlaylistSongCountAsync(int playlistId)
    {
        if (playlistId <= 0)
            throw new ArgumentException("Playlist ID must be positive", nameof(playlistId));

        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var count = collection
            .Count(x => x.Playlist != null && x.Playlist.Id == playlistId);

        return Task.FromResult(count);
    }
}
