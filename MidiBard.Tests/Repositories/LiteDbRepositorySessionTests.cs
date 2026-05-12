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
                maxActive = Math.Max(maxActive, current);
                Thread.Sleep(25);
                Interlocked.Decrement(ref active);
            })))
            .ToArray();

        await Task.WhenAll(tasks);

        maxActive.ShouldBe(1);
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
}

public class WineDatabaseFileLockTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public WineDatabaseFileLockTests()
    {
        DalamudTestSetup.Initialize();
        _directory = Path.Combine(Path.GetTempPath(), "midibard-lock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "midibard.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Acquire_ConcurrentCall_WaitsForRelease()
    {
        var databaseLock = new WineDatabaseFileLock(
            _databasePath,
            timeout: TimeSpan.FromSeconds(5));

        using var firstLease = databaseLock.Acquire();
        var entered = false;

        var secondAcquire = Task.Run(() =>
        {
            using var secondLease = databaseLock.Acquire();
            entered = true;
        });

        await Task.Delay(150);
        entered.ShouldBeFalse();

        firstLease.Dispose();
        await secondAcquire.WaitAsync(TimeSpan.FromSeconds(2));
        entered.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ReleasesAndRemovesLockFile()
    {
        var lockPath = _databasePath + ".lock";
        var databaseLock = new WineDatabaseFileLock(
            _databasePath,
            timeout: TimeSpan.FromSeconds(2));

        using (databaseLock.Acquire())
        {
            File.Exists(lockPath).ShouldBeTrue();
        }

        File.Exists(lockPath).ShouldBeFalse();
    }

    [Fact]
    public void Acquire_ActiveLock_TimesOutWithOwnerMetadata()
    {
        var databaseLock = new WineDatabaseFileLock(
            _databasePath,
            timeout: TimeSpan.FromMilliseconds(250));

        using var firstLease = databaseLock.Acquire();

        var exception = Should.Throw<TimeoutException>(() => databaseLock.Acquire());

        exception.Message.ShouldContain(_databasePath + ".lock");
        exception.Message.ShouldContain("pid=");
        File.Exists(_databasePath + ".lock").ShouldBeTrue();
    }
}
