using System;
using System.Linq;

using MidiBard.Playlist;

namespace MidiBard;

public partial class SongsWindow
{
    private bool MatchesFilters(Song song)
    {
        if (!string.IsNullOrWhiteSpace(_search) &&
            !(song.Name?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !(song.Artist?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !(song.FilePath?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false))
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

        if (!string.IsNullOrWhiteSpace(_filterFilePath) &&
            !(song.FilePath?.Contains(_filterFilePath, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterComments) &&
            !(song.Comments?.Contains(_filterComments, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterTags) &&
            !song.Tags.Any(t => t.Name?.Contains(_filterTags, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return true;
    }

    private void Search()
    {
        _searchIndexes.Clear();
        _searchIndexes.AddRange(
            _songs
                .Select((song, index) => new { song, index })
                .Where(x => MatchesFilters(x.song))
                .Select(x => x.index)
        );
        _isGlobalSongsCheckboxChecked = _searchIndexes.Count > 0
            && _searchIndexes.All(i => _selectedSongIds.Contains(_songs[i].Id));
    }

    private void ApplySortSongs()
    {
        if (_sortCol == null)
        {
            Search();
            return;
        }

        IOrderedEnumerable<Song> sorted = _sortCol.Value switch
        {
            SongSortColumn.Name => _sortAsc ? _songs.OrderBy(s => s.Name) : _songs.OrderByDescending(s => s.Name),
            SongSortColumn.Artist => _sortAsc ? _songs.OrderBy(s => s.Artist) : _songs.OrderByDescending(s => s.Artist),
            SongSortColumn.Year => _sortAsc ? _songs.OrderBy(s => s.ReleaseYear) : _songs.OrderByDescending(s => s.ReleaseYear),
            SongSortColumn.Duration => _sortAsc ? _songs.OrderBy(s => s.Duration) : _songs.OrderByDescending(s => s.Duration),
            SongSortColumn.PlayCount => _sortAsc ? _songs.OrderBy(s => s.PlayCount) : _songs.OrderByDescending(s => s.PlayCount),
            SongSortColumn.LastPlayed => _sortAsc ? _songs.OrderBy(s => s.LastPlayedAt) : _songs.OrderByDescending(s => s.LastPlayedAt),
            SongSortColumn.Rating => _sortAsc ? _songs.OrderBy(s => s.Rating) : _songs.OrderByDescending(s => s.Rating),
            SongSortColumn.FileModified => _sortAsc ? _songs.OrderBy(s => s.FileLastModifiedAt) : _songs.OrderByDescending(s => s.FileLastModifiedAt),
            SongSortColumn.IsValid => _sortAsc ? _songs.OrderBy(s => s.IsValid) : _songs.OrderByDescending(s => s.IsValid),
            _ => _songs.OrderBy(s => s.Id)
        };

        _songs = sorted.ToList();
        Search();
    }
}
