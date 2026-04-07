using System;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Playlist;

namespace MidiBard;

public partial class PlaylistWindow
{
    private bool HasActiveFiltersOrSort =>
        _sortCol != null ||
        _filterPlayed != 0 ||
        !string.IsNullOrEmpty(_songSearch) ||
        !string.IsNullOrEmpty(_filterName) ||
        !string.IsNullOrEmpty(_filterArtist) ||
        !string.IsNullOrEmpty(_filterYear) ||
        !string.IsNullOrEmpty(_filterFilePath) ||
        !string.IsNullOrEmpty(_filterComments) ||
        !string.IsNullOrEmpty(_filterTags);

    private void ClearFiltersAndSort()
    {
        _songSearch = string.Empty;
        _filterName = string.Empty;
        _filterArtist = string.Empty;
        _filterYear = string.Empty;
        _filterFilePath = string.Empty;
        _filterComments = string.Empty;
        _filterTags = string.Empty;
        _filterPlayed = 0;
        _sortCol = null;
        _sortAsc = true;
        ApplySortPlaylistSongs();
    }

    private bool MatchesSongFilters(PlaylistSong ps)
    {
        var song = ps.Song;
        if (song == null) return false;

        if (!string.IsNullOrWhiteSpace(_songSearch) &&
            !(song.Name?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !(song.Artist?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterName) &&
            !(song.Name?.Contains(_filterName, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterArtist) &&
            !(song.Artist?.Contains(_filterArtist, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterYear))
        {
            var yearStr = song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "";
            if (!yearStr.Contains(_filterYear, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(_filterTags))
        {
            var hasTags = song.Tags.Count > 0 &&
                song.Tags.Any(t => t.Name?.Contains(_filterTags, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hasTags) return false;
        }

        if (_filterPlayed != 0)
        {
            var isPlayed = ps.IsPlayed;
            if (_filterPlayed == 1 && !isPlayed) return false;
            if (_filterPlayed == 2 && isPlayed) return false;
        }

        if (!string.IsNullOrWhiteSpace(_filterFilePath) &&
            !(song.FilePath?.Contains(_filterFilePath, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterComments) &&
            !(song.Comments?.Contains(_filterComments, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return true;
    }

    private void SearchSongs()
    {
        _songSearchIndexes.Clear();
        _songSearchIndexes.AddRange(
            PlaylistSongs
                .Select((ps, index) => new { ps, index })
                .Where(x => MatchesSongFilters(x.ps))
                .Select(x => x.index)
        );
    }

    private void SearchPlaylists()
    {
        _playlistSearchIndexes.Clear();

        if (string.IsNullOrWhiteSpace(_playlistSearch))
        {
            _playlistSearchIndexes.AddRange(Enumerable.Range(0, _playlists.Count));
            return;
        }

        _playlistSearchIndexes.AddRange(
            _playlists
                .Select((playlist, index) => new { playlist, index })
                .Where(x => x.playlist.Name.Contains(_playlistSearch, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index)
        );
    }

    private void ApplySortPlaylistSongs()
    {
        if (_sortCol == null)
        {
            SearchSongs();
            return;
        }

        if (_selectedPlaylist == null) return;

        IOrderedEnumerable<PlaylistSong> sorted = _sortCol.Value switch
        {
            SongSortColumn.Name => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Name) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Name),
            SongSortColumn.Artist => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Artist) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Artist),
            SongSortColumn.Year => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.ReleaseYear) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.ReleaseYear),
            SongSortColumn.Duration => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Duration) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Duration),
            SongSortColumn.PlayCount => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.PlayCount) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.PlayCount),
            SongSortColumn.LastPlayed => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.LastPlayedAt) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.LastPlayedAt),
            SongSortColumn.Rating => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Rating) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Rating),
            SongSortColumn.FileModified => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.FileLastModifiedAt) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.FileLastModifiedAt),
            _ => _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Id)
        };

        _selectedPlaylist.Songs = sorted.ToList();
        SearchSongs();

        // Persist the new order to the DB and notify other clients
        if (!_selectedPlaylist.IsTemp)
        {
            var playlist = _selectedPlaylist;
            _ = Task.Run(async () =>
            {
                try
                {
                    await ServiceContainer.PlaylistService.UpdateAsync(playlist);
                    Plugin.IpcProvider.LoadPlaylist(playlist.Id);
                }
                catch (Exception ex)
                {
                    DalamudApi.PluginLog.Error(ex, "[PlaylistWindow] Failed to persist playlist sort order");
                }
            });
        }
    }
}
