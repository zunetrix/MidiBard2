using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MidiBard.Playlist;

public interface IDatabaseLock
{
    IDisposable Acquire(CancellationToken cancellationToken = default);
}

public sealed class NoOpDatabaseLock : IDatabaseLock
{
    public static NoOpDatabaseLock Instance { get; } = new();

    private NoOpDatabaseLock() { }

    public IDisposable Acquire(CancellationToken cancellationToken = default) => NoOpLease.Instance;

    private sealed class NoOpLease : IDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public void Dispose() { }
    }
}

public sealed class WineDatabaseFileLock : IDatabaseLock
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly ThreadLocal<Random> Random = new(() => new Random(Guid.NewGuid().GetHashCode()));

    private readonly string _databasePath;
    private readonly string _lockPath;
    private readonly TimeSpan _timeout;

    public WineDatabaseFileLock(string databasePath, TimeSpan? timeout = null)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _lockPath = _databasePath + ".lock";
        _timeout = timeout ?? DefaultTimeout;
    }

    public IDisposable Acquire(CancellationToken cancellationToken = default)
    {
        var owner = CreateOwnerInfo();
        var startedAt = DateTimeOffset.UtcNow;
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow - startedAt < _timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var parent = Path.GetDirectoryName(_lockPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                var stream = new FileStream(
                    _lockPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read);

                WriteOwner(stream, owner);
                return new FileLockLease(stream, _lockPath, owner.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(Random.Value!.Next(50, 151)));
        }

        var currentOwner = TryReadOwnerSummary();
        throw new TimeoutException(
            $"Timed out waiting for LiteDB lock '{_lockPath}'. Current owner: {currentOwner}",
            lastError);
    }

    private DatabaseLockOwner CreateOwnerInfo()
    {
        return new DatabaseLockOwner
        {
            Token = Guid.NewGuid().ToString("N"),
            DatabasePath = _databasePath,
            ProcessId = Environment.ProcessId,
            ProcessName = Process.GetCurrentProcess().ProcessName,
            CharacterName = TryGetCharacterName(),
            ContentId = TryGetContentId(),
            AcquiredAt = DateTimeOffset.UtcNow
        };
    }

    private static string TryGetCharacterName()
    {
        try
        {
            return DalamudApi.PlayerState?.CharacterName.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ulong TryGetContentId()
    {
        try
        {
            return DalamudApi.PlayerState?.ContentId ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void WriteOwner(FileStream stream, DatabaseLockOwner owner)
    {
        var json = JsonSerializer.Serialize(owner, DatabaseLockJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);

        stream.SetLength(0);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private string TryReadOwnerSummary()
    {
        try
        {
            if (!File.Exists(_lockPath))
                return "none";

            var json = File.ReadAllText(_lockPath);
            var owner = JsonSerializer.Deserialize<DatabaseLockOwner>(json, DatabaseLockJson.Options);
            return owner?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string? TryReadOwnerToken(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath))
                return null;

            var json = File.ReadAllText(lockPath);
            var owner = JsonSerializer.Deserialize<DatabaseLockOwner>(json, DatabaseLockJson.Options);
            return owner?.Token;
        }
        catch
        {
            return null;
        }
    }

    private sealed class FileLockLease : IDisposable
    {
        private readonly FileStream _stream;
        private readonly string _lockPath;
        private readonly string _token;
        private bool _disposed;

        public FileLockLease(FileStream stream, string lockPath, string token)
        {
            _stream = stream;
            _lockPath = lockPath;
            _token = token;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stream.Dispose();

            try
            {
                if (TryReadOwnerToken(_lockPath) == _token)
                    File.Delete(_lockPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DalamudApi.PluginLog.Warning(ex, $"[DatabaseLock] Failed to release LiteDB lock '{_lockPath}'");
            }
        }
    }

    private sealed class DatabaseLockOwner
    {
        public string Token { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public ulong ContentId { get; set; }
        public DateTimeOffset AcquiredAt { get; set; }

        public override string ToString()
        {
            return $"{CharacterName} cid={ContentId} pid={ProcessId} process={ProcessName} acquired={AcquiredAt:O}";
        }
    }

    private static class DatabaseLockJson
    {
        public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true };
    }
}

public static class DatabaseLockFactory
{
    public static IDatabaseLock Create(string databasePath, bool useWineLock)
    {
        return useWineLock
            ? new WineDatabaseFileLock(databasePath)
            : NoOpDatabaseLock.Instance;
    }
}
