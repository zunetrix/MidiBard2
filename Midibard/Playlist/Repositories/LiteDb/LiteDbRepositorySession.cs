using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

public sealed class LiteDbRepositorySession :
    ISongRepository,
    IPlaylistRepository,
    ITagRepository,
    IDisposable
{
    private sealed class SessionContext
    {
        public LiteDatabase Database { get; init; } = null!;
    }

    private readonly SemaphoreSlim _localLock = new(1, 1);
    private readonly AsyncLocal<SessionContext?> _current = new();
    private readonly Func<LiteDatabase> _databaseFactory;
    private readonly LiteDatabase? _sharedDatabase;
    private readonly IDatabaseLock _databaseLock;
    private readonly bool _disposeSharedDatabase;
    private readonly bool _openPerOperation;
    private bool _disposed;

    private LiteDbRepositorySession(
        Func<LiteDatabase> databaseFactory,
        LiteDatabase? sharedDatabase,
        IDatabaseLock databaseLock,
        bool disposeSharedDatabase,
        bool openPerOperation)
    {
        _databaseFactory = databaseFactory;
        _sharedDatabase = sharedDatabase;
        _databaseLock = databaseLock;
        _disposeSharedDatabase = disposeSharedDatabase;
        _openPerOperation = openPerOperation;
    }

    public static LiteDbRepositorySession ForDatabase(LiteDatabase database, bool disposeDatabase = false)
    {
        ArgumentNullException.ThrowIfNull(database);

        return new LiteDbRepositorySession(
            () => database,
            database,
            NoOpDatabaseLock.Instance,
            disposeDatabase,
            openPerOperation: false);
    }

    public static LiteDbRepositorySession ForFile(
        string databasePath,
        IDatabaseLock databaseLock,
        bool openPerOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(databaseLock);

        LiteDatabase CreateDatabase()
        {
            var connectionString = new ConnectionString
            {
                Filename = databasePath,
                Connection = ConnectionType.Shared
            };

            return new LiteDatabase(connectionString);
        }

        var sharedDatabase = openPerOperation ? null : CreateDatabase();
        return new LiteDbRepositorySession(
            CreateDatabase,
            sharedDatabase,
            databaseLock,
            disposeSharedDatabase: sharedDatabase != null,
            openPerOperation);
    }

    internal T Execute<T>(Func<LiteDatabase, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();

        var existingContext = _current.Value;
        if (existingContext != null)
            return action(existingContext.Database);

        _localLock.Wait();
        IDisposable? lockLease = null;
        LiteDatabase? operationDatabase = null;
        try
        {
            ThrowIfDisposed();
            lockLease = _databaseLock.Acquire();
            operationDatabase = _openPerOperation ? _databaseFactory() : _sharedDatabase ?? _databaseFactory();
            _current.Value = new SessionContext { Database = operationDatabase };
            return action(operationDatabase);
        }
        finally
        {
            _current.Value = null;

            if (_openPerOperation)
                operationDatabase?.Dispose();

            lockLease?.Dispose();
            _localLock.Release();
        }
    }

    internal void Execute(Action<LiteDatabase> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Execute(database =>
        {
            action(database);
            return true;
        });
    }

    private async Task<T> ExecuteAsync<T>(Func<LiteDatabase, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();

        var existingContext = _current.Value;
        if (existingContext != null)
            return await action(existingContext.Database).ConfigureAwait(false);

        await _localLock.WaitAsync().ConfigureAwait(false);
        IDisposable? lockLease = null;
        LiteDatabase? operationDatabase = null;
        try
        {
            ThrowIfDisposed();
            lockLease = _databaseLock.Acquire();
            operationDatabase = _openPerOperation ? _databaseFactory() : _sharedDatabase ?? _databaseFactory();
            _current.Value = new SessionContext { Database = operationDatabase };
            return await action(operationDatabase).ConfigureAwait(false);
        }
        finally
        {
            _current.Value = null;

            if (_openPerOperation)
                operationDatabase?.Dispose();

            lockLease?.Dispose();
            _localLock.Release();
        }
    }

    private async Task ExecuteAsync(Func<LiteDatabase, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await ExecuteAsync(async database =>
        {
            await action(database).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private Task<T> WithSongRepository<T>(Func<ISongRepository, Task<T>> action)
    {
        return ExecuteAsync(database => action(new LiteDbSongRepository(database)));
    }

    private Task WithSongRepository(Func<ISongRepository, Task> action)
    {
        return ExecuteAsync(database => action(new LiteDbSongRepository(database)));
    }

    private Task<T> WithPlaylistRepository<T>(Func<IPlaylistRepository, Task<T>> action)
    {
        return ExecuteAsync(database => action(new LiteDbPlaylistRepository(database)));
    }

    private Task WithPlaylistRepository(Func<IPlaylistRepository, Task> action)
    {
        return ExecuteAsync(database => action(new LiteDbPlaylistRepository(database)));
    }

    private Task<T> WithTagRepository<T>(Func<ITagRepository, Task<T>> action)
    {
        return ExecuteAsync(database => action(new LiteDbTagRepository(database)));
    }

    private Task WithTagRepository(Func<ITagRepository, Task> action)
    {
        return ExecuteAsync(database => action(new LiteDbTagRepository(database)));
    }

    Task<Song?> ISongRepository.GetSongByIdAsync(int id) =>
        WithSongRepository(repository => repository.GetSongByIdAsync(id));

    Task<Song?> ISongRepository.GetSongByIdLightAsync(int id) =>
        WithSongRepository(repository => repository.GetSongByIdLightAsync(id));

    Task<Song?> ISongRepository.GetByIdAsync(int id) =>
        WithSongRepository(repository => repository.GetByIdAsync(id));

    Task<Song?> ISongRepository.GetByFilePathAsync(string filePath) =>
        WithSongRepository(repository => repository.GetByFilePathAsync(filePath));

    Task<Song?> ISongRepository.GetByFilePathWithTagsAsync(string filePath) =>
        WithSongRepository(repository => repository.GetByFilePathWithTagsAsync(filePath));

    Task<Song> ISongRepository.CreateOrGetSongAsync(
        string filePath,
        string name,
        string artist,
        int releaseYear,
        TimeSpan duration,
        bool isValid,
        DateTime fileLastModifiedAt) =>
        WithSongRepository(repository => repository.CreateOrGetSongAsync(
            filePath,
            name,
            artist,
            releaseYear,
            duration,
            isValid,
            fileLastModifiedAt));

    Task<List<Song>> ISongRepository.BulkInsertSongsAsync(IEnumerable<Song> songs) =>
        WithSongRepository(repository => repository.BulkInsertSongsAsync(songs));

    Task<List<Song>> ISongRepository.GetAllSongsAsync() =>
        WithSongRepository(repository => repository.GetAllSongsAsync());

    Task<List<Song>> ISongRepository.GetAllSongsWithTagsAsync() =>
        WithSongRepository(repository => repository.GetAllSongsWithTagsAsync());

    Task ISongRepository.UpdateAsync(Song song) =>
        WithSongRepository(repository => repository.UpdateAsync(song));

    Task ISongRepository.DeleteAsync(int id) =>
        WithSongRepository(repository => repository.DeleteAsync(id));

    Task ISongRepository.DeleteAllAsync() =>
        WithSongRepository(repository => repository.DeleteAllAsync());

    Task ISongRepository.IncrementPlayCountAsync(int songId) =>
        WithSongRepository(repository => repository.IncrementPlayCountAsync(songId));

    Task ISongRepository.AddTagAsync(int songId, string tag) =>
        WithSongRepository(repository => repository.AddTagAsync(songId, tag));

    Task ISongRepository.RemoveTagAsync(int songId, string tag) =>
        WithSongRepository(repository => repository.RemoveTagAsync(songId, tag));

    Task ISongRepository.RemoveTagByIdAsync(int songId, int tagId) =>
        WithSongRepository(repository => repository.RemoveTagByIdAsync(songId, tagId));

    Task<List<Song>> ISongRepository.GetSongsByIdsAsync(IEnumerable<int> songIds) =>
        WithSongRepository(repository => repository.GetSongsByIdsAsync(songIds));

    Task<List<Song>> ISongRepository.GetSongsByIdsWithTagsAsync(IEnumerable<int> songIds) =>
        WithSongRepository(repository => repository.GetSongsByIdsWithTagsAsync(songIds));

    Task ISongRepository.AddTagsAsync(int songId, IEnumerable<string> tagNames) =>
        WithSongRepository(repository => repository.AddTagsAsync(songId, tagNames));

    Task ISongRepository.RemoveTagsAsync(int songId, IEnumerable<string> tagNames) =>
        WithSongRepository(repository => repository.RemoveTagsAsync(songId, tagNames));

    Task<List<Song>> ISongRepository.BulkReplaceFilePathPrefixAsync(string oldPrefix, string newPrefix) =>
        WithSongRepository(repository => repository.BulkReplaceFilePathPrefixAsync(oldPrefix, newPrefix));

    Task<int> ISongRepository.BulkUpdateAsync(IEnumerable<Song> songs) =>
        WithSongRepository(repository => repository.BulkUpdateAsync(songs));

    Task<Song?> ISongRepository.GetBySyncIdAsync(int syncId) =>
        WithSongRepository(repository => repository.GetBySyncIdAsync(syncId));

    Task<int> ISongRepository.GetMaxSyncIdAsync() =>
        WithSongRepository(repository => repository.GetMaxSyncIdAsync());

    Task<List<int>> ISongRepository.GetAllSyncIdsAsync() =>
        WithSongRepository(repository => repository.GetAllSyncIdsAsync());

    Task<Playlist?> IPlaylistRepository.GetByIdAsync(int id) =>
        WithPlaylistRepository(repository => repository.GetByIdAsync(id));

    Task<Playlist?> IPlaylistRepository.GetByIdLightAsync(int id) =>
        WithPlaylistRepository(repository => repository.GetByIdLightAsync(id));

    Task<List<Playlist>> IPlaylistRepository.GetAllAsync() =>
        WithPlaylistRepository(repository => repository.GetAllAsync());

    Task<List<Playlist>> IPlaylistRepository.GetAllWithSongsAsync() =>
        WithPlaylistRepository(repository => repository.GetAllWithSongsAsync());

    Task<Playlist> IPlaylistRepository.CreateAsync(Playlist playlist) =>
        WithPlaylistRepository(repository => repository.CreateAsync(playlist));

    Task IPlaylistRepository.UpdateAsync(Playlist playlist) =>
        WithPlaylistRepository(repository => repository.UpdateAsync(playlist));

    Task IPlaylistRepository.DeleteAsync(int id) =>
        WithPlaylistRepository(repository => repository.DeleteAsync(id));

    Task IPlaylistRepository.AddSongToPlaylistAsync(int playlistId, int songId, int order) =>
        WithPlaylistRepository(repository => repository.AddSongToPlaylistAsync(playlistId, songId, order));

    Task IPlaylistRepository.RemoveSongFromPlaylistAsync(int playlistId, int songId) =>
        WithPlaylistRepository(repository => repository.RemoveSongFromPlaylistAsync(playlistId, songId));

    Task IPlaylistRepository.ReorderSongAsync(int playlistId, int songId, int newOrder) =>
        WithPlaylistRepository(repository => repository.ReorderSongAsync(playlistId, songId, newOrder));

    Task IPlaylistRepository.SetSongPlayedStatusAsync(int playlistId, int songId, bool isPlayed) =>
        WithPlaylistRepository(repository => repository.SetSongPlayedStatusAsync(playlistId, songId, isPlayed));

    Task IPlaylistRepository.ResetAllSongsPlayedStatusAsync(int playlistId) =>
        WithPlaylistRepository(repository => repository.ResetAllSongsPlayedStatusAsync(playlistId));

    Task IPlaylistRepository.RemoveAllSongsAsync(int playlistId) =>
        WithPlaylistRepository(repository => repository.RemoveAllSongsAsync(playlistId));

    Task IPlaylistRepository.ClearAllPlaylistsAsync() =>
        WithPlaylistRepository(repository => repository.ClearAllPlaylistsAsync());

    Task IPlaylistRepository.ReorderAllSongsAsync(int playlistId, List<int> songIdsInOrder) =>
        WithPlaylistRepository(repository => repository.ReorderAllSongsAsync(playlistId, songIdsInOrder));

    Task IPlaylistRepository.BulkAddSongsToPlaylistAsync(int playlistId, IEnumerable<int> songIds) =>
        WithPlaylistRepository(repository => repository.BulkAddSongsToPlaylistAsync(playlistId, songIds));

    Task<Tag?> ITagRepository.GetByIdAsync(int id) =>
        WithTagRepository(repository => repository.GetByIdAsync(id));

    Task<Tag?> ITagRepository.GetByNameAsync(string name) =>
        WithTagRepository(repository => repository.GetByNameAsync(name));

    Task<List<Tag>> ITagRepository.GetAllAsync() =>
        WithTagRepository(repository => repository.GetAllAsync());

    Task<Tag> ITagRepository.CreateAsync(string name) =>
        WithTagRepository(repository => repository.CreateAsync(name));

    Task<Tag> ITagRepository.CreateOrGetAsync(string name) =>
        WithTagRepository(repository => repository.CreateOrGetAsync(name));

    Task ITagRepository.UpdateAsync(Tag tag) =>
        WithTagRepository(repository => repository.UpdateAsync(tag));

    Task ITagRepository.DeleteAsync(int id) =>
        WithTagRepository(repository => repository.DeleteAsync(id));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_disposeSharedDatabase)
            _sharedDatabase?.Dispose();

        _localLock.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiteDbRepositorySession));
    }
}
