using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using LiteDB;

namespace MidiBard.Playlist;

internal enum DatabaseOperationMode
{
    Read,
    Write
}

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

    private readonly AsyncLocalDatabaseGate _localGate = new();
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
        return ExecuteWrite(action);
    }

    internal T ExecuteRead<T>(Func<LiteDatabase, T> action)
    {
        return Execute(DatabaseOperationMode.Read, action);
    }

    internal void ExecuteRead(Action<LiteDatabase> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExecuteRead(database =>
        {
            action(database);
            return true;
        });
    }

    internal T ExecuteWrite<T>(Func<LiteDatabase, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Execute(DatabaseOperationMode.Write, action);
    }

    internal void ExecuteWrite(Action<LiteDatabase> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExecuteWrite(database =>
        {
            action(database);
            return true;
        });
    }

    private T Execute<T>(DatabaseOperationMode mode, Func<LiteDatabase, T> action)
    {
        ThrowIfDisposed();

        var existingContext = _current.Value;
        if (existingContext != null)
            return action(existingContext.Database);

        var localLease = _localGate.EnterAsync(GetLocalGateMode(mode)).GetAwaiter().GetResult();
        IDisposable? lockLease = null;
        LiteDatabase? operationDatabase = null;
        try
        {
            ThrowIfDisposed();
            lockLease = AcquireDatabaseLock(mode);
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
            localLease.Dispose();
        }
    }

    internal void Execute(Action<LiteDatabase> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExecuteWrite(action);
    }

    private async Task<T> ExecuteAsync<T>(DatabaseOperationMode mode, Func<LiteDatabase, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();

        var existingContext = _current.Value;
        if (existingContext != null)
            return await action(existingContext.Database).ConfigureAwait(false);

        var localLease = await _localGate.EnterAsync(GetLocalGateMode(mode)).ConfigureAwait(false);
        IDisposable? lockLease = null;
        LiteDatabase? operationDatabase = null;
        try
        {
            ThrowIfDisposed();
            lockLease = AcquireDatabaseLock(mode);
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
            localLease.Dispose();
        }
    }

    private async Task ExecuteAsync(DatabaseOperationMode mode, Func<LiteDatabase, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await ExecuteAsync(mode, async database =>
        {
            await action(database).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private IDisposable AcquireDatabaseLock(DatabaseOperationMode mode)
    {
        return mode == DatabaseOperationMode.Read
            ? _databaseLock.AcquireRead()
            : _databaseLock.AcquireWrite();
    }

    private DatabaseOperationMode GetLocalGateMode(DatabaseOperationMode mode)
    {
        return _openPerOperation ? mode : DatabaseOperationMode.Write;
    }

    private Task<T> WithSongRepository<T>(DatabaseOperationMode mode, Func<ISongRepository, Task<T>> action)
    {
        return ExecuteAsync(mode, database => action(new LiteDbSongRepository(database)));
    }

    private Task WithSongRepository(DatabaseOperationMode mode, Func<ISongRepository, Task> action)
    {
        return ExecuteAsync(mode, database => action(new LiteDbSongRepository(database)));
    }

    private Task<T> WithPlaylistRepository<T>(DatabaseOperationMode mode, Func<IPlaylistRepository, Task<T>> action)
    {
        return ExecuteAsync(mode, database => action(new LiteDbPlaylistRepository(database)));
    }

    private Task WithPlaylistRepository(DatabaseOperationMode mode, Func<IPlaylistRepository, Task> action)
    {
        return ExecuteAsync(mode, database => action(new LiteDbPlaylistRepository(database)));
    }

    private Task<T> WithTagRepository<T>(DatabaseOperationMode mode, Func<ITagRepository, Task<T>> action)
    {
        return ExecuteAsync(mode, database => action(new LiteDbTagRepository(database)));
    }

    private Task WithTagRepository(DatabaseOperationMode mode, Func<ITagRepository, Task> action)
    {
        return ExecuteAsync(mode, database => action(new LiteDbTagRepository(database)));
    }

    Task<Song?> ISongRepository.GetSongByIdAsync(int id) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetSongByIdAsync(id));

    Task<Song?> ISongRepository.GetSongByIdLightAsync(int id) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetSongByIdLightAsync(id));

    Task<Song?> ISongRepository.GetByIdAsync(int id) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetByIdAsync(id));

    Task<Song?> ISongRepository.GetByFilePathAsync(string filePath) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetByFilePathAsync(filePath));

    Task<Song?> ISongRepository.GetByFilePathWithTagsAsync(string filePath) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetByFilePathWithTagsAsync(filePath));

    Task<Song> ISongRepository.CreateOrGetSongAsync(
        string filePath,
        string name,
        string artist,
        int releaseYear,
        TimeSpan duration,
        bool isValid,
        DateTime fileLastModifiedAt) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.CreateOrGetSongAsync(
            filePath,
            name,
            artist,
            releaseYear,
            duration,
            isValid,
            fileLastModifiedAt));

    Task<List<Song>> ISongRepository.BulkInsertSongsAsync(IEnumerable<Song> songs) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.BulkInsertSongsAsync(songs));

    Task<List<Song>> ISongRepository.GetAllSongsAsync() =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetAllSongsAsync());

    Task<List<Song>> ISongRepository.GetAllSongsWithTagsAsync() =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetAllSongsWithTagsAsync());

    Task ISongRepository.UpdateAsync(Song song) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.UpdateAsync(song));

    Task ISongRepository.DeleteAsync(int id) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.DeleteAsync(id));

    Task ISongRepository.DeleteAllAsync() =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.DeleteAllAsync());

    Task ISongRepository.IncrementPlayCountAsync(int songId) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.IncrementPlayCountAsync(songId));

    Task ISongRepository.AddTagAsync(int songId, string tag) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.AddTagAsync(songId, tag));

    Task ISongRepository.RemoveTagAsync(int songId, string tag) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.RemoveTagAsync(songId, tag));

    Task ISongRepository.RemoveTagByIdAsync(int songId, int tagId) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.RemoveTagByIdAsync(songId, tagId));

    Task<List<Song>> ISongRepository.GetSongsByIdsAsync(IEnumerable<int> songIds) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetSongsByIdsAsync(songIds));

    Task<List<Song>> ISongRepository.GetSongsByIdsWithTagsAsync(IEnumerable<int> songIds) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetSongsByIdsWithTagsAsync(songIds));

    Task ISongRepository.AddTagsAsync(int songId, IEnumerable<string> tagNames) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.AddTagsAsync(songId, tagNames));

    Task ISongRepository.RemoveTagsAsync(int songId, IEnumerable<string> tagNames) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.RemoveTagsAsync(songId, tagNames));

    Task<List<Song>> ISongRepository.BulkReplaceFilePathPrefixAsync(string oldPrefix, string newPrefix) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.BulkReplaceFilePathPrefixAsync(oldPrefix, newPrefix));

    Task<int> ISongRepository.BulkUpdateAsync(IEnumerable<Song> songs) =>
        WithSongRepository(DatabaseOperationMode.Write, repository => repository.BulkUpdateAsync(songs));

    Task<Song?> ISongRepository.GetBySyncIdAsync(int syncId) =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetBySyncIdAsync(syncId));

    Task<int> ISongRepository.GetMaxSyncIdAsync() =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetMaxSyncIdAsync());

    Task<List<int>> ISongRepository.GetAllSyncIdsAsync() =>
        WithSongRepository(DatabaseOperationMode.Read, repository => repository.GetAllSyncIdsAsync());

    Task<Playlist?> IPlaylistRepository.GetByIdAsync(int id) =>
        WithPlaylistRepository(DatabaseOperationMode.Read, repository => repository.GetByIdAsync(id));

    Task<Playlist?> IPlaylistRepository.GetByIdLightAsync(int id) =>
        WithPlaylistRepository(DatabaseOperationMode.Read, repository => repository.GetByIdLightAsync(id));

    Task<List<Playlist>> IPlaylistRepository.GetAllAsync() =>
        WithPlaylistRepository(DatabaseOperationMode.Read, repository => repository.GetAllAsync());

    Task<List<Playlist>> IPlaylistRepository.GetAllWithSongsAsync() =>
        WithPlaylistRepository(DatabaseOperationMode.Read, repository => repository.GetAllWithSongsAsync());

    Task<Playlist> IPlaylistRepository.CreateAsync(Playlist playlist) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.CreateAsync(playlist));

    Task IPlaylistRepository.UpdateAsync(Playlist playlist) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.UpdateAsync(playlist));

    Task IPlaylistRepository.DeleteAsync(int id) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.DeleteAsync(id));

    Task IPlaylistRepository.AddSongToPlaylistAsync(int playlistId, int songId, int order) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.AddSongToPlaylistAsync(playlistId, songId, order));

    Task IPlaylistRepository.RemoveSongFromPlaylistAsync(int playlistId, int songId) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.RemoveSongFromPlaylistAsync(playlistId, songId));

    Task IPlaylistRepository.ReorderSongAsync(int playlistId, int songId, int newOrder) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.ReorderSongAsync(playlistId, songId, newOrder));

    Task IPlaylistRepository.SetSongPlayedStatusAsync(int playlistId, int songId, bool isPlayed) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.SetSongPlayedStatusAsync(playlistId, songId, isPlayed));

    Task IPlaylistRepository.ResetAllSongsPlayedStatusAsync(int playlistId) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.ResetAllSongsPlayedStatusAsync(playlistId));

    Task IPlaylistRepository.RemoveAllSongsAsync(int playlistId) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.RemoveAllSongsAsync(playlistId));

    Task IPlaylistRepository.ClearAllPlaylistsAsync() =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.ClearAllPlaylistsAsync());

    Task IPlaylistRepository.ReorderAllSongsAsync(int playlistId, List<int> songIdsInOrder) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.ReorderAllSongsAsync(playlistId, songIdsInOrder));

    Task IPlaylistRepository.BulkAddSongsToPlaylistAsync(int playlistId, IEnumerable<int> songIds) =>
        WithPlaylistRepository(DatabaseOperationMode.Write, repository => repository.BulkAddSongsToPlaylistAsync(playlistId, songIds));

    Task<Tag?> ITagRepository.GetByIdAsync(int id) =>
        WithTagRepository(DatabaseOperationMode.Read, repository => repository.GetByIdAsync(id));

    Task<Tag?> ITagRepository.GetByNameAsync(string name) =>
        WithTagRepository(DatabaseOperationMode.Read, repository => repository.GetByNameAsync(name));

    Task<List<Tag>> ITagRepository.GetAllAsync() =>
        WithTagRepository(DatabaseOperationMode.Read, repository => repository.GetAllAsync());

    Task<Tag> ITagRepository.CreateAsync(string name) =>
        WithTagRepository(DatabaseOperationMode.Write, repository => repository.CreateAsync(name));

    Task<Tag> ITagRepository.CreateOrGetAsync(string name) =>
        WithTagRepository(DatabaseOperationMode.Write, repository => repository.CreateOrGetAsync(name));

    Task ITagRepository.UpdateAsync(Tag tag) =>
        WithTagRepository(DatabaseOperationMode.Write, repository => repository.UpdateAsync(tag));

    Task ITagRepository.DeleteAsync(int id) =>
        WithTagRepository(DatabaseOperationMode.Write, repository => repository.DeleteAsync(id));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_disposeSharedDatabase)
            _sharedDatabase?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiteDbRepositorySession));
    }

    private sealed class AsyncLocalDatabaseGate
    {
        private readonly object _sync = new();
        private readonly Queue<TaskCompletionSource<IDisposable>> _waitingReaders = new();
        private readonly Queue<TaskCompletionSource<IDisposable>> _waitingWriters = new();
        private int _activeReaders;
        private bool _writerActive;

        public Task<IDisposable> EnterAsync(DatabaseOperationMode mode)
        {
            return mode == DatabaseOperationMode.Read
                ? EnterReadAsync()
                : EnterWriteAsync();
        }

        private Task<IDisposable> EnterReadAsync()
        {
            lock (_sync)
            {
                if (!_writerActive && _waitingWriters.Count == 0)
                {
                    _activeReaders++;
                    return Task.FromResult<IDisposable>(new Releaser(this, DatabaseOperationMode.Read));
                }

                var waiter = CreateWaiter();
                _waitingReaders.Enqueue(waiter);
                return waiter.Task;
            }
        }

        private Task<IDisposable> EnterWriteAsync()
        {
            lock (_sync)
            {
                if (!_writerActive && _activeReaders == 0)
                {
                    _writerActive = true;
                    return Task.FromResult<IDisposable>(new Releaser(this, DatabaseOperationMode.Write));
                }

                var waiter = CreateWaiter();
                _waitingWriters.Enqueue(waiter);
                return waiter.Task;
            }
        }

        private void Release(DatabaseOperationMode mode)
        {
            TaskCompletionSource<IDisposable>? writerToRelease = null;
            List<TaskCompletionSource<IDisposable>>? readersToRelease = null;

            lock (_sync)
            {
                if (mode == DatabaseOperationMode.Read)
                {
                    _activeReaders--;
                }
                else
                {
                    _writerActive = false;
                }

                if (!_writerActive && _activeReaders == 0 && _waitingWriters.Count > 0)
                {
                    _writerActive = true;
                    writerToRelease = _waitingWriters.Dequeue();
                }
                else if (!_writerActive && _waitingWriters.Count == 0 && _waitingReaders.Count > 0)
                {
                    readersToRelease = new List<TaskCompletionSource<IDisposable>>(_waitingReaders.Count);
                    while (_waitingReaders.Count > 0)
                    {
                        _activeReaders++;
                        readersToRelease.Add(_waitingReaders.Dequeue());
                    }
                }
            }

            writerToRelease?.SetResult(new Releaser(this, DatabaseOperationMode.Write));

            if (readersToRelease == null)
                return;

            foreach (var reader in readersToRelease)
                reader.SetResult(new Releaser(this, DatabaseOperationMode.Read));
        }

        private static TaskCompletionSource<IDisposable> CreateWaiter()
        {
            return new TaskCompletionSource<IDisposable>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly AsyncLocalDatabaseGate _owner;
            private readonly DatabaseOperationMode _mode;
            private bool _disposed;

            public Releaser(AsyncLocalDatabaseGate owner, DatabaseOperationMode mode)
            {
                _owner = owner;
                _mode = mode;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.Release(_mode);
            }
        }
    }
}
