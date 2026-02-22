using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

/// <summary>
/// DEPRECATED: This repository is no longer used.
/// PlaylistSong is now embedded inside Playlist documents instead of being a separate collection.
/// See LiteDbPlaylistRepository for PlaylistSong operations.
/// </summary>
[Obsolete("Use LiteDbPlaylistRepository for PlaylistSong operations instead")]
public class LiteDbPlaylistSongRepository : IPlaylistSongRepository
{
    private readonly LiteDatabase _database;

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public LiteDbPlaylistSongRepository(LiteDatabase database)
    {
        _database = database;
    }

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task<PlaylistSong?> GetByIdAsync(int id) => Task.FromResult<PlaylistSong?>(null);

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task<List<PlaylistSong>> GetByPlaylistIdAsync(int playlistId) => Task.FromResult(new List<PlaylistSong>());

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task<PlaylistSong?> GetByPlaylistAndSongAsync(int playlistId, int songId) => Task.FromResult<PlaylistSong?>(null);

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task<PlaylistSong> CreateAsync(PlaylistSong playlistSong) => Task.FromResult(playlistSong);

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task UpdateAsync(PlaylistSong playlistSong) => Task.CompletedTask;

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task DeleteAsync(int id) => Task.CompletedTask;

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task MarkAsPlayedAsync(int id) => Task.CompletedTask;

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task ResetPlayedStatusAsync(int id) => Task.CompletedTask;

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task ReorderAsync(int playlistId, int songId, int newOrder) => Task.CompletedTask;

    [Obsolete("Use LiteDbPlaylistRepository instead")]
    public Task<int> GetPlaylistSongCountAsync(int playlistId) => Task.FromResult(0);
}
