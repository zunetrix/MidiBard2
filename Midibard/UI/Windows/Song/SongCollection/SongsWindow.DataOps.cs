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
        _selectedSongIds.UnionWith(_songs.Select(s => s.Id));
    }

    public void ClearSongsSelection()
    {
        _selectedSongIds.Clear();
    }
}
