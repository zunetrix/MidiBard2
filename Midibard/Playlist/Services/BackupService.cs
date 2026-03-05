using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MidiBard;

public static class BackupService
{
    private const string BackupFilePattern = "midibard-backup-*.db";
    private const string DbFileName = "midibard.db";

    // Held for the process lifetime — prevents other MidiBard instances from duplicating the startup backup.
    private static Mutex? _startupMutex;

    public static string GetDatabasePath(string? folder) =>
        Path.Combine(
            folder ?? DalamudApi.PluginInterface.ConfigDirectory.FullName,
            DbFileName);

    /// <summary>
    /// Creates a backup of the database file synchronously.
    /// Does NOT close the database connection — caller is responsible for ensuring the DB is not open.
    /// Returns the backup file path, or null if the database file does not exist.
    /// </summary>
    public static string? CreateBackup(string dbPath, string backupFolder, int maxBackupCount)
    {
        if (!File.Exists(dbPath)) return null;

        Directory.CreateDirectory(backupFolder);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupFolder, $"midibard-backup-{timestamp}.db");
        File.Copy(dbPath, backupPath, overwrite: false);
        DalamudApi.PluginLog.Information($"[Backup] Created: {backupPath}");

        TrimBackups(backupFolder, maxBackupCount);
        return backupPath;
    }

    /// <summary>
    /// Creates a startup backup synchronously on the calling thread.
    /// Uses a named mutex so only the first MidiBard instance per session creates the backup.
    /// Errors are caught and logged as warnings.
    /// </summary>
    public static void TryCreateStartupBackup(Configuration config)
    {
        var mutex = new Mutex(false, "MidiBard2_StartupBackup");
        if (!mutex.WaitOne(0))
        {
            mutex.Dispose();
            return; // Another instance already claimed the startup backup slot
        }

        _startupMutex = mutex; // keep held for process lifetime

        try
        {
            var dbPath = GetDatabasePath(config.defaultPlaylistFolder);
            CreateBackup(dbPath, config.DefaultBackupFolder, config.MaxBackupCount);
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, "[Backup] Failed to create startup backup");
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
}
