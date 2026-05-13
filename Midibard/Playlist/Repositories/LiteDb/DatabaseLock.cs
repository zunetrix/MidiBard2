using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MidiBard.Playlist;

public interface IDatabaseLock
{
    IDisposable AcquireRead(CancellationToken cancellationToken = default);
    IDisposable AcquireWrite(CancellationToken cancellationToken = default);
}

public sealed class NoOpDatabaseLock : IDatabaseLock
{
    public static NoOpDatabaseLock Instance { get; } = new();

    private NoOpDatabaseLock() { }

    public IDisposable AcquireRead(CancellationToken cancellationToken = default) => NoOpLease.Instance;

    public IDisposable AcquireWrite(CancellationToken cancellationToken = default) => NoOpLease.Instance;

    private sealed class NoOpLease : IDisposable
    {
        public static NoOpLease Instance { get; } = new();

        public void Dispose() { }
    }
}

public sealed record DatabaseLockPath(
    string LockDirectory,
    string LockNamePrefix,
    bool UsesFallbackDirectory);

public static class DatabaseLockPathResolver
{
    public const string OverrideEnvironmentVariable = "MIDIBARD_DB_LOCK_DIR";
    public const string DefaultWineTempLockDirectory = @"Z:\tmp\midibard2\db-locks";

