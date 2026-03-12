using System;
using System.Linq;

using MidiBard.Playlist;

namespace MidiBard;

public partial class PlaylistWindow
{
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
    }
}
