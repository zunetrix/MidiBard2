using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Playlist;

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

        var modified = new List<Song>();

        _importHelper.OnSyncCompleted = () =>
        {
            _importHelper.OnSyncCompleted = OnSyncCompleted; // restore default handler
            _ = FinalizeSyncAsync(modified);
        };

        _importHelper.StartSync(_songs, async song =>
        {
            if (await Plugin.PlaylistManager.ComputeSyncSongFileDataAsync(song))
                modified.Add(song);
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

    private void SyncSelectedSongsFileData()
    {
        if (_selectedSongIds.Count == 0) return;
        var selectedSongs = _songs.Where(s => _selectedSongIds.Contains(s.Id)).ToList();
        var modified = new List<Song>();

        _importHelper.OnSyncCompleted = () =>
        {
            _importHelper.OnSyncCompleted = OnSyncCompleted;
            _ = FinalizeSyncAsync(modified);
        };

        _importHelper.StartSync(selectedSongs, async song =>
        {
            if (await Plugin.PlaylistManager.ComputeSyncSongFileDataAsync(song))
                modified.Add(song);
        });
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

    private async Task StampIdsAsync(bool fillGaps)
    {
        if (_songs.Count == 0) return;

        var songsToStamp = _songs.Where(s => s.SyncId == null && s.IsValid).ToList();
        if (songsToStamp.Count == 0)
        {
            _messageDisplay.ShowError("No unstamped songs found (only valid songs can be stamped).");
            return;
        }

        var modified = new List<Song>();
        int stamped = 0;
        int failedRename = 0;

        foreach (var song in songsToStamp)
        {
            try
            {
                var nextId = await Playlist.Helpers.SongFileOperationHelper.GetNextSyncIdAsync(fillGaps);

                // Build the new file name: "original name [N].ext"
                var dir = System.IO.Path.GetDirectoryName(song.FilePath)!;
                var ext = System.IO.Path.GetExtension(song.FilePath);
                var baseName = System.IO.Path.GetFileNameWithoutExtension(song.FilePath);
                var newFileName = $"{baseName} [{nextId}]{ext}";
                var newFilePath = System.IO.Path.Combine(dir, newFileName);

                // Rename file on disk
                System.IO.File.Move(song.FilePath, newFilePath);

                // Update song record
                song.FilePath = newFilePath;
                song.SyncId = nextId;
                song.UpdatedAt = DateTime.UtcNow;
                modified.Add(song);
                stamped++;
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

        if (failedRename > 0)
            _messageDisplay.ShowError($"Stamped {stamped} song(s). {failedRename} rename(s) failed (check logs).");
        else
            _messageDisplay.ShowSuccess($"Stamped {stamped} song(s) with SyncIds.");
    }
}
