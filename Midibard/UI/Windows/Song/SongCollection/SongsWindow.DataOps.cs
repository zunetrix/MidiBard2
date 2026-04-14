using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Playlist;
using MidiBard.Playlist.Helpers;

namespace MidiBard;

public partial class SongsWindow
{
    private TimeSpan GetSelectedSongsDuration()
        => _songs
            .Where(s => _selectedSongIds.Contains(s.Id))
            .Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration);

    private void SyncSongsFileData()
    {
        if (_songs.Count == 0) return;

        if (Plugin.Config.UseSyncByFileId)
        {
            _syncForSelectedOnly = false;
            OpenPopup("SyncFileDataPopup");
        }
        else
        {
            ExecuteSyncFileDataDirect(_songs);
        }
    }

    private void SyncSelectedSongsFileData()
    {
        if (_selectedSongIds.Count == 0) return;

        if (Plugin.Config.UseSyncByFileId)
        {
            _syncForSelectedOnly = true;
            OpenPopup("SyncFileDataPopup");
        }
        else
        {
            var selectedSongs = _songs.Where(s => _selectedSongIds.Contains(s.Id)).ToList();
            ExecuteSyncFileDataDirect(selectedSongs);
        }
    }

    /// <summary>
    /// Direct sync without popup - used when UseSyncByFileId is OFF.
    /// </summary>
    private void ExecuteSyncFileDataDirect(List<Song> songs)
    {
        var modified = new List<Song>();

        _importHelper.OnSyncCompleted = () =>
        {
            _importHelper.OnSyncCompleted = OnSyncCompleted;
            _ = FinalizeSyncAsync(modified);
        };

        _importHelper.StartSync(songs, async song =>
        {
            if (await Plugin.PlaylistManager.ComputeSyncSongFileDataAsync(song))
                modified.Add(song);
        });
    }

    /// <summary>
    /// Sync with selectable metadata fields - used when UseSyncByFileId is ON.
    /// Called from the SyncFileDataPopup after the user selects which fields to update.
    /// </summary>
    private void ExecuteSyncFileDataWithFields()
    {
        // Build the set of optional fields to update
        var syncFields = new HashSet<ExtractionField>();
        if (_syncFieldSongName) syncFields.Add(ExtractionField.SongName);
        if (_syncFieldArtist) syncFields.Add(ExtractionField.Artist);
        if (_syncFieldReleaseYear) syncFields.Add(ExtractionField.ReleaseYear);
        if (_syncFieldRating) syncFields.Add(ExtractionField.Rating);
        if (_syncFieldComments) syncFields.Add(ExtractionField.Comments);
        if (_syncFieldTags) syncFields.Add(ExtractionField.Tags);

        var extractionRules = Plugin.Config.ExtractionRules;
        var songs = _syncForSelectedOnly
            ? _songs.Where(s => _selectedSongIds.Contains(s.Id)).ToList()
            : _songs.ToList();

        if (songs.Count == 0) return;

        var modified = new List<Song>();
        var tagsToSync = syncFields.Contains(ExtractionField.Tags);

        _importHelper.OnSyncCompleted = () =>
        {
            _importHelper.OnSyncCompleted = OnSyncCompleted;
            _ = FinalizeSyncAsync(modified);
        };

        _importHelper.StartSync(songs, async song =>
        {
            if (await Plugin.PlaylistManager.ComputeSyncSongFileDataAsync(song, syncFields, extractionRules))
                modified.Add(song);

            // Handle Tags separately (requires service calls)
            if (tagsToSync && song.IsValid)
            {
                var baseName = System.IO.Path.GetFileNameWithoutExtension(song.FilePath);
                var cleanName = SongFileOperationHelper.CleanNameFromSyncId(baseName);
                var metadata = SongMetadataExtractor.Extract(cleanName, extractionRules);

                if (metadata.Tags.Count > 0)
                {
                    foreach (var tagName in metadata.Tags)
                        await ServiceContainer.SongService.AddTagAsync(song.Id, tagName);
                }
            }
        });
    }

    private async Task FinalizeSyncAsync(List<Song> modified)
    {
        if (modified.Count > 0)
            await ServiceContainer.SongService.BulkUpdateAsync(modified);
        _ = LoadSongsAsync();
    }

    private async Task DeleteSelectedSongsAsync()
    {
        var ids = _selectedSongIds.ToList();
        foreach (var id in ids)
            await ServiceContainer.SongService.DeleteAsync(id);
        _selectedSongIds.Clear();
        await LoadSongsAsync();
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();
    }

    private async Task DeleteSongAsync(int songId)
    {
        await ServiceContainer.SongService.DeleteAsync(songId);

        await LoadSongsAsync();
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();
    }

    private async Task DeleteAllSongsAsync()
    {
        var songRepo = ServiceContainer.SongRepository;
        var playlistRepo = ServiceContainer.PlaylistRepository;

        {
            // Clear all songs from all playlists first
            await playlistRepo.ClearAllPlaylistsAsync();

            // Then delete all songs from database
            await songRepo.DeleteAllAsync();
        }

        await LoadSongsAsync();
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();
    }

    public void SelectAllSongs()
    {
        _selectedSongIds.Clear();
        _selectedSongIds.UnionWith(_searchIndexes.Select(i => _songs[i].Id));
    }

    public void ClearSongsSelection()
    {
        _selectedSongIds.Clear();
    }

    private async Task StampIdsAsync(bool fillGaps, bool renameAssociated)
    {
        if (_songs.Count == 0) return;

        // Process all valid songs, categorizing by current state
        var validSongs = _songs.Where(s => s.IsValid).ToList();
        if (validSongs.Count == 0)
        {
            _messageDisplay.ShowError("No valid songs found to stamp.");
            return;
        }

        var modified = new List<Song>();
        int stamped = 0;
        int skipped = 0;
        int failedRename = 0;

        // Track pending IDs to avoid duplicates within this batch
        var pendingIds = new HashSet<int>();

        foreach (var song in validSongs)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(song.FilePath)!;
                var ext = System.IO.Path.GetExtension(song.FilePath);
                var baseName = System.IO.Path.GetFileNameWithoutExtension(song.FilePath);

                var extractedId = SongFileOperationHelper.ExtractSyncId(baseName);

                // Category 1: Already has SyncId in DB AND matching ID in file name → skip
                if (song.SyncId.HasValue && extractedId.HasValue && extractedId.Value == song.SyncId.Value)
                {
                    skipped++;
                    continue;
                }

                // Category 2: Has ID in file name but SyncId is null in DB
                if (extractedId.HasValue && song.SyncId == null)
                {
                    // Check if the extracted ID is free in DB and not pending
                    var existingOwner = await ServiceContainer.SongRepository.GetBySyncIdAsync(extractedId.Value);
                    if (existingOwner == null && !pendingIds.Contains(extractedId.Value))
                    {
                        // Adopt existing ID from filename
                        song.SyncId = extractedId.Value;
                        song.Name = SongFileOperationHelper.CleanNameFromSyncId(baseName);
                        song.UpdatedAt = DateTime.UtcNow;
                        pendingIds.Add(extractedId.Value);
                        modified.Add(song);
                        stamped++;
                        continue;
                    }

                    // ID is in use or pending - need a new ID and rename
                    var newId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps, pendingIds);
                    pendingIds.Add(newId);

                    var cleanBase = SongFileOperationHelper.CleanNameFromSyncId(baseName);
                    var newFileName = SongFileOperationHelper.BuildStampedFileName(cleanBase, newId, ext);
                    var newFilePath = System.IO.Path.Combine(dir, newFileName);

                    if (!song.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.File.Move(song.FilePath, newFilePath);
                        if (renameAssociated)
                            SongFileOperationHelper.RenameAssociatedFiles(song.FilePath, newFilePath);
                    }

                    song.FilePath = newFilePath;
                    song.Name = cleanBase;
                    song.SyncId = newId;
                    song.UpdatedAt = DateTime.UtcNow;
                    modified.Add(song);
                    stamped++;
                    continue;
                }

                // Category 3: No ID in file name and SyncId null → assign new ID and rename
                if (!extractedId.HasValue && song.SyncId == null)
                {
                    var nextId = await SongFileOperationHelper.GetNextSyncIdAsync(fillGaps, pendingIds);
                    pendingIds.Add(nextId);

                    var cleanBase = SongFileOperationHelper.CleanNameFromSyncId(baseName);
                    var newFileName = SongFileOperationHelper.BuildStampedFileName(cleanBase, nextId, ext);
                    var newFilePath = System.IO.Path.Combine(dir, newFileName);

                    if (!song.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.File.Move(song.FilePath, newFilePath);
                        if (renameAssociated)
                            SongFileOperationHelper.RenameAssociatedFiles(song.FilePath, newFilePath);
                    }

                    song.FilePath = newFilePath;
                    song.Name = cleanBase;
                    song.SyncId = nextId;
                    song.UpdatedAt = DateTime.UtcNow;
                    modified.Add(song);
                    stamped++;
                    continue;
                }

                // Category 4: Has SyncId in DB but no ID in filename → stamp it
                if (song.SyncId.HasValue && !extractedId.HasValue)
                {
                    pendingIds.Add(song.SyncId.Value);

                    var cleanBase = SongFileOperationHelper.CleanNameFromSyncId(baseName);
                    var newFileName = SongFileOperationHelper.BuildStampedFileName(cleanBase, song.SyncId.Value, ext);
                    var newFilePath = System.IO.Path.Combine(dir, newFileName);

                    if (!song.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.File.Move(song.FilePath, newFilePath);
                        if (renameAssociated)
                            SongFileOperationHelper.RenameAssociatedFiles(song.FilePath, newFilePath);
                    }

                    song.FilePath = newFilePath;
                    song.Name = cleanBase;
                    song.UpdatedAt = DateTime.UtcNow;
                    modified.Add(song);
                    stamped++;
                    continue;
                }

                // Category 5: SyncId in DB but mismatching ID in filename → rename to match DB
                if (song.SyncId.HasValue && extractedId.HasValue && extractedId.Value != song.SyncId.Value)
                {
                    pendingIds.Add(song.SyncId.Value);

                    var cleanBase = SongFileOperationHelper.CleanNameFromSyncId(baseName);
                    var newFileName = SongFileOperationHelper.BuildStampedFileName(cleanBase, song.SyncId.Value, ext);
                    var newFilePath = System.IO.Path.Combine(dir, newFileName);

                    if (!song.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.File.Move(song.FilePath, newFilePath);
                        if (renameAssociated)
                            SongFileOperationHelper.RenameAssociatedFiles(song.FilePath, newFilePath);
                    }

                    song.FilePath = newFilePath;
                    song.Name = cleanBase;
                    song.UpdatedAt = DateTime.UtcNow;
                    modified.Add(song);
                    stamped++;
                    continue;
                }

                skipped++;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, $"[SongsWindow] Failed to stamp ID for song {song.Id}: {song.FilePath}");
                failedRename++;
            }
        }

        if (modified.Count > 0)
            await ServiceContainer.SongService.BulkUpdateAsync(modified);

        await LoadSongsAsync();

        var msg = $"Stamped {stamped} song(s).";
        if (skipped > 0) msg += $" Skipped {skipped} (already stamped).";
        if (failedRename > 0) msg += $" {failedRename} failed (check logs).";

        if (failedRename > 0)
            _messageDisplay.ShowError(msg);
        else
            _messageDisplay.ShowSuccess(msg);
    }
}
