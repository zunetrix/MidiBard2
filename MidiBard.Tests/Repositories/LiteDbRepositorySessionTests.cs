using LiteDB;

using MidiBard.Playlist;
using MidiBard.Tests.Infrastructure;

namespace MidiBard.Tests.Repositories;

public class LiteDbRepositorySessionTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDbRepositorySession _session;

    public LiteDbRepositorySessionTests()
    {
        DalamudTestSetup.Initialize();
        _database = new LiteDatabase(new MemoryStream());
        _session = LiteDbRepositorySession.ForDatabase(_database);
    }

    public void Dispose()
    {
        _session.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Execute_ConcurrentCalls_SerializesInProcess()
    {
        var active = 0;
        var maxActive = 0;
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _session.Execute(database =>
            {
                var current = Interlocked.Increment(ref active);
                TrackMax(ref maxActive, current);
                Thread.Sleep(25);
                Interlocked.Decrement(ref active);
            })))
            .ToArray();

        await Task.WhenAll(tasks);

        maxActive.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteRead_OpenPerOperation_AllowsConcurrentReaders()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var session = LiteDbRepositorySession.ForFile(
                Path.Combine(directory, "midibard.db"),
                new RecordingDatabaseLock(),
                openPerOperation: true);
            var active = 0;
            var maxActive = 0;
            var tasks = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => session.ExecuteRead(database =>
                {
                    var current = Interlocked.Increment(ref active);
                    TrackMax(ref maxActive, current);
                    Thread.Sleep(25);
                    Interlocked.Decrement(ref active);
                })))
                .ToArray();

            await Task.WhenAll(tasks);

            maxActive.ShouldBeGreaterThan(1);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Execute_NestedRepositoryCall_DoesNotDeadlock()
    {
        _session.Execute(database =>
        {
            database.GetCollection<Song>("songs").Insert(new Song { FilePath = "nested.mid", Name = "Nested" });

            var songRepository = (ISongRepository)_session;
            var songs = songRepository.GetAllSongsAsync().GetAwaiter().GetResult();

            songs.Count.ShouldBe(1);
        });
    }

    [Fact]
    public void ExecuteWrite_NestedRepositoryCall_DoesNotReacquireDatabaseLock()
    {
        var directory = CreateTempDirectory();
        try
        {
            var databaseLock = new RecordingDatabaseLock();
            using var session = LiteDbRepositorySession.ForFile(
                Path.Combine(directory, "midibard.db"),
                databaseLock,
                openPerOperation: true);

            session.ExecuteWrite(database =>
            {
                database.GetCollection<Song>("songs").Insert(new Song { FilePath = "nested.mid", Name = "Nested" });

                var songRepository = (ISongRepository)session;
                var songs = songRepository.GetAllSongsAsync().GetAwaiter().GetResult();

                songs.Count.ShouldBe(1);
            });

            databaseLock.WriteCount.ShouldBe(1);
            databaseLock.ReadCount.ShouldBe(0);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RepositoryCalls_AcquireReadOrWriteLocks()
    {
        var directory = CreateTempDirectory();
        try
        {
            var databaseLock = new RecordingDatabaseLock();
            using var session = LiteDbRepositorySession.ForFile(
                Path.Combine(directory, "midibard.db"),
                databaseLock,
                openPerOperation: true);
            ISongRepository songRepository = session;
            IPlaylistRepository playlistRepository = session;

            await songRepository.GetAllSongsAsync();
            await playlistRepository.GetAllAsync();
            databaseLock.ReadCount.ShouldBe(2);
            databaseLock.WriteCount.ShouldBe(0);

            var song = await songRepository.CreateOrGetSongAsync("write.mid", "Write", "", 0, TimeSpan.Zero);
            await songRepository.IncrementPlayCountAsync(song.Id);

            databaseLock.WriteCount.ShouldBe(2);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RepositoryConcurrentIncrements_PreserveFinalPlayCount()
    {
        ISongRepository repository = _session;
        var song = await repository.CreateOrGetSongAsync("play-count.mid", "Play Count", "", 0, TimeSpan.Zero);

        var increments = Enumerable.Range(0, 20)
            .Select(_ => repository.IncrementPlayCountAsync(song.Id));

        await Task.WhenAll(increments);

        var loaded = await repository.GetByIdAsync(song.Id);
        loaded!.PlayCount.ShouldBe(20);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "midibard-session-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static void TrackMax(ref int maxValue, int value)
    {
        while (true)
        {
            var currentMax = Volatile.Read(ref maxValue);
            if (value <= currentMax)
                return;

            if (Interlocked.CompareExchange(ref maxValue, value, currentMax) == currentMax)
                return;
        }
    }
}

public class WineDatabaseFileLockTests : IDisposable
{
    private readonly string _directory;
    private readonly string _otherDirectory;
    private readonly string _databasePath;

    public WineDatabaseFileLockTests()
    {
        DalamudTestSetup.Initialize();
        _directory = Path.Combine(Path.GetTempPath(), "midibard-lock-tests", Guid.NewGuid().ToString("N"));
        _otherDirectory = Path.Combine(Path.GetTempPath(), "midibard-lock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(_otherDirectory);
        _databasePath = Path.Combine(_directory, "midibard.db");
    }

    public void Dispose()
    {
        DeleteDirectory(_directory);
        DeleteDirectory(_otherDirectory);
    }

    [Fact]
    public void PathResolver_EnvironmentOverrideWins()
    {
        var previous = Environment.GetEnvironmentVariable(DatabaseLockPathResolver.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DatabaseLockPathResolver.OverrideEnvironmentVariable, _otherDirectory);

            var path = DatabaseLockPathResolver.Resolve(
                _databasePath,
                new[] { _directory });

            path.LockDirectory.ShouldBe(Path.GetFullPath(_otherDirectory));
            path.UsesFallbackDirectory.ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseLockPathResolver.OverrideEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void PathResolver_FallsBackToDatabaseDirectoryWhenCandidatesUnavailable()
    {
        var path = DatabaseLockPathResolver.Resolve(
            _databasePath,
            new[] { "relative-lock-dir" });

        path.LockDirectory.ShouldBe(Path.GetFullPath(_directory));
        path.UsesFallbackDirectory.ShouldBeTrue();
    }

    [Fact]
    public void PathResolver_UsesStableDatabasePathHash()
    {
        var first = DatabaseLockPathResolver.GetLockNamePrefix(_databasePath);
        var second = DatabaseLockPathResolver.GetLockNamePrefix(_databasePath);
        var other = DatabaseLockPathResolver.GetLockNamePrefix(Path.Combine(_directory, "other.db"));

        second.ShouldBe(first);
        other.ShouldNotBe(first);
        first.ShouldStartWith("midibard-");
    }

    [Fact]
    public async Task AcquireWrite_ConcurrentWrite_WaitsForRelease()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromSeconds(5));

        using var firstLease = databaseLock.AcquireWrite();
        var entered = false;

        var secondAcquire = Task.Run(() =>
        {
            using var secondLease = databaseLock.AcquireWrite();
            entered = true;
        });

        await Task.Delay(150);
        entered.ShouldBeFalse();

        firstLease.Dispose();
        await secondAcquire.WaitAsync(TimeSpan.FromSeconds(2));
        entered.ShouldBeTrue();
    }

    [Fact]
    public void AcquireRead_AllowsConcurrentReaders()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromSeconds(2));

        using var firstReader = databaseLock.AcquireRead();
        using var secondReader = databaseLock.AcquireRead();

        Directory.GetDirectories(_directory, GetReadLockPattern()).Length.ShouldBe(2);
    }

    [Fact]
    public async Task AcquireWrite_WaitsForReaders()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromSeconds(5));

        using var readerLease = databaseLock.AcquireRead();
        var entered = false;

        var writerAcquire = Task.Run(() =>
        {
            using var writerLease = databaseLock.AcquireWrite();
            entered = true;
        });

        await Task.Delay(150);
        entered.ShouldBeFalse();

        readerLease.Dispose();
        await writerAcquire.WaitAsync(TimeSpan.FromSeconds(2));
        entered.ShouldBeTrue();
    }

    [Fact]
    public async Task AcquireRead_WaitsForPendingWriter()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromSeconds(5));

        using var firstReader = databaseLock.AcquireRead();
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new ManualResetEventSlim();

        var writerTask = Task.Run(() =>
        {
            using var writerLease = databaseLock.AcquireWrite();
            writerEntered.SetResult();
            releaseWriter.Wait(TimeSpan.FromSeconds(3));
        });

        SpinWait.SpinUntil(() => Directory.Exists(GetWriteLockPath()), TimeSpan.FromSeconds(2)).ShouldBeTrue();

        var readerEntered = false;
        var secondReaderTask = Task.Run(() =>
        {
            using var secondReader = databaseLock.AcquireRead();
            readerEntered = true;
        });

        await Task.Delay(150);
        readerEntered.ShouldBeFalse();

        firstReader.Dispose();
        await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(150);
        readerEntered.ShouldBeFalse();

        releaseWriter.Set();
        await writerTask.WaitAsync(TimeSpan.FromSeconds(2));
        await secondReaderTask.WaitAsync(TimeSpan.FromSeconds(2));
        readerEntered.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ReleasesAndRemovesLockDirectories()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromSeconds(2));

        using (databaseLock.AcquireRead())
        {
            Directory.GetDirectories(_directory, GetReadLockPattern()).Length.ShouldBe(1);
        }

        Directory.GetDirectories(_directory, GetReadLockPattern()).Length.ShouldBe(0);

        using (databaseLock.AcquireWrite())
        {
            Directory.Exists(GetWriteLockPath()).ShouldBeTrue();
        }

        Directory.Exists(GetWriteLockPath()).ShouldBeFalse();
    }

    [Fact]
    public void Dispose_DoesNotRemoveForeignOwnedLockDirectory()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromSeconds(2));

        using (var lease = databaseLock.AcquireWrite())
        {
            Directory.Exists(GetWriteLockPath()).ShouldBeTrue();
            File.WriteAllText(
                Path.Combine(GetWriteLockPath(), "owner.json"),
                """
                {
                  "Token": "foreign-owner"
                }
                """);

            lease.Dispose();
        }

        Directory.Exists(GetWriteLockPath()).ShouldBeTrue();
    }

    [Fact]
    public void AcquireWrite_ActiveWriter_TimesOutWithOwnerMetadata()
    {
        var databaseLock = CreateDatabaseLock(timeout: TimeSpan.FromMilliseconds(250));

        using var firstLease = databaseLock.AcquireWrite();

        var exception = Should.Throw<TimeoutException>(() => databaseLock.AcquireWrite());

        exception.Message.ShouldContain(GetWriteLockPath());
        exception.Message.ShouldContain("pid=");
        Directory.Exists(GetWriteLockPath()).ShouldBeTrue();
    }

    private WineDatabaseFileLock CreateDatabaseLock(TimeSpan timeout)
    {
        return new WineDatabaseFileLock(
            _databasePath,
            timeout,
            new[] { _directory });
    }

    private string GetWriteLockPath()
    {
        return Path.Combine(
            _directory,
            DatabaseLockPathResolver.GetLockNamePrefix(_databasePath) + ".write.lockdir");
    }

    private string GetReadLockPattern()
    {
        return DatabaseLockPathResolver.GetLockNamePrefix(_databasePath) + ".read.*.lockdir";
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

internal sealed class RecordingDatabaseLock : IDatabaseLock
{
    private int _activeReads;
    private int _activeWrites;

    public int ReadCount;
    public int WriteCount;
    public int MaxActiveReads;
    public int MaxActiveWrites;

    public IDisposable AcquireRead(CancellationToken cancellationToken = default)
    {
        ReadCount++;
        var active = Interlocked.Increment(ref _activeReads);
        TrackMax(ref MaxActiveReads, active);
        return new DelegateLease(() => Interlocked.Decrement(ref _activeReads));
    }

    public IDisposable AcquireWrite(CancellationToken cancellationToken = default)
    {
        WriteCount++;
        var active = Interlocked.Increment(ref _activeWrites);
        TrackMax(ref MaxActiveWrites, active);
        return new DelegateLease(() => Interlocked.Decrement(ref _activeWrites));
    }

    private static void TrackMax(ref int maxValue, int value)
    {
        while (true)
        {
            var currentMax = Volatile.Read(ref maxValue);
            if (value <= currentMax)
                return;

            if (Interlocked.CompareExchange(ref maxValue, value, currentMax) == currentMax)
                return;
        }
    }

    private sealed class DelegateLease : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public DelegateLease(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _onDispose();
        }
    }
}
