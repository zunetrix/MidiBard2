using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using MidiBard.Playlist;

namespace MidiBard;

public static class BackupService
{
    private const string BackupFilePattern = "midibard-backup-*.db";
    private const string DbFileName = "midibard.db";

    public static string GetDatabasePath(string? folder) =>
        Path.Combine(
            folder ?? DalamudApi.PluginInterface.ConfigDirectory.FullName,
            DbFileName);

    /// <summary>
    /// Creates a backup of the database file synchronously.
    /// Does NOT close the database connection - caller is responsible for ensuring the DB is not open.
    /// Returns the backup file path, or null if the database file does not exist.
    /// </summary>
    public static string? CreateBackup(string dbPath, string backupFolder, int maxBackupCount, bool skipIfRecent = false)
    {
        return ExecuteWithDatabaseLock(dbPath, () => CreateBackupUnlocked(dbPath, backupFolder, maxBackupCount, skipIfRecent));
    }

    public static void RestoreBackup(string backupPath, string dbPath)
    {
        ExecuteWithDatabaseLock(dbPath, () =>
        {
            File.Copy(backupPath, dbPath, overwrite: true);
            DalamudApi.PluginLog.Information($"[Backup] Restored: {backupPath}");
        });
    }

    public static void MoveDatabaseFiles(string currentDbPath, string newDbPath, string currentLogPath, string newLogPath)
    {
        ExecuteWithDatabaseLock(currentDbPath, () =>
        {
            if (File.Exists(currentDbPath))
                File.Move(currentDbPath, newDbPath);
            if (File.Exists(currentLogPath))
                File.Move(currentLogPath, newLogPath);
        });
    }

    private static string? CreateBackupUnlocked(string dbPath, string backupFolder, int maxBackupCount, bool skipIfRecent)
    {
        if (!File.Exists(dbPath)) return null;

        Directory.CreateDirectory(backupFolder);
        if (skipIfRecent)
        {
            var latestBackup = GetBackupFiles(backupFolder).FirstOrDefault();
            if (latestBackup != null && DateTime.UtcNow - latestBackup.LastWriteTimeUtc < TimeSpan.FromSeconds(30))
            {
                DalamudApi.PluginLog.Information($"[Backup] Skipping startup backup; recent backup already exists: {latestBackup.FullName}");
                return null;
            }
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupFolder, $"midibard-backup-{timestamp}.db");
        File.Copy(dbPath, backupPath, overwrite: false);
        DalamudApi.PluginLog.Information($"[Backup] Created: {backupPath}");

        TrimBackups(backupFolder, maxBackupCount);
        return backupPath;
    }

    /// <summary>
    /// Creates a startup backup synchronously on the calling thread.
    /// Uses a named mutex so only one MidiBard instance creates the startup backup at a time.
    /// Errors are caught and logged as warnings.
    /// </summary>
    public static void TryCreateStartupBackup(Configuration config)
    {
        var mutex = new Mutex(false, "MidiBard2_StartupBackup");
        var lockTaken = false;

        try
        {
            try
            {
                lockTaken = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException ex)
            {
                // If another instance crashed while owning the mutex, we can safely continue.
                lockTaken = true;
                DalamudApi.PluginLog.Warning(ex, "[Backup] Startup mutex was abandoned; proceeding with backup");
            }

            if (!lockTaken)
            {
                return;
            }

            var dbPath = GetDatabasePath(config.defaultPlaylistFolder);
            CreateBackup(dbPath, config.DefaultBackupFolder, config.MaxBackupCount, skipIfRecent: true);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "[Backup] Failed to create startup backup");
        }
        finally
        {
            try
            {
                if (lockTaken)
                {
                    mutex.ReleaseMutex();
                }
            }
            catch (ApplicationException ex)
            {
                DalamudApi.PluginLog.Warning(ex, "[Backup] Failed to release startup mutex");
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    public static void TrimBackups(string backupFolder, int maxBackupCount)
    {
        if (maxBackupCount <= 0) return;
        var files = GetBackupFiles(backupFolder);
        while (files.Count > maxBackupCount)
        {
            try
            {
                File.Delete(files[^1].FullName);
                DalamudApi.PluginLog.Information($"[Backup] Deleted old backup: {files[^1].Name}");
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Warning(ex, "[Backup] Failed to delete old backup");
            }
            files.RemoveAt(files.Count - 1);
        }
    }

    public static List<FileInfo> GetBackupFiles(string backupFolder)
    {
        if (!Directory.Exists(backupFolder)) return new();
        return new DirectoryInfo(backupFolder)
            .GetFiles(BackupFilePattern)
            .OrderByDescending(f => f.Name)
            .ToList();
    }

    private static T ExecuteWithDatabaseLock<T>(string dbPath, Func<T> action)
    {
        var useWineLock = Dalamud.Utility.Util.IsWine();
        using var lease = DatabaseLockFactory.Create(dbPath, useWineLock).Acquire();
        return action();
    }

    private static void ExecuteWithDatabaseLock(string dbPath, Action action)
    {
        ExecuteWithDatabaseLock(dbPath, () =>
        {
            action();
            return true;
        });
    }
}
