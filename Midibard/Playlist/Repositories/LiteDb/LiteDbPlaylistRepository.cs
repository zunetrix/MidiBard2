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

        // Get the playlist by ID
        var playlist = collection.FindById(id);
        if (playlist == null)
            return Task.FromResult<Playlist?>(null);

        // Manually load all PlaylistSongs with their Song references
        var playlistSongCollection = _database.GetCollection<PlaylistSong>("playlist_songs");
        playlist.Songs = playlistSongCollection
            .Include(x => x.Song)
            .Find(x => x.Playlist != null && x.Playlist.Id == id)
            .OrderBy(x => x.Order)
            .ToList();

        return Task.FromResult<Playlist?>(playlist);
    }

    public Task<List<Playlist>> GetAllAsync()
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlists = collection.FindAll().ToList();

        // Manually load all PlaylistSongs with their Song references for each playlist
        var playlistSongCollection = _database.GetCollection<PlaylistSong>("playlist_songs");
        foreach (var playlist in playlists)
        {
            playlist.Songs = playlistSongCollection
                .Include(x => x.Song)
                .Find(x => x.Playlist != null && x.Playlist.Id == playlist.Id)
                .OrderBy(x => x.Order)
                .ToList();
        }

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
        var playlistSongs = playlistSongCollection
            .Find(x => x.Playlist != null && x.Playlist.Id == id)
            .ToList();

        foreach (var ps in playlistSongs)
        {
            playlistSongCollection.Delete(ps.Id);
        }

        return Task.CompletedTask;
    }

    // ==================== PlaylistSong Operations (Join Table) ====================

    public Task AddSongToPlaylistAsync(int playlistId, int songId, int order)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");

        // Check if already exists
        var playlistSongCollection = _database.GetCollection<PlaylistSong>("playlist_songs");
        var existing = playlistSongCollection.FindOne(x => x.Playlist != null && x.Playlist.Id == playlistId && x.Song != null && x.Song.Id == songId);
        if (existing != null)
        {
            return Task.CompletedTask;
        }

        // Load Playlist and Song objects for DbRef
        var playlistCollection = _database.GetCollection<Playlist>("playlists");
        var songCollection = _database.GetCollection<Song>("songs");

        var playlist = playlistCollection.FindById(playlistId);
        var song = songCollection.FindById(songId);

        if (playlist == null || song == null)
        {
            return Task.CompletedTask;
        }

        var playlistSong = new PlaylistSong
        {
            Playlist = playlist,
            Song = song,
            Order = order,
            AddedAt = DateTime.UtcNow
        };
        collection.Insert(playlistSong);

        // Update playlist timestamp
        playlist.UpdatedAt = DateTime.UtcNow;
        playlistCollection.Update(playlist);

        return Task.CompletedTask;
    }

    public Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");

        // Find the entry with relationships loaded
        var playlistSong = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .FindOne(x => x.Playlist != null && x.Playlist.Id == playlistId
                        && x.Song != null && x.Song.Id == songId);

        if (playlistSong == null)
            return Task.CompletedTask;

        // Store the order of the deleted song for optimization
        var deletedOrder = playlistSong.Order;
        collection.Delete(playlistSong.Id);

        // Optimization: Only reorder songs that come AFTER the deleted one
        // This reduces update operations from O(n) to O(n-k) where k is the position
        var songsToReorder = collection
            .Include(x => x.Playlist)
            .Find(x => x.Playlist != null && x.Playlist.Id == playlistId && x.Order > deletedOrder)
            .OrderBy(x => x.Order)
            .ToList();

        for (int i = 0; i < songsToReorder.Count; i++)
        {
            songsToReorder[i].Order--;
            collection.Update(songsToReorder[i]);
        }

        return Task.CompletedTask;
    }

    public Task ReorderSongAsync(int playlistId, int songId, int newOrder)
    {
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

    public Task MarkSongAsPlayedAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<PlaylistSong>("playlist_songs");

        var playlistSong = collection
            .Include(x => x.Playlist)
            .Include(x => x.Song)
            .FindOne(x => x.Playlist != null && x.Playlist.Id == playlistId
                        && x.Song != null && x.Song.Id == songId);

        if (playlistSong != null)
        {
            playlistSong.IsPlayed = true;
            collection.Update(playlistSong);
        }

        return Task.CompletedTask;
    }
}
