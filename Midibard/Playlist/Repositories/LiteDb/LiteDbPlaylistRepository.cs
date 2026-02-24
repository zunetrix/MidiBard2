using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

/// <summary>
/// LiteDB implementation of IPlaylistRepository using embedded PlaylistSong documents.
/// Each Playlist stores its songs as an embedded array, so order is determined by array position.
/// </summary>
public class LiteDbPlaylistRepository : IPlaylistRepository
{
    private readonly LiteDatabase _database;
    private readonly ISongRepository _songRepository;

    public LiteDbPlaylistRepository(LiteDatabase database, ISongRepository songRepository)
    {
        _database = database;
        _songRepository = songRepository;
    }

    // ==================== Playlist Operations ====================

    public Task<Playlist?> GetByIdAsync(int id)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(id);

        if (playlist == null)
            return Task.FromResult<Playlist?>(null);

        // Batch load all Song references for embedded PlaylistSong documents
        if (playlist.Songs != null && playlist.Songs.Count > 0)
        {
            var songCollection = _database.GetCollection<Song>("songs");

            // Collect all unique song IDs that need to be loaded
            var songIds = playlist.Songs
                .Where(ps => ps.Song?.Id > 0)
                .Select(ps => ps.Song!.Id)
                .Distinct()
                .ToList();

            if (songIds.Count > 0)
            {
                // Load all songs in one query instead of N queries (include Tags)
                var songs = songCollection.Include(x => x.Tags).Find(x => songIds.Contains(x.Id)).ToList();
                var songDict = songs.ToDictionary(s => s.Id);

                // Assign loaded songs to PlaylistSongs
                foreach (var ps in playlist.Songs)
                {
                    if (ps.Song?.Id > 0 && songDict.TryGetValue(ps.Song.Id, out var song))
                    {
                        ps.Song = song;
                    }
                }
            }
        }

