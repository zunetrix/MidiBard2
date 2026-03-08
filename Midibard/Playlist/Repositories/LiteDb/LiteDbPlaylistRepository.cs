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

    public Task<Playlist?> GetByIdAsync(int id) => Task.Run<Playlist?>(() =>
    {
        try
        {
            var collection = _database.GetCollection<Playlist>("playlists");
            var playlist = collection.FindById(id);
            if (playlist == null)
                return null;

            // Batch load all Song references for embedded PlaylistSong documents
            if (playlist.Songs != null && playlist.Songs.Count > 0)
            {
                var songIds = playlist.Songs
                    .Where(ps => ps.Song?.Id > 0)
                    .Select(ps => ps.Song!.Id)
                    .Distinct()
                    .ToList();

                if (songIds.Count > 0)
                {
                    var songs = _database.GetCollection<Song>("songs")
                        .Include(x => x.Tags)
                        .Find(x => songIds.Contains(x.Id))
                        .ToList();
                    var songDict = songs.ToDictionary(s => s.Id);

                    foreach (var ps in playlist.Songs)
                    {
                        if (ps.Song?.Id > 0 && songDict.TryGetValue(ps.Song.Id, out var song))
                            ps.Song = song;
                    }
                }
            }

            DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Loaded playlist {playlist.Id}: {playlist.Name} with {playlist.Songs?.Count ?? 0} songs");
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbPlaylistRepository] Error getting playlist {id}");
            throw;
        }
    });

    public Task<Playlist?> GetByIdLightAsync(int id) => Task.Run<Playlist?>(() =>
    {
        try
        {
            var playlist = _database.GetCollection<Playlist>("playlists").FindById(id);
            if (playlist == null)
                return null;

            DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Loaded playlist {id} (lightweight)");
            return playlist;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbPlaylistRepository] Error getting playlist {id}");
            throw;
        }
    });

    public Task<List<Playlist>> GetAllAsync() => Task.Run(() =>
    {
        try
        {
            var playlists = _database.GetCollection<Playlist>("playlists").FindAll().ToList();
            DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Loaded {playlists.Count} playlists (lightweight)");
            return playlists;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbPlaylistRepository] Error getting all playlists");
            throw;
        }
    });

    public Task<List<Playlist>> GetAllWithSongsAsync() => Task.Run(() =>
    {
        try
        {
            var collection = _database.GetCollection<Playlist>("playlists");
            var playlists = collection.FindAll().ToList();

            var allSongIds = playlists
                .Where(p => p.Songs != null && p.Songs.Count > 0)
                .SelectMany(p => p.Songs)
                .Where(ps => ps.Song?.Id > 0)
                .Select(ps => ps.Song.Id)
                .Distinct()
                .ToList();

            var songDict = new Dictionary<int, Song>();
            if (allSongIds.Count > 0)
            {
                var songs = _database.GetCollection<Song>("songs")
                    .Include(x => x.Tags)
                    .Find(x => allSongIds.Contains(x.Id))
                    .ToList();
                songDict = songs.ToDictionary(s => s.Id);
            }

            foreach (var playlist in playlists)
            {
                if (playlist.Songs == null) continue;
                foreach (var ps in playlist.Songs)
                {
                    if (ps.Song?.Id > 0 && songDict.TryGetValue(ps.Song.Id, out var song))
                        ps.Song = song;
                }
            }

            DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Loaded {playlists.Count} playlists with all songs");
            return playlists;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LiteDbPlaylistRepository] Error getting all playlists with songs");
            throw;
        }
    });

    public Task<Playlist> CreateAsync(Playlist playlist) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        playlist.CreatedAt = DateTime.UtcNow;
        playlist.UpdatedAt = DateTime.UtcNow;
        playlist.Songs = new List<PlaylistSong>();
        collection.Insert(playlist);
        return playlist;
    });

    public Task UpdateAsync(Playlist playlist) => Task.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (string.IsNullOrWhiteSpace(playlist.Name))
            throw new ArgumentException("Playlist name cannot be empty", nameof(playlist));

        try
        {
            playlist.UpdatedAt = DateTime.UtcNow;
            _database.GetCollection<Playlist>("playlists").Update(playlist);
            DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Updated playlist {playlist.Id}: {playlist.Name} with {playlist.Songs?.Count ?? 0} songs");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbPlaylistRepository] Error updating playlist {playlist.Id}");
            throw;
        }
    });

    public Task DeleteAsync(int id) => Task.Run(() =>
    {
        _database.GetCollection<Playlist>("playlists").Delete(id);
    });

    // ==================== PlaylistSong Operations (Embedded) ====================

    public Task AddSongToPlaylistAsync(int playlistId, int songId, int order) => Task.Run(async () =>
    {
        try
        {
            var collection = _database.GetCollection<Playlist>("playlists");
            var playlist = collection.FindById(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[LiteDbPlaylistRepository] Playlist {playlistId} not found for adding song");
                return;
            }

            playlist.Songs ??= new List<PlaylistSong>();

            if (playlist.Songs.Any(ps => ps.Song?.Id == songId))
            {
                DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Song {songId} already exists in playlist {playlistId}");
                return;
            }

            var song = await _songRepository.GetSongByIdAsync(songId);
            if (song == null)
            {
                DalamudApi.PluginLog.Warning($"[LiteDbPlaylistRepository] Song {songId} not found for adding to playlist {playlistId}");
                return;
            }

            var playlistSong = new PlaylistSong { Song = song, IsPlayed = false, AddedAt = DateTime.UtcNow };
            if (order >= 0 && order <= playlist.Songs.Count)
                playlist.Songs.Insert(order, playlistSong);
            else
                playlist.Songs.Add(playlistSong);

            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
            DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Added song {songId} to playlist {playlistId} at index {(order >= 0 ? order : playlist.Songs.Count - 1)}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbPlaylistRepository] Error adding song {songId} to playlist {playlistId}");
            throw;
        }
    });

    public Task RemoveSongFromPlaylistAsync(int playlistId, int songId) => Task.Run(() =>
    {
        try
        {
            var collection = _database.GetCollection<Playlist>("playlists");
            var playlist = collection.FindById(playlistId);
            if (playlist == null)
            {
                DalamudApi.PluginLog.Warning($"[LiteDbPlaylistRepository] Playlist {playlistId} not found for removing song");
                return;
            }

            playlist.Songs ??= new List<PlaylistSong>();

            var songToRemove = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
            if (songToRemove != null)
            {
                playlist.Songs.Remove(songToRemove);
                playlist.UpdatedAt = DateTime.UtcNow;
                collection.Update(playlist);
                DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Removed song {songId} from playlist {playlistId}");
            }
            else
            {
                DalamudApi.PluginLog.Debug($"[LiteDbPlaylistRepository] Song {songId} not found in playlist {playlistId}");
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"[LiteDbPlaylistRepository] Error removing song {songId} from playlist {playlistId}");
            throw;
        }
    });

    public Task ReorderSongAsync(int playlistId, int songId, int newOrder) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null) return;

        playlist.Songs ??= new List<PlaylistSong>();

        var songToMove = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
        if (songToMove == null) return;

        playlist.Songs.Remove(songToMove);
        playlist.Songs.Insert(Math.Clamp(newOrder, 0, playlist.Songs.Count), songToMove);
        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Update(playlist);
    });

    public Task SetSongPlayedStatusAsync(int playlistId, int songId, bool isPlayed) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null) return;

        playlist.Songs ??= new List<PlaylistSong>();

        var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == songId);
        if (playlistSong != null)
        {
            playlistSong.IsPlayed = isPlayed;
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }
    });

    public Task ResetAllSongsPlayedStatusAsync(int playlistId) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null) return;

        playlist.Songs ??= new List<PlaylistSong>();

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
    });

    public Task RemoveAllSongsAsync(int playlistId) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null) return;

        if (playlist.Songs != null && playlist.Songs.Count > 0)
        {
            playlist.Songs.Clear();
            playlist.UpdatedAt = DateTime.UtcNow;
            collection.Update(playlist);
        }
    });

    public Task ClearAllPlaylistsAsync() => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlists = collection.FindAll().ToList();

        foreach (var playlist in playlists)
        {
            if (playlist.Songs != null && playlist.Songs.Count > 0)
            {
                playlist.Songs.Clear();
                playlist.UpdatedAt = DateTime.UtcNow;
                collection.Update(playlist);
            }
        }
    });

    public Task ReorderAllSongsAsync(int playlistId, List<int> songIdsInOrder) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null || playlist.Songs == null || playlist.Songs.Count == 0) return;
        if (songIdsInOrder == null || songIdsInOrder.Count == 0) return;

        var songDict = playlist.Songs
            .Where(ps => ps.Song?.Id > 0)
            .ToDictionary(ps => ps.Song.Id);

        var newSongList = new List<PlaylistSong>();
        foreach (var songId in songIdsInOrder)
        {
            if (songDict.TryGetValue(songId, out var ps))
                newSongList.Add(ps);
        }

        playlist.Songs = newSongList;
        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Update(playlist);
    });

    public Task BulkAddSongsToPlaylistAsync(int playlistId, IEnumerable<int> songIds) => Task.Run(() =>
    {
        var collection = _database.GetCollection<Playlist>("playlists");
        var playlist = collection.FindById(playlistId);
        if (playlist == null) return;

        playlist.Songs ??= new List<PlaylistSong>();

        foreach (var songId in songIds)
        {
            playlist.Songs.Add(new PlaylistSong
            {
                Song = new Song { Id = songId },
                AddedAt = DateTime.UtcNow,
            });
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        collection.Update(playlist);
    });
}
