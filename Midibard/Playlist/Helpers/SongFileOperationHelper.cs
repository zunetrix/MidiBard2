using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Core;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Playlist.Helpers;

/// <summary>
/// Manages file I/O operations: song import, file validation, metadata syncing, and duration calculation.
/// Encapsulates all MIDI file handling and file system interactions.
/// </summary>
internal class SongFileOperationHelper
{
    private readonly CurrentSongController _songController;

    // Callback for removing song if file no longer exists
    private readonly Func<int, Task> _onRemoveSongCallback;

    public SongFileOperationHelper(
        CurrentSongController songController,
        Func<int, Task> onRemoveSongCallback)
    {
        _songController = songController;
        _onRemoveSongCallback = onRemoveSongCallback;
    }

    /// <summary>
    /// Import songs from file paths into current playlist.
    /// When UseSyncByFileId is enabled, files with an embedded [SyncId] are matched
    /// to existing DB records by SyncId rather than creating duplicate entries.
    /// </summary>
    public async Task AddSongsAsync(Playlist? currentPlaylist, IEnumerable<string> filePaths, bool useSyncByFileId = false)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[SongFileOperationHelper] Cannot add songs - current playlist is invalid");
            return;
        }

        var successfulSongs = new List<Song>();
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            foreach (var (file, path) in CheckValidFiles(filePaths))
            {
                try
                {
                    var songLength = file.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    Song? song = null;

                    if (useSyncByFileId)
                    {
                        var embeddedSyncId = ExtractSyncId(fileName);

                        if (embeddedSyncId.HasValue)
                        {
                            // File already has [N] in name — try to match by SyncId
                            var existingBySyncId = await ServiceContainer.SongRepository.GetBySyncIdAsync(embeddedSyncId.Value);

                            if (existingBySyncId != null)
                            {
                                // SyncId already claimed by a different file path?
                                bool isSameFile = existingBySyncId.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase);
                                if (!isSameFile)
                                {
                                    // Update the file path (the file was renamed/moved)
                                    existingBySyncId.FilePath = path;
                                    existingBySyncId.Name = CleanNameFromSyncId(fileName);
                                    existingBySyncId.IsValid = true;
                                    existingBySyncId.FileLastModifiedAt = File.GetLastWriteTime(path);
                                    existingBySyncId.Duration = songLength;
                                    existingBySyncId.UpdatedAt = DateTime.UtcNow;
                                    await ServiceContainer.SongRepository.UpdateAsync(existingBySyncId);
                                    DalamudApi.PluginLog.Information($"[SongFileOperationHelper] SyncId [{embeddedSyncId}] updated FilePath to: {path}");
                                }
                                song = existingBySyncId;
                            }
                            else
                            {
                                // Check if another song in current import batch already claims this SyncId
                                bool duplicateInBatch = successfulSongs.Any(s => s.SyncId == embeddedSyncId.Value);
                                if (duplicateInBatch)
                                {
                                    DalamudApi.PluginLog.Warning($"[SongFileOperationHelper] Duplicate SyncId [{embeddedSyncId}] in import batch for file: {path}. Marking invalid.");
                                    song = await ServiceContainer.SongService.GetOrCreateFromFileAsync(path, fileName, "", 0, songLength);
                                    if (song != null)
                                    {
                                        song.IsValid = false;
                                        song.SyncId = null;
                                        await ServiceContainer.SongRepository.UpdateAsync(song);
                                    }
                                }
                                else
                                {
                                    // Free SyncId — create/get the song and claim it
                                    song = await ServiceContainer.SongService.GetOrCreateFromFileAsync(path, CleanNameFromSyncId(fileName), "", 0, songLength);
                                    if (song != null && song.SyncId != embeddedSyncId.Value)
                                    {
                                        song.SyncId = embeddedSyncId.Value;
                                        song.Name = CleanNameFromSyncId(fileName);
                                        await ServiceContainer.SongRepository.UpdateAsync(song);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // No [N] in name — create normally, assign next SyncId
                            song = await ServiceContainer.SongService.GetOrCreateFromFileAsync(path, fileName, "", 0, songLength);
                            if (song != null && song.SyncId == null)
                            {
                                song.SyncId = await GetNextSyncIdAsync(fillGaps: false);
                                await ServiceContainer.SongRepository.UpdateAsync(song);
                                DalamudApi.PluginLog.Debug($"[SongFileOperationHelper] Assigned SyncId [{song.SyncId}] to: {fileName}");
                            }
                        }
                    }
                    else
                    {
                        // Standard import (SyncId feature disabled)
                        song = await ServiceContainer.SongService.GetOrCreateFromFileAsync(path, fileName, "", 0, songLength);
                    }

                    if (song == null)
                    {
                        DalamudApi.PluginLog.Warning($"[SongFileOperationHelper] Failed to create/get song from file {path}");
                        continue;
                    }

                    successfulSongs.Add(song);
                }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Warning(ex, "[SongFileOperationHelper] Error when adding song");
                }
            }

            // 2. Bulk add all songs to playlist in one DB operation
            if (successfulSongs.Count > 0)
            {
                var addSuccess = await ServiceContainer.PlaylistSongService.BulkAddSongsAsync(currentPlaylist.Id, successfulSongs.Select(s => s.Id));
                if (addSuccess)
                {
                    foreach (var song in successfulSongs)
                    {
                        currentPlaylist.AddSong(new PlaylistSong
                        {
                            Song = song,
                            IsPlayed = false,
                            AddedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    DalamudApi.PluginLog.Warning("[SongFileOperationHelper] Bulk add to playlist failed");
                    successfulSongs.Clear();
                }
            }

            // 3. Calculate durations in parallel for songs that don't have them, then persist
            var songsToCalc = currentPlaylist.Songs
                .Where(ps => ps.Song != null && ps.Song.Duration == default)
                .Select(ps => ps.Song!)
                .ToList();

            if (songsToCalc.Count > 0)
            {
                await ServiceContainer.MidiFileService.CalculateAllDurationsAsync(songsToCalc);
                foreach (var s in songsToCalc)
                {
                    await ServiceContainer.SongService.UpdateAsync(s);
                }
            }
        });

        DalamudApi.PluginLog.Information($"[SongFileOperationHelper] File import complete in {sw.Elapsed.TotalMilliseconds} ms! success: {successfulSongs.Count}");
    }

    /// <summary>
    /// Sync song file metadata: validate file existence, update modification date, recalculate duration.
    /// </summary>
    public async Task SyncSongFileDataAsync(Song song)
    {
        if (song == null) return;

        if (await ComputeSyncFileDataAsync(song))
            await ServiceContainer.SongService.UpdateAsync(song);
    }

    /// <summary>
    /// Computes updated file data (IsValid, FileLastModifiedAt, Duration) for a song in-memory.
    /// Does NOT persist to DB. Returns true if any field changed.
    /// When useSyncByFileId is true and the file is missing, attempts to find the file
    /// by scanning the original scan root for a file with the matching [SyncId] in its name.
    /// </summary>
    public async Task<bool> ComputeSyncFileDataAsync(Song song, bool useSyncByFileId = false)
    {
        if (song == null) return false;

        bool updated = false;

        // Validate file path
        var hasValidFilePath = !string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath);

        // If file missing and SyncId mode enabled, try to recover the path
        if (!hasValidFilePath && useSyncByFileId && song.SyncId.HasValue)
        {
            var recoveredPath = await TryLocateFileBySyncIdAsync(song.FilePath, song.SyncId.Value);
            if (recoveredPath != null)
            {
                DalamudApi.PluginLog.Information($"[SongFileOperationHelper] SyncId [{song.SyncId}] recovered path: {recoveredPath}");
                song.FilePath = recoveredPath;
                song.Name = CleanNameFromSyncId(Path.GetFileNameWithoutExtension(recoveredPath));
                hasValidFilePath = true;
                updated = true;
            }
        }

        if (song.IsValid != hasValidFilePath)
        {
            song.IsValid = hasValidFilePath;
            updated = true;
        }

        // Update file last modified date if file exists
        if (hasValidFilePath)
        {
            try
            {
                var fileLastModified = File.GetLastWriteTime(song.FilePath);
                if (song.FileLastModifiedAt != fileLastModified)
                {
                    song.FileLastModifiedAt = fileLastModified;
                    updated = true;
                }
            }
            catch
            {
                // Ignore errors getting file date
            }
        }

        // Recalculate duration if file exists
        if (hasValidFilePath)
        {
            try
            {
                var midiFile = ServiceContainer.MidiFileService.LoadMidiFile(song.FilePath);
                if (midiFile != null)
                {
                    var newDuration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    if (song.Duration != newDuration)
                    {
                        song.Duration = newDuration;
                        updated = true;
                    }
                }
            }
            catch
            {
                // If we can't read the file, mark as invalid
                if (song.IsValid)
                {
                    song.IsValid = false;
                    updated = true;
                }
            }
        }

        return updated;
    }

    /// <summary>
    /// Calculate duration for a song at given index in current playlist.
    /// Removes song if file no longer exists.
    /// </summary>
    public async Task CalculateSongDurationAsync(Playlist? currentPlaylist, int songIndex)
    {
        if (currentPlaylist == null || !currentPlaylist.IsValidSongIndex(songIndex))
            return;

        try
        {
            var ps = currentPlaylist.Songs[songIndex];
            var song = ps.Song;
            if (song == null) return;

            if (!File.Exists(song.FilePath))
            {
                await _onRemoveSongCallback(songIndex);
                ImGuiUtil.AddNotification(NotificationType.Warning, $"The song file no longer exists and has been removed from the playlist");
                return;
            }

            var midiFile = ServiceContainer.MidiFileService.LoadMidiFile(song.FilePath);
            if (midiFile != null)
            {
                song.Duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                _ = ServiceContainer.SongService.UpdateAsync(song);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "[SongFileOperationHelper] Error when getting song duration");
        }
    }

    // ==================== Helper Methods ====================

    // Regex: matches the last [N] bracket before the file extension.
    // Examples: "my song [42].mid" → 42 | "live [show] [7].mid" → 7
    private static readonly Regex SyncIdRegex = new(@"\[(\d+)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extract the embedded SyncId from a file name (without extension).
    /// Returns null if no [N] pattern is found.
    /// </summary>
    public static int? ExtractSyncId(string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension)) return null;
        var match = SyncIdRegex.Match(fileNameWithoutExtension);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Remove the trailing [N] stamp from a display name, e.g. "my song [42]" → "my song".
    /// </summary>
    public static string CleanNameFromSyncId(string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension)) return fileNameWithoutExtension;
        return SyncIdRegex.Replace(fileNameWithoutExtension, "").TrimEnd();
    }

    /// <summary>
    /// Derive the scan root from an existing file path by walking up until the parent
    /// is the drive root (e.g. "C:\"). This yields the top-level folder under the drive
    /// without any config.
    /// Example: "C:\music\folder\sub\file.mid" → "C:\music"
    /// </summary>
    public static string DeriveScanRoot(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;

        var driveRoot = Path.GetPathRoot(filePath);
        if (string.IsNullOrEmpty(driveRoot)) return string.Empty;

        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return driveRoot;

        while (true)
        {
            var parent = Path.GetDirectoryName(dir);
            // If parent is null or equals the drive root, 'dir' is the first-level folder
            if (parent == null || string.Equals(parent, driveRoot, StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = parent;
        }
    }

    /// <summary>
    /// Get the next SyncId to assign:
    /// - fillGaps=false: MAX(existing SyncIds) + 1
    /// - fillGaps=true: find the smallest gap first, then continue after max
    /// </summary>
    public static async Task<int> GetNextSyncIdAsync(bool fillGaps, HashSet<int>? pendingIds = null)
    {
        if (!fillGaps)
        {
            var max = await ServiceContainer.SongRepository.GetMaxSyncIdAsync();
            if (pendingIds != null && pendingIds.Count > 0)
                max = Math.Max(max, pendingIds.Max());
            return max + 1;
        }

        var all = await ServiceContainer.SongRepository.GetAllSyncIdsAsync();
        if (pendingIds != null)
            all.AddRange(pendingIds);

        var sortedIds = all.Distinct().OrderBy(id => id).ToList();
        if (sortedIds.Count == 0) return 1;

        int expected = 1;
        foreach (var id in sortedIds)
        {
            if (id > expected) break; // found a gap
            expected = id + 1;
        }
        return expected;
    }

    /// <summary>
    /// Scan the derived root folder of the original file path recursively,
    /// looking for a .mid/.midi file with [syncId] embedded in the name.
    /// Returns the found path, or null if 0 or multiple matches.
    /// </summary>
    private static Task<string?> TryLocateFileBySyncIdAsync(string originalFilePath, int syncId)
    {
        return Task.Run<string?>(() =>
        {
            try
            {
                var scanRoot = DeriveScanRoot(originalFilePath);
                if (string.IsNullOrEmpty(scanRoot) || !Directory.Exists(scanRoot))
                    return null;

                var stamp = $"[{syncId}]";
                var matches = Directory
                    .EnumerateFiles(scanRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f);
                        if (!ext.Equals(".mid", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".midi", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var nameNoExt = Path.GetFileNameWithoutExtension(f);
                        var extracted = ExtractSyncId(nameNoExt);
                        return extracted.HasValue && extracted.Value == syncId;
                    })
                    .Take(2) // short-circuit after finding 2 (duplicate detection)
                    .ToList();

                if (matches.Count == 1)
                    return matches[0];

                if (matches.Count > 1)
                    DalamudApi.PluginLog.Warning($"[SongFileOperationHelper] Duplicate SyncId [{syncId}] found in scan root '{scanRoot}': {string.Join(", ", matches)}");

                return null;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[SongFileOperationHelper] Error scanning for SyncId [{syncId}]");
                return null;
            }
        });
    }

    /// <summary>
    /// Validate file paths and yield only valid MIDI files.
    /// </summary>
    private IEnumerable<(MidiFile, string)> CheckValidFiles(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            if (!Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase))
                continue;

            var file = ServiceContainer.MidiFileService.LoadMidiFile(path);
            if (file is not null)
                yield return (file, path);
        }
    }
}