        return Task.FromResult<Playlist?>(playlist);
    }

    public Task<List<Playlist>> GetAllAsync()
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlists = collection.FindAll().ToList();

        // Batch load all Song references for embedded PlaylistSong documents
        var songCollection = _database.GetCollection<Song>("songs");

        // Collect all unique song IDs across all playlists
        var allSongIds = playlists
            .Where(p => p.Songs != null && p.Songs.Count > 0)
            .SelectMany(p => p.Songs)
            .Where(ps => ps.Song?.Id > 0)
            .Select(ps => ps.Song!.Id)
            .Distinct()
            .ToList();

        // Load all songs in one query
        var songDict = new Dictionary<int, Song>();
        if (allSongIds.Count > 0)
        {
            var songs = songCollection.Find(x => allSongIds.Contains(x.Id)).ToList();
            songDict = songs.ToDictionary(s => s.Id);
        }

        // Assign loaded songs to all playlists
        foreach (var playlist in playlists)
        {
            if (playlist.Songs != null && playlist.Songs.Count > 0)
            {
                foreach (var ps in playlist.Songs)
                {
                    if (ps.Song?.Id > 0 && songDict.TryGetValue(ps.Song.Id, out var song))
                    {
                        ps.Song = song;
                    }
                }
            }
        }

        return Task.FromResult(playlists);
    }

    public Task<Playlist> CreateAsync(Playlist playlist)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        playlist.CreatedAt = DateTime.UtcNow;
        playlist.UpdatedAt = DateTime.UtcNow;
        playlist.Songs = new List<PlaylistSong>();
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
        // No need to delete playlist_songs separately - they're embedded
        return Task.CompletedTask;
    }

    // ==================== PlaylistSong Operations (Embedded) ====================

    public async Task AddSongToPlaylistAsync(int playlistId, int songId, int order)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);

        if (playlist == null)
            return;

        // Ensure Songs is initialized
        if (playlist.Songs == null)
            playlist.Songs = new List<PlaylistSong>();

        // Check if song already exists in playlist
        if (playlist.Songs.Any(ps => ps.Song?.Id == songId))
            return;

        var song = await _songRepository.GetSongByIdAsync(songId);
        if (song == null)
            return;

        // Create new PlaylistSong (embedded)
        var playlistSong = new PlaylistSong
        {
            Id = GeneratePlaylistSongId(playlist.Songs),
            Song = song,
            IsPlayed = false,
            AddedAt = DateTime.UtcNow
        };

        // Add to array - order parameter determines position
        if (order >= 0 && order <= playlist.Songs.Count)
        {
            playlist.Songs.Insert(order, playlistSong);
        }
        else
        {
            playlist.Songs.Add(playlistSong);
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Update(playlist);
    }

    public Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);

        if (playlist == null)
            return Task.CompletedTask;

        // Ensure Songs is initialized
        if (playlist.Songs == null)
            playlist.Songs = new List<PlaylistSong>();

        // Remove from embedded array - O(1) operation
        var songToRemove = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
        if (songToRemove != null)
        {
            playlist.Songs.Remove(songToRemove);
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task ReorderSongAsync(int playlistId, int songId, int newOrder)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);

        if (playlist == null)
            return Task.CompletedTask;

        // Ensure Songs is initialized
        if (playlist.Songs == null)
            playlist.Songs = new List<PlaylistSong>();

        // Find and remove the song from current position
        var songToMove = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
        if (songToMove == null)
            return Task.CompletedTask;

        playlist.Songs.Remove(songToMove);

        // Insert at new position
        var clampedOrder = Math.Max(0, Math.Min(newOrder, playlist.Songs.Count));
        playlist.Songs.Insert(clampedOrder, songToMove);

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

        // Ensure Songs is initialized
        if (playlist.Songs == null)
            playlist.Songs = new List<PlaylistSong>();

        var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
        if (playlistSong != null)
        {
            playlistSong.IsPlayed = true;
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task ResetAllSongsPlayedStatusAsync(int playlistId)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);

        if (playlist == null)
            return Task.CompletedTask;

        if (playlist.Songs == null)
            playlist.Songs = new List<PlaylistSong>();

        var changed = false;
        foreach (var ps in playlist.Songs)
        {
            if (ps.IsPlayed)
            {
                ps.IsPlayed = false;
                changed = true;
            }
        }

        if (changed)
        {
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAllSongsAsync(int playlistId)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);

        if (playlist == null)
            return Task.CompletedTask;

        // Clear all songs from the playlist in a single operation
        if (playlist.Songs != null && playlist.Songs.Count > 0)
        {
            playlist.Songs.Clear();
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }

        return Task.CompletedTask;
    }

    public Task ClearAllPlaylistsAsync()
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlists = collection.FindAll().ToList();

        // Clear songs from all playlists
        foreach (var playlist in playlists)
        {
            if (playlist.Songs != null && playlist.Songs.Count > 0)
            {
                playlist.Songs.Clear();
                playlist.UpdatedAt = DateTime.UtcNow;
                collection.Update(playlist);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReorderAllSongsAsync(int playlistId, List<int> songIdsInOrder)
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);

        if (playlist == null || playlist.Songs == null || playlist.Songs.Count == 0)
            return Task.CompletedTask;

        if (songIdsInOrder == null || songIdsInOrder.Count == 0)
            return Task.CompletedTask;

        // Build a dictionary of songs by ID for quick lookup
        var songDict = playlist.Songs
            .Where(ps => ps.Song?.Id > 0)
            .ToDictionary(ps => ps.Song!.Id);

        // Create new ordered list
        var newSongList = new List<PlaylistSong>();
        foreach (var songId in songIdsInOrder)
        {
            if (songDict.TryGetValue(songId, out var ps))
            {
                newSongList.Add(ps);
            }
        }

        // Update the playlist's song list
        playlist.Songs = newSongList;
        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Update(playlist);

        return Task.CompletedTask;
    }

    // ==================== Helper Methods ====================


    /// <summary>
    /// Generate a unique ID for embedded PlaylistSong documents
    /// </summary>
    private int GeneratePlaylistSongId(List<PlaylistSong> songs)
    {
        if (songs.Count == 0)
            return 1;

        return songs.Max(s => s.Id) + 1;
    }
}
