using System;
using System.Collections.Generic;
using System.Linq;

using MidiBard.Playlist;

using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Control.MidiControl;

/// <summary>
/// Maintains one in-memory shuffle session for the active playlist.
/// A cycle is a Fisher-Yates permutation, so every playlist entry is selected
/// once before the next cycle begins.
/// </summary>
internal sealed class PlaylistShuffleSession
{
    private readonly object _gate = new();
    private readonly Random _random;

    private PlaylistModel? _playlist;
    private List<PlaylistSong> _playlistSnapshot = new();
    private List<PlaylistSong> _remaining = new();
    private readonly List<PlaylistSong> _history = new();
    private int _historyIndex = -1;

    public PlaylistShuffleSession(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public int NextIndex(PlaylistModel? playlist, int currentIndex)
    {
        lock (_gate)
        {
            if (playlist == null || playlist.Songs.Count == 0)
                return currentIndex;

            var current = SongAt(playlist, currentIndex);
            EnsureSession(playlist, current);

            if (_historyIndex + 1 < _history.Count)
            {
                _historyIndex++;
                return IndexOf(playlist, _history[_historyIndex], currentIndex);
            }

            if (_remaining.Count == 0)
                StartNewCycle(playlist, current);

            var nextIndex = _remaining.Count - 1;
            var next = _remaining[nextIndex];
            _remaining.RemoveAt(nextIndex);
            _history.Add(next);
            _historyIndex = _history.Count - 1;

            return IndexOf(playlist, next, currentIndex);
        }
    }

    public int PreviousIndex(PlaylistModel? playlist, int currentIndex)
    {
        lock (_gate)
        {
            if (playlist == null || playlist.Songs.Count == 0)
                return currentIndex;

            var current = SongAt(playlist, currentIndex);
            EnsureSession(playlist, current);

            if (_historyIndex <= 0)
                return currentIndex;

            _historyIndex--;
            return IndexOf(playlist, _history[_historyIndex], currentIndex);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _playlist = null;
            _playlistSnapshot.Clear();
            _remaining.Clear();
            _history.Clear();
            _historyIndex = -1;
        }
    }

    private void EnsureSession(PlaylistModel playlist, PlaylistSong? current)
    {
        if (!ReferenceEquals(_playlist, playlist) ||
            !MatchesSnapshot(playlist) ||
            !CurrentMatchesHistory(current))
        {
            ResetForPlaylist(playlist, current);
        }
    }

    private bool MatchesSnapshot(PlaylistModel playlist)
    {
        if (_playlistSnapshot.Count != playlist.Songs.Count)
            return false;

        for (var i = 0; i < _playlistSnapshot.Count; i++)
        {
            if (!ReferenceEquals(_playlistSnapshot[i], playlist.Songs[i]))
                return false;
        }

        return true;
    }

    private bool CurrentMatchesHistory(PlaylistSong? current)
    {
        if (current == null)
            return _historyIndex < 0;

        return _historyIndex >= 0 &&
            _historyIndex < _history.Count &&
            ReferenceEquals(_history[_historyIndex], current);
    }

    private void ResetForPlaylist(PlaylistModel playlist, PlaylistSong? current)
    {
        _playlist = playlist;
        _playlistSnapshot = playlist.Songs.ToList();
        _remaining = playlist.Songs
            .Where(song => !ReferenceEquals(song, current))
            .ToList();
        FisherYates(_remaining, _random);

        _history.Clear();
        if (current != null)
        {
            _history.Add(current);
            _historyIndex = 0;
        }
        else
        {
            _historyIndex = -1;
        }
    }

    private void StartNewCycle(PlaylistModel playlist, PlaylistSong? current)
    {
        _remaining = playlist.Songs.ToList();
        FisherYates(_remaining, _random);

        var nextIndex = _remaining.Count - 1;
        if (current == null || _remaining.Count <= 1 ||
            !ReferenceEquals(_remaining[nextIndex], current))
        {
            return;
        }

        var swapIndex = _random.Next(0, nextIndex);
        (_remaining[nextIndex], _remaining[swapIndex]) =
            (_remaining[swapIndex], _remaining[nextIndex]);
    }

    internal static void FisherYates<T>(IList<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static PlaylistSong? SongAt(PlaylistModel playlist, int index)
        => index >= 0 && index < playlist.Songs.Count
            ? playlist.Songs[index]
            : null;

    private static int IndexOf(
        PlaylistModel playlist,
        PlaylistSong song,
        int fallback)
    {
        var index = playlist.Songs.IndexOf(song);
        return index >= 0 ? index : fallback;
    }
}
