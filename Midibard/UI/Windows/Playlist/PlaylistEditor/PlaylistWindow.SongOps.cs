using System;
using System.Linq;
using System.Threading.Tasks;

namespace MidiBard;

public partial class PlaylistWindow
{
    private async Task DeleteSongAsync(int songId)
    {
        if (_selectedPlaylist == null) return;
        await Plugin.PlaylistManager.RemoveSongFromPlaylistAsync(_selectedPlaylist.Id, songId);
        _selectedSong = null;
        _selectedSongIndex = -1;
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }

    private async Task ReorderPlaylistSongByIdAsync(int fromSongId, int toSongId)
    {
        if (_selectedPlaylist == null) return;

        // If this is the active playback playlist, use the manager which handles DB + IPC
        if (Plugin.PlaylistManager.CurrentPlaylist?.Id == _selectedPlaylist.Id)
        {
            await Plugin.PlaylistManager.MoveSongByIdAsync(fromSongId, toSongId);
        }
        else
        {
            // Otherwise persist directly - no IPC needed (not the active playlist)
            await ServiceContainer.PlaylistSongService.ReorderSongByIdAsync(_selectedPlaylist.Id, fromSongId, toSongId);
        }

        // DnD reorder establishes manual order - clear any active column sort so the new order is visible.
        _sortCol = null;
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }

    private async Task UpdatePlaylistSongPlayedStatusAsync(int songIndex, bool isPlayed)
    {
        if (_selectedPlaylist == null)
            return;

        if (songIndex < 0 || songIndex >= _selectedPlaylist.Songs.Count)
            return;

        var playlistSong = _selectedPlaylist.Songs[songIndex];
        if (playlistSong.IsPlayed == isPlayed)
            return;

        // Preferred path: current-playlist flow handles local update + DB persist + IPC.
        if (Plugin.PlaylistManager.CurrentPlaylist?.Id == _selectedPlaylist.Id)
        {
            // Keep the table responsive even if current playlist instance differs from selected instance.
            playlistSong.IsPlayed = isPlayed;
            await Plugin.PlaylistManager.ChangeSongPlayedStatusAsync(songIndex, isPlayed);
            SearchSongs();
            return;
        }

        var previousValue = playlistSong.IsPlayed;

        // Fallback for non-current playlists: persist directly by playlist id and broadcast full reload.
        playlistSong.IsPlayed = isPlayed;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            playlistSong.IsPlayed = previousValue;
            _messageDisplay.ShowError("Failed to update played status.");
            return;
        }

        Plugin.IpcProvider.LoadPlaylist(_selectedPlaylist.Id);

        SearchSongs();
    }

    private async Task ResetPlaylistSongsPlayedStatusAsync()
    {
        if (_selectedPlaylist == null)
            return;

        if (Plugin.PlaylistManager.CurrentPlaylist?.Id == _selectedPlaylist.Id)
        {
            foreach (var song in _selectedPlaylist.Songs)
                song.IsPlayed = false;

            await Plugin.PlaylistManager.ResetAllSongsPlayedStatusAsync();

            // resets main window filter
            Plugin.Config.SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;
            SearchSongs();
            return;
        }

        var previousValues = _selectedPlaylist.Songs.Select(s => s.IsPlayed).ToArray();

        foreach (var song in _selectedPlaylist.Songs)
            song.IsPlayed = false;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            for (int i = 0; i < _selectedPlaylist.Songs.Count; i++)
                _selectedPlaylist.Songs[i].IsPlayed = previousValues[i];
            _messageDisplay.ShowError("Failed to reset played status.");
            return;
        }

        Plugin.IpcProvider.LoadPlaylist(_selectedPlaylist.Id);
        Plugin.Config.SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;
        SearchSongs();
    }

    private async Task PlaySongAsync()
    {
        if (_selectedSong == null || _selectedPlaylist == null) return;
        await Plugin.PlaylistManager.SwitchToPlaylistAsync(_selectedPlaylist.Id);
        var currentSongs = await Plugin.PlaylistManager.GetPlaylistSongsAsync(_selectedPlaylist.Id);
        var index = currentSongs.FindIndex(s => s.Id == _selectedSong.Id);
        if (index >= 0)
        {
            await Plugin.PlaylistManager.LoadPlayback(index, false);
        }
    }

    private async Task LoadPlaylistToCurrentAsync(int playlistId)
    {
        var playlist = await Plugin.PlaylistManager.LoadPlaylistToCurrentAsync(playlistId);
        if (playlist == null) return;

        _selectedPlaylist = playlist;
        _selectedSongIndex = -1;
        _selectedSong = null;
        SearchSongs();

        _messageDisplay.ShowSuccess($"Loaded playlist: {playlist.Name}");
    }

    private async Task ClearPlaylistAsync(int playlistId)
    {
        await Plugin.PlaylistManager.ClearPlaylistAsync(playlistId);
        await LoadPlaylistSongsAsync(playlistId);
        _messageDisplay.ShowSuccess("Playlist cleared!");
    }
}
