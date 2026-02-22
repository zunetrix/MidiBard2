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

    // ==================== Playlist Operations ====================

    public Task<Playlist?> GetByIdAsync(int id)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(id);

        if (playlist != null)
        {
            // Load related songs from playlist_songs collection
            var playlistSongCollection = _database.GetCollection<PlaylistSong>("playlist_songs");
            playlist.Songs = playlistSongCollection.Find(x => x.PlaylistId == id)
                .OrderBy(x => x.Order)
                .ToList();
        }

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

        // Also delete all PlaylistSong entries for this playlist
        var playlistSongCollection = _database.GetCollection<PlaylistSong>("playlist_songs");
        playlistSongCollection.DeleteMany(x => x.PlaylistId == id);

        return Task.CompletedTask;
    }

    // ==================== PlaylistSong Operations (Join Table) ====================

    public Task AddSongToPlaylistAsync(int playlistId, int songId, int order)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");

        // Check if already exists
        var existing = collection.FindOne(x => x.PlaylistId == playlistId && x.SongId == songId);
        if (existing != null)
        {
            return Task.CompletedTask;
        }

        var playlistSong = new PlaylistSong
        {
            PlaylistId = playlistId,
            SongId = songId,
            Order = order,
            AddedAt = DateTime.UtcNow
        };
        collection.Insert(playlistSong);

        // Update playlist timestamp
        var playlistCollection = _database.GetCollection<Playlist>("playlists");
        var playlist = playlistCollection.FindById(playlistId);
        if (playlist != null)
        {
            playlist.UpdatedAt = DateTime.UtcNow;
            playlistCollection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        collection.DeleteMany(x => x.PlaylistId == playlistId && x.SongId == songId);

        // Reorder remaining songs
        var remaining = collection.Find(x => x.PlaylistId == playlistId).OrderBy(x => x.Order).ToList();
        for (int i = 0; i < remaining.Count; i++)
        {
            if (remaining[i].Order != i)
            {
                remaining[i].Order = i;
                collection.Update(remaining[i]);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReorderSongAsync(int playlistId, int songId, int newOrder)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSong = collection.FindOne(x => x.PlaylistId == playlistId && x.SongId == songId);

        if (playlistSong == null)
            return Task.CompletedTask;

        var oldOrder = playlistSong.Order;

        // Get all songs in playlist
        var allSongs = collection.Find(x => x.PlaylistId == playlistId).ToList();

        foreach (var ps in allSongs)
        {
            if (ps.SongId == songId)
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

    public Task MarkAsPlayedAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var playlistSong = collection.FindOne(x => x.PlaylistId == playlistId && x.SongId == songId);

        if (playlistSong != null)
        {
            playlistSong.IsPlayed = true;
            collection.Update(playlistSong);
        }

        return Task.CompletedTask;
    }
}
