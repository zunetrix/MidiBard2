using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MidiBard.Playlist.Helpers;

namespace MidiBard;

/// <summary>
/// Helper class for managing song import operations with progress tracking and cancellation support.
/// Can be shared between PlaylistWindow and SongsWindow.
/// </summary>
public class SongImportHelper
{
    private readonly Plugin _plugin;

    // Progress tracking
    public int TotalCount { get; private set; }
    public int CurrentCount { get; private set; }
    public bool IsImporting { get; private set; }
    public CancellationTokenSource? CancellationSource { get; private set; }

    // Callback for adding song to playlist (different for PlaylistWindow vs SongsWindow)
    private Func<string, TimeSpan, Task>? _addSongCallback;

    // Callback for when import completes
    public Action? OnImportCompleted { get; set; }

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
        var midiFileService = ServiceContainer.MidiFileService;
        var songService = ServiceContainer.SongService;
        var songRepository = ServiceContainer.SongRepository;

        // Run the heavy work in a background thread to not block the UI
        await Task.Run(async () =>
        {
            foreach (var filePath in filePaths)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    // Fast path: song already in DB — skip all file I/O and metadata extraction
                    var existingSong = await songRepository.GetByFilePathAsync(filePath);
                    if (existingSong != null)
                    {
                        CurrentCount++;
                        if (_addSongCallback != null)
                            await _addSongCallback(filePath, existingSong.Duration);
                        continue;
                    }

                    // Slow path: new song — full pipeline (validate, parse, extract, persist)

                    // Validate the MIDI file first
                    var (isValid, errorMessage) = midiFileService.ValidateMidiFile(filePath);
                    if (!isValid)
                    {
                        DalamudApi.PluginLog.Warning($"[SongImportHelper] Invalid MIDI file: {errorMessage} - {filePath}");
                        CurrentCount++;
                        continue;
                    }

                    // Extract song name from file
                    var filename = midiFileService.ExtractSongNameFromMidi(filePath);

                    // Apply user-defined extraction rules to derive metadata from filename
                    var metadata = SongMetadataExtractor.Extract(filename, _plugin.Config.ExtractionRules);

                    // Calculate duration
                    var duration = await midiFileService.CalculateDurationFromFileAsync(filePath);

                    // Create song via service (which handles repository persistence)
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
                        continue;
                    }

                    // Apply fields not handled by GetOrCreateFromFileAsync
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

                    // Update progress on UI thread
                    CurrentCount++;

                    // Call the callback to add to playlist (if provided) - invoke on UI thread
                    if (_addSongCallback != null)
                    {
                        await _addSongCallback(filePath, duration);
                    }
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

        // Call the completion callback if provided
        OnImportCompleted?.Invoke();
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
        CheckAndUpdateLastOpenedFolder(plugin.Config);

        if (plugin.Config.useLegacyFileDialog)
        {
            return await GetMidiFilesFromFileDialogWin32Async(plugin);
        }
        else
        {
            return await GetMidiFilesFromFileDialogImGuiAsync(plugin);
        }
    }

    /// <summary>
    /// Opens a folder dialog and returns MIDI file paths from selected folder (without importing).
    /// Useful for callers with custom import logic.
    /// </summary>
    public async Task<IEnumerable<string>?> GetMidiFilesFromFolderDialogAsync(Plugin plugin)
    {
        CheckAndUpdateLastOpenedFolder(plugin.Config);

        if (plugin.Config.useLegacyFileDialog)
        {
            return await GetMidiFilesFromFolderDialogWin32Async(plugin);
        }
        else
        {
            return await GetMidiFilesFromFolderDialogImGuiAsync(plugin);
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
}