    public static DatabaseLockPath Resolve(
        string databasePath,
        IEnumerable<string>? candidateDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var databaseFullPath = Path.GetFullPath(databasePath);
        var lockNamePrefix = GetLockNamePrefix(databaseFullPath);
        var candidates = new List<string>();
        var configuredDirectory = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            candidates.Add(configuredDirectory);

        if (candidateDirectories != null)
            candidates.AddRange(candidateDirectories.Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
        else
            candidates.Add(DefaultWineTempLockDirectory);

        foreach (var candidate in candidates)
        {
            if (TryPrepareDirectory(candidate, out var lockDirectory))
                return new DatabaseLockPath(lockDirectory, lockNamePrefix, UsesFallbackDirectory: false);
        }

        var fallbackDirectory = Path.GetDirectoryName(databaseFullPath) ?? Environment.CurrentDirectory;
        if (!TryPrepareDirectory(fallbackDirectory, out var preparedFallbackDirectory))
            throw new IOException($"Unable to create LiteDB lock directory '{fallbackDirectory}'");

        return new DatabaseLockPath(preparedFallbackDirectory, lockNamePrefix, UsesFallbackDirectory: true);
    }

    public static string GetLockNamePrefix(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var normalizedPath = Path.GetFullPath(databasePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return "midibard-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static bool TryPrepareDirectory(string directory, out string preparedDirectory)
    {
        preparedDirectory = string.Empty;

        try
        {
            if (!Path.IsPathFullyQualified(directory))
                return false;

            Directory.CreateDirectory(directory);

            var probePath = Path.Combine(directory, $".midibard-lock-probe-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(probePath);

            preparedDirectory = Path.GetFullPath(directory);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class WineDatabaseFileLock : IDatabaseLock
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly ThreadLocal<Random> Random = new(() => new Random(Guid.NewGuid().GetHashCode()));
    private const string OwnerFileName = "owner.json";

    private readonly string _databasePath;
    private readonly string _lockDirectory;
    private readonly string _lockNamePrefix;
    private readonly string _writeLockPath;
    private readonly TimeSpan _timeout;

    public WineDatabaseFileLock(
        string databasePath,
        TimeSpan? timeout = null,
        IEnumerable<string>? candidateLockDirectories = null)
    {
        _databasePath = Path.GetFullPath(databasePath);
        var lockPath = DatabaseLockPathResolver.Resolve(_databasePath, candidateLockDirectories);
        _lockDirectory = lockPath.LockDirectory;
        _lockNamePrefix = lockPath.LockNamePrefix;
        _writeLockPath = Path.Combine(_lockDirectory, _lockNamePrefix + ".write.lockdir");
        _timeout = timeout ?? DefaultTimeout;

        if (lockPath.UsesFallbackDirectory)
        {
            LogWarning($"Using fallback LiteDB lock directory '{_lockDirectory}' for '{_databasePath}'");
        }
        else
        {
            LogDebug($"Using LiteDB lock directory '{_lockDirectory}' for '{_databasePath}' prefix={_lockNamePrefix}");
        }
    }

    public IDisposable AcquireRead(CancellationToken cancellationToken = default)
    {
        var owner = CreateOwnerInfo(DatabaseLockMode.Read);
        var startedAt = DateTimeOffset.UtcNow;
        Exception? lastError = null;
        LogDebug($"AcquireRead start prefix={_lockNamePrefix} db='{_databasePath}' root='{_lockDirectory}'");

        while (DateTimeOffset.UtcNow - startedAt < _timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(_writeLockPath))
            {
                SleepBeforeRetry();
                continue;
            }

            var lockPath = Path.Combine(
                _lockDirectory,
                $"{_lockNamePrefix}.read.{Environment.ProcessId}.{Guid.NewGuid():N}.lockdir");
            var lockCreated = false;

            try
            {
                Directory.CreateDirectory(_lockDirectory);
                if (!AtomicDirectory.TryCreate(lockPath, out var createError))
                {
                    lastError = createError ?? lastError;
                    SleepBeforeRetry();
                    continue;
                }

                lockCreated = true;
                WriteOwner(lockPath, owner);

                if (!Directory.Exists(_writeLockPath))
                {
                    LogDebug($"AcquireRead success path='{lockPath}' owner={owner}");
                    return new DirectoryLockLease(lockPath, owner.Token);
                }

                TryDeleteOwnedLock(lockPath, owner.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (lockCreated)
                    TryDeleteCreatedLock(lockPath, owner.Token);
            }

            SleepBeforeRetry();
        }

        var currentOwner = TryReadOwnerSummary(_writeLockPath);
        LogWarning($"AcquireRead timeout prefix={_lockNamePrefix} writer={currentOwner}");
        throw new TimeoutException(
            $"Timed out waiting for LiteDB read lock '{_lockNamePrefix}' in '{_lockDirectory}'. Writer owner: {currentOwner}",
            lastError);
    }

    public IDisposable AcquireWrite(CancellationToken cancellationToken = default)
    {
        var owner = CreateOwnerInfo(DatabaseLockMode.Write);
        var startedAt = DateTimeOffset.UtcNow;
        Exception? lastError = null;
        LogDebug($"AcquireWrite start prefix={_lockNamePrefix} db='{_databasePath}' root='{_lockDirectory}'");

        while (DateTimeOffset.UtcNow - startedAt < _timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lockCreated = false;

            try
            {
                Directory.CreateDirectory(_lockDirectory);
                if (!AtomicDirectory.TryCreate(_writeLockPath, out var createError))
                {
                    lastError = createError ?? lastError;
                    SleepBeforeRetry();
                    continue;
                }

                lockCreated = true;
                WriteOwner(_writeLockPath, owner);
                if (WaitForReadersToDrain(startedAt, cancellationToken))
                {
                    LogDebug($"AcquireWrite success path='{_writeLockPath}' owner={owner}");
                    return new DirectoryLockLease(_writeLockPath, owner.Token);
                }

                TryDeleteOwnedLock(_writeLockPath, owner.Token);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (lockCreated)
                    TryDeleteCreatedLock(_writeLockPath, owner.Token);
            }
            catch
            {
                if (lockCreated)
                    TryDeleteCreatedLock(_writeLockPath, owner.Token);
                throw;
            }
        }

        var currentOwner = TryReadOwnerSummary(_writeLockPath);
        var readerSummary = TryReadBlockingReaderSummary();
        LogWarning($"AcquireWrite timeout path='{_writeLockPath}' writer={currentOwner} readers={readerSummary}");
        throw new TimeoutException(
            $"Timed out waiting for LiteDB write lock '{_writeLockPath}'. Writer owner: {currentOwner}. Readers: {readerSummary}",
            lastError);
    }

    private DatabaseLockOwner CreateOwnerInfo(DatabaseLockMode mode)
    {
        return new DatabaseLockOwner
        {
            Token = Guid.NewGuid().ToString("N"),
            Mode = mode.ToString(),
            DatabasePath = _databasePath,
            LockDirectory = _lockDirectory,
            ProcessId = Environment.ProcessId,
            ProcessName = Process.GetCurrentProcess().ProcessName,
            CharacterName = TryGetCharacterName(),
            ContentId = TryGetContentId(),
            AcquiredAt = DateTimeOffset.UtcNow
        };
    }

    private bool WaitForReadersToDrain(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        while (DateTimeOffset.UtcNow - startedAt < _timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasReaderLocks())
                return true;

            SleepBeforeRetry();
        }

        return false;
    }

    private bool HasReaderLocks()
    {
        try
        {
            return Directory.Exists(_lockDirectory)
                && Directory.EnumerateDirectories(_lockDirectory, GetReadLockSearchPattern()).Any();
        }
        catch
        {
            return true;
        }
    }

    private string TryReadBlockingReaderSummary()
    {
        try
        {
            if (!Directory.Exists(_lockDirectory))
                return "none";

            var readerLocks = Directory.GetDirectories(_lockDirectory, GetReadLockSearchPattern());
            if (readerLocks.Length == 0)
                return "none";

            var owners = readerLocks
                .Take(5)
                .Select(TryReadOwnerSummary);
            return $"{readerLocks.Length} active reader(s), sample owners: {string.Join("; ", owners)}";
        }
        catch
        {
            return "unknown";
        }
    }

    private string GetReadLockSearchPattern() => _lockNamePrefix + ".read.*.lockdir";

    private static void SleepBeforeRetry()
    {
        Thread.Sleep(TimeSpan.FromMilliseconds(Random.Value!.Next(50, 151)));
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

    private static void WriteOwner(string lockPath, DatabaseLockOwner owner)
    {
        var json = JsonSerializer.Serialize(owner, DatabaseLockJson.Options);
        File.WriteAllText(GetOwnerPath(lockPath), json, Encoding.UTF8);
    }

    private static string TryReadOwnerSummary(string lockPath)
    {
        try
        {
            if (!Directory.Exists(lockPath))
                return "none";

            var ownerPath = GetOwnerPath(lockPath);
            if (!File.Exists(ownerPath))
                return "owner file missing";

            var json = File.ReadAllText(ownerPath);
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
            if (!Directory.Exists(lockPath))
                return null;

            var ownerPath = GetOwnerPath(lockPath);
            if (!File.Exists(ownerPath))
                return null;

            var json = File.ReadAllText(ownerPath);
            var owner = JsonSerializer.Deserialize<DatabaseLockOwner>(json, DatabaseLockJson.Options);
            return owner?.Token;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteOwnedLock(string lockPath, string token)
    {
        try
        {
            DeleteOwnedLock(lockPath, token);
        }
        catch
        {
        }
    }

    private static void TryDeleteCreatedLock(string lockPath, string token)
    {
        try
        {
            var ownerToken = TryReadOwnerToken(lockPath);
            if (ownerToken == null || ownerToken == token)
                Directory.Delete(lockPath, recursive: true);
        }
        catch
        {
        }
    }

    private static bool DeleteOwnedLock(string lockPath, string token)
    {
        if (TryReadOwnerToken(lockPath) != token)
            return false;

        Directory.Delete(lockPath, recursive: true);
        return true;
    }

    private static string GetOwnerPath(string lockPath) => Path.Combine(lockPath, OwnerFileName);

    private sealed class DirectoryLockLease : IDisposable
    {
        private readonly string _lockPath;
        private readonly string _token;
        private bool _disposed;

        public DirectoryLockLease(string lockPath, string token)
        {
            _lockPath = lockPath;
            _token = token;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (DeleteOwnedLock(_lockPath, _token))
                    LogDebug($"Released LiteDB lock '{_lockPath}'");
                else
                    LogWarning($"Skipped release for LiteDB lock '{_lockPath}' because owner token changed");
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
        public string Mode { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public string LockDirectory { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string CharacterName { get; set; } = string.Empty;
        public ulong ContentId { get; set; }
        public DateTimeOffset AcquiredAt { get; set; }

        public override string ToString()
        {
            return $"{Mode} {CharacterName} cid={ContentId} pid={ProcessId} process={ProcessName} lockRoot={LockDirectory} acquired={AcquiredAt:O}";
        }
    }

    private enum DatabaseLockMode
    {
        Read,
        Write
    }

    private static class DatabaseLockJson
    {
        public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true };
    }

    private static void LogDebug(string message)
    {
        try
        {
            DalamudApi.PluginLog.Debug($"[DatabaseLock] {message}");
        }
        catch
        {
        }
    }

    private static void LogWarning(string message)
    {
        try
        {
            DalamudApi.PluginLog.Warning($"[DatabaseLock] {message}");
        }
        catch
        {
        }
    }

    private static class AtomicDirectory
    {
        private const int ErrorAlreadyExists = 183;
        private const int Eexist = 17;

        public static bool TryCreate(string path, out Exception? error)
        {
            error = null;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (CreateDirectoryWindows(path, IntPtr.Zero))
                        return true;

                    var errorCode = Marshal.GetLastPInvokeError();
                    if (errorCode == ErrorAlreadyExists)
                        return false;

                    error = new IOException($"CreateDirectoryW failed for '{path}' with error {errorCode}.");
                    return false;
                }

                if (Mkdir(path, 0x1C0) == 0)
                    return true;

                var errno = Marshal.GetLastPInvokeError();
                if (errno == Eexist)
                    return false;

                error = new IOException($"mkdir failed for '{path}' with errno {errno}.");
                return false;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        [DllImport("kernel32", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryWindows(string lpPathName, IntPtr lpSecurityAttributes);

        [DllImport("libc", EntryPoint = "mkdir", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int Mkdir(string pathname, uint mode);
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
