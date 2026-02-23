using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MidiBard.Extensions.DryWetMidi;
using MidiBard.Playlist;

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
        var songRepo = ServiceContainer.TryGet<ISongRepository>();

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
                    var duration = TimeSpan.Zero;
                    var midiFile = _plugin.PlaylistManager?.LoadSongFile(filePath);
                    if (midiFile != null)
                    {
                        duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    }

                    // Create song in database - hasValidFilePath is true since we're importing a valid file
                    if (songRepo != null)
                    {
                        await songRepo.CreateOrGetSongAsync(
                            filePath,
                            Path.GetFileNameWithoutExtension(filePath),
                            "", 0, duration, true);
                    }

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
                    DalamudApi.PluginLog.Warning(e, $"Error adding song: {filePath}");
                }
            }
        });

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
        return $"Importing: {CurrentCount}/{TotalCount} - {progress:F1}%";
    }

    // ==================== Sync File Data Operations ====================

    // Callback for syncing song
    private Func<Playlist.Song, Task>? _syncSongCallback;

    // Callback for when sync completes
    public Action? OnSyncCompleted { get; set; }

    /// <summary>
    /// Get sync progress text for display.
    /// </summary>
    public string GetSyncProgressText()
    {
        var progress = GetProgressValue() * 100f;
        return $"Syncing: {CurrentCount}/{TotalCount} - {progress:F1}%";
    }

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
        });

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
}
