using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MidiBard.Playlist;
using MidiBard.Playlist.Helpers;
using MidiBard.Playlist.Services;

namespace MidiBard;

/// <summary>
/// Helper class for managing song import operations with progress tracking and cancellation support.
/// Can be shared between PlaylistWindow and SongsWindow.
/// </summary>
public class SongImportHelper
{
    private readonly Plugin _plugin;

    // Progress tracking
    private bool _isDialogPending;
    public int TotalCount { get; private set; }
    public int CurrentCount { get; private set; }
    public bool IsImporting { get; private set; }
    public bool IsRunning => _isDialogPending || IsImporting;
    public CancellationTokenSource? CancellationSource { get; private set; }

    // Callback for adding song to playlist (different for PlaylistWindow vs SongsWindow)
    private Func<string, TimeSpan, Task>? _addSongCallback;

    public Func<Task>? OnImportCompleted { get; set; }

    public SongImportHelper(Plugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>
    /// Cancel the current import operation.
    /// </summary>
    public void Cancel()
    {
        CancellationSource?.Cancel();
    }

    public void SetProgress(int current, int total)
    {
        TotalCount = total;
        CurrentCount = current;
        IsImporting = true;
    }

    public void StopProgress() => IsImporting = false;

    /// <summary>
    /// Start importing files with the given callback for adding songs.
    /// </summary>
    /// <param name="filePaths">List of file paths to import</param>
    /// <param name="addSongCallback">Callback to add song (filePath, duration) -> Task</param>
    public void StartImport(IEnumerable<string> filePaths, Func<string, TimeSpan, Task> addSongCallback)
    {
        var filePathList = filePaths.ToList();

        // Cancel any existing import
        CancellationSource?.Cancel();
        CancellationSource = new CancellationTokenSource();

        TotalCount = filePathList.Count;
        CurrentCount = 0;
        IsImporting = true;
        _addSongCallback = addSongCallback;

        // Start the import task
        _ = ImportFilesAsync(filePathList, CancellationSource.Token);
    }

    private async Task ImportFilesAsync(List<string> filePaths, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            foreach (var filePath in filePaths)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var handled = await TryHandleFastPathAsync(filePath);
                    if (handled) continue;

                    await HandleSlowPathAsync(filePath);
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Warning(e, $"[SongImportHelper] Error adding song: {filePath}");
                    CurrentCount++;
                }
            }
        }, cancellationToken);

        EndImport();
    }

    /// <summary>
    /// End the current import.
    /// </summary>
    public void EndImport()
    {
        IsImporting = false;
        TotalCount = 0;
        CurrentCount = 0;
        _addSongCallback = null;

        _ = OnImportCompleted?.Invoke();
    }

    // ==================== Import helpers ====================

    /// <summary>
    /// Fast path: song is already in the DB by file path.
    /// Ensures SyncId is assigned/corrected when UseSyncByFileId is active.
    /// Returns true if the song was handled and the caller should move to the next file.
    /// </summary>
    private async Task<bool> TryHandleFastPathAsync(string filePath)
    {
        var songRepository = ServiceContainer.SongRepository;
        var songService = ServiceContainer.SongService;

        var existingSong = await songRepository.GetByFilePathAsync(filePath);
        if (existingSong == null) return false;

        if (_plugin.Config.UseSyncByFileId && existingSong.SyncId == null)
            await EnsureSyncIdFastPathAsync(existingSong, filePath, songRepository, songService);

        CurrentCount++;
        if (_addSongCallback != null)
            await _addSongCallback(existingSong.FilePath, existingSong.Duration);
        return true;
    }

    private static async Task EnsureSyncIdFastPathAsync(
    Song existingSong, string filePath,
    ISongRepository songRepository, ISongService songService)
    {
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var embeddedId = SongFileOperationHelper.ExtractSyncId(baseName);

        if (embeddedId.HasValue)
        {
            var owner = await songRepository.GetBySyncIdAsync(embeddedId.Value);
            if (owner == null || owner.Id == existingSong.Id)
            {
                existingSong.SyncId = embeddedId.Value;
                await UpdateSyncFieldsOnlyAsync(existingSong, songRepository, songService);
            }
            else
            {
                var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps: false);
                await RenameWithSyncIdAsync(existingSong, filePath, newId, songRepository, songService);
            }
        }
        else
        {
            var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps: false);
            await RenameWithSyncIdAsync(existingSong, filePath, newId, songRepository, songService);
        }
    }

    /// <summary>
    /// Assigns or resolves a SyncId for a song that already exists in the DB (fast path).
    /// Handles: embedded ID free, embedded ID conflict (rename), no embedded ID (stamp).
    /// </summary>
    // private static async Task EnsureSyncIdFastPathAsync(
    //     Song existingSong, string filePath,
    //     ISongRepository songRepository, ISongService songService)
    // {
    //     var baseName = Path.GetFileNameWithoutExtension(filePath);
    //     var embeddedId = SongFileOperationHelper.ExtractSyncId(baseName);

    //     if (embeddedId.HasValue)
    //     {
    //         var owner = await songRepository.GetBySyncIdAsync(embeddedId.Value);
    //         if (owner == null || owner.Id == existingSong.Id)
    //         {
    //             // ID is free or already ours - adopt it
    //             existingSong.SyncId = embeddedId.Value;
    //             await songService.UpdateAsync(existingSong);
    //         }
    //         else
    //         {
    //             // Conflict - assign a fresh ID and rename the file
    //             var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps: false);
    //             var clean = SongFileOperationHelper.CleanNameFromSyncId(baseName);
    //             var newPath = Path.Combine(
    //                 Path.GetDirectoryName(filePath)!,
    //                 SongFileOperationHelper.BuildStampedFileName(clean, newId, Path.GetExtension(filePath)));

    //             try
    //             {
    //                 if (!filePath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
    //                 {
    //                     File.Move(filePath, newPath);
    //                     SongFileOperationHelper.RenameAssociatedFiles(filePath, newPath);
    //                 }
    //                 existingSong.FilePath = newPath;
    //             }
    //             catch (Exception ex)
    //             {
    //                 DalamudApi.PluginLog.Warning(ex, $"[SongImportHelper] Fast path rename failed: {filePath}");
    //             }

    //             existingSong.SyncId = newId;
    //             existingSong.Name = clean;
    //             await songService.UpdateAsync(existingSong);
    //         }
    //     }
    //     else
    //     {
    //         // No [ID] in name - stamp it
    //         var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps: false);
    //         var newPath = Path.Combine(
    //             Path.GetDirectoryName(filePath)!,
    //             SongFileOperationHelper.BuildStampedFileName(baseName, newId, Path.GetExtension(filePath)));

    //         try
    //         {
    //             if (!filePath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
    //             {
    //                 File.Move(filePath, newPath);
    //                 SongFileOperationHelper.RenameAssociatedFiles(filePath, newPath);
    //             }
    //             existingSong.FilePath = newPath;
    //         }
    //         catch (Exception ex)
    //         {
    //             DalamudApi.PluginLog.Warning(ex, $"[SongImportHelper] Fast path rename failed: {filePath}");
    //         }

    //         existingSong.SyncId = newId;
    //         await songService.UpdateAsync(existingSong);
    //     }
    // }

    /// <summary>
    /// Slow path: validates the MIDI file, resolves by SyncId when applicable,
    /// creates a new DB record, applies metadata, and assigns/stamps a SyncId.
    /// </summary>
    private async Task HandleSlowPathAsync(string filePath)
    {
        var midiFileService = ServiceContainer.MidiFileService;
        var songService = ServiceContainer.SongService;
        var songRepository = ServiceContainer.SongRepository;

        // Validate MIDI file
        var (isValid, errorMessage) = midiFileService.ValidateMidiFile(filePath);
        if (!isValid)
        {
            DalamudApi.PluginLog.Warning($"[SongImportHelper] Invalid MIDI file: {errorMessage} - {filePath}");
            CurrentCount++;
            return;
        }

        // Always strip the trailing [ID] stamp before extraction so that regex rules
        // work correctly regardless of whether the file already has an ID in its name
        // (CleanNameFromSyncId is a no-op when no [ID] is present).
        var rawFilename = midiFileService.ExtractSongNameFromMidi(filePath);
        var filename = SongFileOperationHelper.CleanNameFromSyncId(rawFilename);

        // When UseSyncByFileId is active and the file already carries an [ID] in its name,
        // attempt to resolve the existing DB record by SyncId BEFORE creating a new one.
        // This handles the case where a file was renamed/moved but kept its [ID].
        if (_plugin.Config.UseSyncByFileId)
        {
            var embeddedId = SongFileOperationHelper.ExtractSyncId(rawFilename);
            if (embeddedId.HasValue)
            {
                var handled = await TryResolveBySyncIdAsync(filePath, filename, embeddedId.Value, midiFileService, songService);
                if (handled) return;
            }
        }

        // Apply extraction rules to derive metadata from the clean filename
        var metadata = SongMetadataExtractor.Extract(filename, _plugin.Config.ExtractionRules);
        var duration = await midiFileService.CalculateDurationFromFileAsync(filePath);

        var song = await songService.GetOrCreateFromFileAsync(
            filePath,
            metadata.SongName ?? filename,
            metadata.Artist ?? "",
            metadata.ReleaseYear ?? 0,
            duration);

        if (song == null)
        {
            DalamudApi.PluginLog.Warning($"[SongImportHelper] Failed to create song: {filePath}");
            CurrentCount++;
            return;
        }

        // Apply extra fields not covered by GetOrCreateFromFileAsync
        await ApplyExtraMetadataAsync(song, metadata, songService);

        // Assign SyncId if the feature is active and not yet assigned
        if (_plugin.Config.UseSyncByFileId && song.SyncId == null)
            await ApplySyncIdAsync(song, filename, songRepository, songService);

        CurrentCount++;
        if (_addSongCallback != null)
            await _addSongCallback(song.FilePath, duration);
    }

    /// <summary>
    /// Attempts to resolve an import by matching the embedded SyncId to an existing DB record.
    /// Used when UseSyncByFileId is active. Returns true if the file was fully handled.
    /// </summary>
    private async Task<bool> TryResolveBySyncIdAsync(
        string filePath, string cleanFilename, int embeddedId,
        IMidiFileService midiFileService, ISongService songService)
    {
        var owner = await ServiceContainer.SongRepository.GetBySyncIdAsync(embeddedId);
        if (owner == null || owner.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            return false;

        // File was renamed/moved - update the existing record instead of creating a duplicate
        owner.FilePath = filePath;
        owner.Name = cleanFilename;
        owner.IsValid = true;
        owner.FileLastModifiedAt = File.GetLastWriteTime(filePath);
        owner.Duration = await midiFileService.CalculateDurationFromFileAsync(filePath);

        // Re-extract optional metadata from the new (clean) filename
        var meta = SongMetadataExtractor.Extract(cleanFilename, _plugin.Config.ExtractionRules);
        if (meta.SongName != null) owner.Name = meta.SongName;
        if (meta.Artist != null) owner.Artist = meta.Artist;
        if (meta.ReleaseYear.HasValue) owner.ReleaseYear = meta.ReleaseYear.Value;
        if (meta.Rating.HasValue && owner.Rating == 0) owner.Rating = meta.Rating.Value;
        if (meta.Comments != null && string.IsNullOrEmpty(owner.Comments)) owner.Comments = meta.Comments;

        await songService.UpdateAsync(owner);
        foreach (var tag in meta.Tags)
            await songService.AddTagAsync(owner.Id, tag);

        DalamudApi.PluginLog.Information($"[SongImportHelper] SyncId [{embeddedId}] path updated (renamed/moved): {filePath}");

        CurrentCount++;
        if (_addSongCallback != null)
            await _addSongCallback(owner.FilePath, owner.Duration);
        return true;
    }

    /// <summary>
    /// Applies Rating, Comments, and Tags that are not handled by GetOrCreateFromFileAsync.
    /// </summary>
    private static async Task ApplyExtraMetadataAsync(Song song, SongMetadata metadata, ISongService songService)
    {
        var needsUpdate = false;

        if (metadata.Rating.HasValue && song.Rating == 0)
        {
            song.Rating = metadata.Rating.Value;
            needsUpdate = true;
        }

        if (!string.IsNullOrEmpty(metadata.Comments) && string.IsNullOrEmpty(song.Comments))
        {
            song.Comments = metadata.Comments;
            needsUpdate = true;
        }

        if (needsUpdate)
            await songService.UpdateAsync(song);

        foreach (var tag in metadata.Tags)
            await songService.AddTagAsync(song.Id, tag);
    }

    /// <summary>
    /// Assigns a SyncId to a newly created song. Name is preserved as already set
    /// by GetOrCreateFromFileAsync (extracted metadata). Only SyncId and FilePath are changed.
    /// </summary>
    private static async Task ApplySyncIdAsync(
        Song song, string cleanFilename,
        ISongRepository songRepository, ISongService songService)
    {
        var currentPath = song.FilePath;
        var baseName = Path.GetFileNameWithoutExtension(currentPath);
        var embeddedId = SongFileOperationHelper.ExtractSyncId(baseName);

        if (embeddedId.HasValue)
        {
            var idOwner = await songRepository.GetBySyncIdAsync(embeddedId.Value);
            if (idOwner == null || idOwner.Id == song.Id)
            {
                song.SyncId = embeddedId.Value;
                await UpdateSyncFieldsOnlyAsync(song, songRepository, songService);
                return;
            }

            var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps: false);
            var cleanBase = SongFileOperationHelper.CleanNameFromSyncId(baseName);
            await RenameWithSyncIdAsync(song, currentPath, newId, songRepository, songService);
            DalamudApi.PluginLog.Debug($"[SongImportHelper] Reassigned SyncId [{newId}] (conflict) to: {song.FilePath}");
        }
        else
        {
            var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps: false);
            await RenameWithSyncIdAsync(song, currentPath, newId, songRepository, songService);
            DalamudApi.PluginLog.Debug($"[SongImportHelper] Assigned SyncId [{newId}] to: {song.FilePath}");
        }
    }

    /// <summary>
    /// Renames the physical file to include [syncId] and updates SyncId + FilePath only.
    /// Name and Tags are preserved as already set upstream.
    /// </summary>
    private static async Task RenameWithSyncIdAsync(
        Song song, string currentPath, int syncId,
        ISongRepository songRepository, ISongService songService)
    {
        song.SyncId = syncId;

        var ext = Path.GetExtension(currentPath);
        var cleanBase = SongFileOperationHelper.CleanNameFromSyncId(
            Path.GetFileNameWithoutExtension(currentPath));
        var newPath = Path.Combine(
            Path.GetDirectoryName(currentPath)!,
            SongFileOperationHelper.BuildStampedFileName(cleanBase, syncId, ext));

        try
        {
            if (!currentPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(currentPath, newPath);
                SongFileOperationHelper.RenameAssociatedFiles(currentPath, newPath);
            }
            song.FilePath = newPath;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Warning(ex, $"[SongImportHelper] Failed to rename for SyncId [{syncId}]: {currentPath}");
        }

        await UpdateSyncFieldsOnlyAsync(song, songRepository, songService);
    }

    /// <summary>
    /// Persists SyncId and FilePath without touching Name or Tags.
    /// Loads tags from DB before saving to prevent the UpdateAsync from wiping them.
    /// </summary>
    private static async Task UpdateSyncFieldsOnlyAsync(
        Song song, ISongRepository songRepository, ISongService songService)
    {
        var withTags = await songRepository.GetSongByIdAsync(song.Id);
        if (withTags != null)
            song.Tags = withTags.Tags;

        song.UpdatedAt = DateTime.UtcNow;
        await songService.UpdateAsync(song);
    }

    /// <summary>
    /// Get progress as a float between 0 and 1.
    /// </summary>
    public float GetProgressValue()
    {
        return TotalCount > 0 ? (float)CurrentCount / TotalCount : 0f;
    }

    /// <summary>
    /// Get progress text for display.
    /// </summary>
    public string GetProgressText()
    {
        var progress = GetProgressValue() * 100f;
        return $"Progress: {CurrentCount}/{TotalCount} - {progress:F1}%";
    }

    // ==================== Sync File Data Operations ====================

    // Callback for syncing song
    private Func<Playlist.Song, Task>? _syncSongCallback;

    // Callback for when sync completes
    public Action? OnSyncCompleted { get; set; }

    /// <summary>
    /// Start syncing file data for all songs.
    /// </summary>
    /// <param name="songs">List of songs to sync</param>
    /// <param name="syncCallback">Callback to sync each song (song) -> Task</param>
    public void StartSync(IEnumerable<Playlist.Song> songs, Func<Playlist.Song, Task> syncCallback)
    {
        var songList = songs.ToList();

        // Cancel any existing operation
        CancellationSource?.Cancel();
        CancellationSource = new CancellationTokenSource();

        TotalCount = songList.Count;
        CurrentCount = 0;
        IsImporting = true;
        _syncSongCallback = syncCallback;

        // Start the sync task
        _ = SyncSongsAsync(songList, CancellationSource.Token);
    }

    private async Task SyncSongsAsync(List<Playlist.Song> songs, CancellationToken cancellationToken)
    {
        // Run the heavy work in a background thread to not block the UI
        await Task.Run(async () =>
        {
            foreach (var song in songs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    // Call the sync callback if provided
                    if (_syncSongCallback != null)
                    {
                        await _syncSongCallback(song);
                    }

                    // Update progress
                    CurrentCount++;
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Warning(e, $"Error syncing song: {song.FilePath}");
                }
            }
        }, cancellationToken);

        EndSync();
    }

    /// <summary>
    /// End the current sync operation.
    /// </summary>
    public void EndSync()
    {
        IsImporting = false;
        TotalCount = 0;
        CurrentCount = 0;
        _syncSongCallback = null;

        // Call the completion callback if provided
        OnSyncCompleted?.Invoke();
    }

    // ==================== File Dialog Operations ====================

    /// <summary>
    /// Opens a file dialog and starts importing the selected MIDI files.
    /// Handles both Win32 and ImGui dialog backends automatically.
    /// </summary>
    public async Task ShowAndImportFilesAsync(
        Plugin plugin,
        Func<string, TimeSpan, Task>? onSongImported = null)
    {
        CheckAndUpdateLastOpenedFolder(plugin.Config);

        if (plugin.Config.useLegacyFileDialog)
        {
            await ShowAndImportFilesWin32Async(plugin, onSongImported);
        }
        else
        {
            await ShowAndImportFilesImGuiAsync(plugin, onSongImported);
        }
    }

    /// <summary>
    /// Opens a folder dialog and starts importing MIDI files from the selected folder.
    /// Handles both Win32 and ImGui dialog backends automatically.
    /// </summary>
    public async Task ShowAndImportFolderAsync(
        Plugin plugin,
        Func<string, TimeSpan, Task>? onSongImported = null)
    {
        CheckAndUpdateLastOpenedFolder(plugin.Config);

        if (plugin.Config.useLegacyFileDialog)
        {
            await ShowAndImportFolderWin32Async(plugin, onSongImported);
        }
        else
        {
            await ShowAndImportFolderImGuiAsync(plugin, onSongImported);
        }
    }

    private async Task ShowAndImportFilesWin32Async(
        Plugin plugin,
        Func<string, TimeSpan, Task>? onSongImported)
    {
        var tcs = new TaskCompletionSource<bool>();

        MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true && filePaths is { Length: > 0 })
            {
                StartImport(filePaths, onSongImported ?? EmptySongCallback);
                plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]) ?? plugin.Config.lastOpenedFolderPath;
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, plugin.Config.lastOpenedFolderPath);

        await tcs.Task;
    }

    private async Task ShowAndImportFilesImGuiAsync(
        Plugin plugin,
        Func<string, TimeSpan, Task>? onSongImported)
    {
        var tcs = new TaskCompletionSource<bool>();

        plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
            "Open MIDI Files",
            ".mid,.midi",
            (result, filePaths) =>
            {
                if (result == true && filePaths.Count > 0)
                {
                    StartImport(filePaths, onSongImported ?? EmptySongCallback);
                    plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]) ?? plugin.Config.lastOpenedFolderPath;
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetResult(false);
                }
            },
            0,
            plugin.Config.lastOpenedFolderPath);

        await tcs.Task;
    }

    private async Task ShowAndImportFolderWin32Async(
        Plugin plugin,
        Func<string, TimeSpan, Task>? onSongImported)
    {
        var tcs = new TaskCompletionSource<bool>();

        MidiBard.Win32.FileDialogs.FolderPicker((result, folderPath) =>
        {
            if (result == true && !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                var files = EnumerateMidiFiles(folderPath);
                StartImport(files, onSongImported ?? EmptySongCallback);
                plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, plugin.Config.lastOpenedFolderPath);

        await tcs.Task;
    }

    private async Task ShowAndImportFolderImGuiAsync(
        Plugin plugin,
        Func<string, TimeSpan, Task>? onSongImported)
    {
        var tcs = new TaskCompletionSource<bool>();

        plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Open folder",
            (result, folderPath) =>
            {
                if (result == true && Directory.Exists(folderPath))
                {
                    var files = EnumerateMidiFiles(folderPath);
                    StartImport(files, onSongImported ?? EmptySongCallback);
                    plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetResult(false);
                }
            },
            plugin.Config.lastOpenedFolderPath);

        await tcs.Task;
    }

    /// <summary>
    /// Enumerates all MIDI files (.mid, .midi) in a folder recursively.
    /// </summary>
    private static IEnumerable<string> EnumerateMidiFiles(string folderPath)
    {
        var allowedExtensions = new[] { ".mid", ".midi" };
        return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
    }

    /// <summary>
    /// Empty callback for when no song import callback is needed.
    /// </summary>
    private static Task EmptySongCallback(string filePath, TimeSpan duration)
    {
        return Task.CompletedTask;
    }

    private static void CheckAndUpdateLastOpenedFolder(Configuration config)
    {
        if (!Directory.Exists(config.lastOpenedFolderPath))
        {
            config.lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    // ==================== Simple Dialog Operations (for custom processors like PlaylistWindow) ====================

    /// <summary>
    /// Opens a file dialog and returns selected MIDI file paths (without importing).
    /// Useful for callers with custom import logic.
    /// </summary>
    public async Task<IEnumerable<string>?> GetMidiFilesFromFileDialogAsync(Plugin plugin)
    {
        _isDialogPending = true;
        try
        {
            CheckAndUpdateLastOpenedFolder(plugin.Config);

            if (plugin.Config.useLegacyFileDialog)
                return await GetMidiFilesFromFileDialogWin32Async(plugin);

            return await GetMidiFilesFromFileDialogImGuiAsync(plugin);
        }
        finally
        {
            _isDialogPending = false;
        }
    }

    /// <summary>
    /// Opens a folder dialog and returns MIDI file paths from selected folder (without importing).
    /// Useful for callers with custom import logic.
    /// </summary>
    public async Task<IEnumerable<string>?> GetMidiFilesFromFolderDialogAsync(Plugin plugin)
    {
        _isDialogPending = true;
        try
        {
            CheckAndUpdateLastOpenedFolder(plugin.Config);

            if (plugin.Config.useLegacyFileDialog)
                return await GetMidiFilesFromFolderDialogWin32Async(plugin);

            return await GetMidiFilesFromFolderDialogImGuiAsync(plugin);
        }
        finally
        {
            _isDialogPending = false;
        }
    }

    private async Task<IEnumerable<string>?> GetMidiFilesFromFileDialogWin32Async(Plugin plugin)
    {
        var tcs = new TaskCompletionSource<IEnumerable<string>?>();

        MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true && filePaths is { Length: > 0 })
            {
                plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]) ?? plugin.Config.lastOpenedFolderPath;
                tcs.TrySetResult(filePaths);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        }, plugin.Config.lastOpenedFolderPath);

        return await tcs.Task;
    }

    private async Task<IEnumerable<string>?> GetMidiFilesFromFileDialogImGuiAsync(Plugin plugin)
    {
        var tcs = new TaskCompletionSource<IEnumerable<string>?>();

        plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
            "Open MIDI Files",
            ".mid,.midi",
            (result, filePaths) =>
            {
                if (result == true && filePaths.Count > 0)
                {
                    plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]) ?? plugin.Config.lastOpenedFolderPath;
                    tcs.TrySetResult(filePaths);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            },
            0,
            plugin.Config.lastOpenedFolderPath);

        return await tcs.Task;
    }

    private async Task<IEnumerable<string>?> GetMidiFilesFromFolderDialogWin32Async(Plugin plugin)
    {
        var tcs = new TaskCompletionSource<IEnumerable<string>?>();

        MidiBard.Win32.FileDialogs.FolderPicker((result, folderPath) =>
        {
            if (result == true && !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                var files = EnumerateMidiFiles(folderPath);
                plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                tcs.TrySetResult(files);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        }, plugin.Config.lastOpenedFolderPath);

        return await tcs.Task;
    }

    private async Task<IEnumerable<string>?> GetMidiFilesFromFolderDialogImGuiAsync(Plugin plugin)
    {
        var tcs = new TaskCompletionSource<IEnumerable<string>?>();

        plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Open folder",
            (result, folderPath) =>
            {
                if (result == true && Directory.Exists(folderPath))
                {
                    var files = EnumerateMidiFiles(folderPath);
                    plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                    tcs.TrySetResult(files);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            },
            plugin.Config.lastOpenedFolderPath);

        return await tcs.Task;
    }

    /// <summary>
    /// Opens a file dialog to select a single MIDI file path for editing a song.
    /// Useful for changing the file path of an existing song.
    /// </summary>
    /// <param name="initialDirectory">Directory to open the dialog in. Defaults to lastOpenedFolderPath.</param>
    public async Task<string?> GetMidiFilePathAsync(Plugin plugin, string? initialDirectory = null)
    {
        var startDir = initialDirectory ?? plugin.Config.lastOpenedFolderPath;
        if (!Directory.Exists(startDir))
            startDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (plugin.Config.useLegacyFileDialog)
        {
            return await GetMidiFilePathWin32Async(plugin, startDir);
        }
        else
        {
            return await GetMidiFilePathImGuiAsync(plugin, startDir);
        }
    }

    private async Task<string?> GetMidiFilePathWin32Async(Plugin plugin, string startDir)
    {
        var tcs = new TaskCompletionSource<string?>();

        MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true && filePaths is { Length: > 0 })
            {
                plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]) ?? plugin.Config.lastOpenedFolderPath;
                tcs.TrySetResult(filePaths[0]);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        }, startDir);

        return await tcs.Task;
    }

    private async Task<string?> GetMidiFilePathImGuiAsync(Plugin plugin, string startDir)
    {
        var tcs = new TaskCompletionSource<string?>();

        plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
            "Open MIDI File",
            ".mid,.midi",
            (result, filePaths) =>
            {
                if (result == true && filePaths.Count > 0)
                {
                    plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]) ?? plugin.Config.lastOpenedFolderPath;
                    tcs.TrySetResult(filePaths[0]);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            },
            0,
            startDir);

        return await tcs.Task;
    }

    /// <summary>
    /// Opens a file dialog to select a legacy .mpl playlist file.
    /// Respects the useLegacyFileDialog setting.
    /// </summary>
    public async Task<string?> GetMplFilePathAsync(Plugin plugin)
    {
        CheckAndUpdateLastOpenedFolder(plugin.Config);

        if (plugin.Config.useLegacyFileDialog)
        {
            var tcs = new TaskCompletionSource<string?>();
            MidiBard.Win32.FileDialogs.OpenPlaylistDialog((result, path) =>
            {
                tcs.TrySetResult(result == true ? path : null);
            }, plugin.Config.lastOpenedFolderPath);
            return await tcs.Task;
        }
        else
        {
            var tcs = new TaskCompletionSource<string?>();
            plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Open Playlist File",
                ".mpl",
                (result, filePaths) =>
                {
                    tcs.TrySetResult(result == true && filePaths.Count > 0 ? filePaths[0] : null);
                },
                0,
                plugin.Config.lastOpenedFolderPath);
            return await tcs.Task;
        }
    }
}
