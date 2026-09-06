using MidiBard.Control.MidiControl;
using MidiBard.Playlist;

using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Tests.Control.MidiControl;

public class PlaylistShuffleSessionTests
{
    [Fact]
    public void InitialCycleVisitsEveryOtherSongExactlyOnce()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(12345));
        var current = 0;
        var visited = new List<int>();

        for (var i = 0; i < playlist.Songs.Count - 1; i++)
        {
            current = shuffle.NextIndex(playlist, current);
            visited.Add(current);
        }

        visited.ShouldNotContain(0);
        visited.Distinct().Count().ShouldBe(playlist.Songs.Count - 1);
        visited.OrderBy(index => index).ShouldBe(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void NewCycleContainsEverySongAndDoesNotRepeatBoundarySong()
    {
        var playlist = PlaylistWithSongs(4);
        var shuffle = new PlaylistShuffleSession(new Random(54321));
        var current = 0;

        for (var i = 0; i < playlist.Songs.Count - 1; i++)
            current = shuffle.NextIndex(playlist, current);

        var lastOfFirstCycle = current;
        var secondCycle = new List<int>();

        for (var i = 0; i < playlist.Songs.Count; i++)
        {
            current = shuffle.NextIndex(playlist, current);
            secondCycle.Add(current);
        }

        secondCycle[0].ShouldNotBe(lastOfFirstCycle);
        secondCycle.Distinct().Count().ShouldBe(playlist.Songs.Count);
        secondCycle.OrderBy(index => index).ShouldBe(new[] { 0, 1, 2, 3 });
    }

    [Fact]
    public void ShuffleSessionDoesNotDependOnPersistentPlayedFlags()
    {
        var playlist = PlaylistWithSongs(5);
        foreach (var playlistSong in playlist.Songs)
            playlistSong.IsPlayed = true;

        var shuffle = new PlaylistShuffleSession(new Random(24680));
        var current = 0;
        var visited = new List<int>();

        for (var i = 0; i < playlist.Songs.Count - 1; i++)
        {
            current = shuffle.NextIndex(playlist, current);
            visited.Add(current);
        }

        visited.Distinct().Count().ShouldBe(playlist.Songs.Count - 1);
        visited.ShouldNotContain(0);
    }

    [Fact]
    public void PreviousAndNextWalkExistingShuffleHistory()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(9876));
        var current = 0;

        var first = shuffle.NextIndex(playlist, current);
        var second = shuffle.NextIndex(playlist, first);

        var previous = shuffle.PreviousIndex(playlist, second);
        previous.ShouldBe(first);

        var forwardAgain = shuffle.NextIndex(playlist, previous);
        forwardAgain.ShouldBe(second);
    }

    [Fact]
    public void LeavingAndReenteringShuffleClearsHistoryWithoutNavigation()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(31415));
        var current = 0;

        var first = shuffle.NextIndex(playlist, current);
        var second = shuffle.NextIndex(playlist, first);

        shuffle.OnPlayModeChanged(PlayMode.Random, PlayMode.ListOrdered);
        shuffle.OnPlayModeChanged(PlayMode.ListOrdered, PlayMode.Random);

        shuffle.PreviousIndex(playlist, second).ShouldBe(second);
    }

    [Fact]
    public void KeepingShuffleSelectedPreservesHistory()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(27182));
        var current = 0;

        var first = shuffle.NextIndex(playlist, current);
        var second = shuffle.NextIndex(playlist, first);

        shuffle.OnPlayModeChanged(PlayMode.Random, PlayMode.Random);

        shuffle.PreviousIndex(playlist, second).ShouldBe(first);
    }

    [Fact]
    public void StartingWithoutCurrentSongBeginsWithShuffledPlaylist()
    {
        var playlist = PlaylistWithSongs(4);
        var shuffle = new PlaylistShuffleSession(new Random(2222));
        var current = -1;
        var cycle = new List<int>();

        for (var i = 0; i < playlist.Songs.Count; i++)
        {
            current = shuffle.NextIndex(playlist, current);
            cycle.Add(current);
        }

        cycle.Distinct().Count().ShouldBe(playlist.Songs.Count);
        cycle.OrderBy(index => index).ShouldBe(new[] { 0, 1, 2, 3 });
    }

    [Fact]
    public void PlaylistReorderStartsFreshShuffleSession()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(13579));

        var first = shuffle.NextIndex(playlist, 0);
        var second = shuffle.NextIndex(playlist, first);
        var currentSong = playlist.Songs[second];

        playlist.MoveSongToIndex(0, playlist.Songs.Count - 1)
            .ShouldBeTrue();

        var remappedCurrent = playlist.Songs.IndexOf(currentSong);

        shuffle.PreviousIndex(playlist, remappedCurrent)
            .ShouldBe(remappedCurrent);
    }

    [Fact]
    public void SwitchingPlaylistObjectStartsFreshShuffleSession()
    {
        var firstPlaylist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(4242));

        var first = shuffle.NextIndex(firstPlaylist, 0);
        _ = shuffle.NextIndex(firstPlaylist, first);

        var secondPlaylist = PlaylistWithSongs(5);
        var current = 2;

        shuffle.PreviousIndex(secondPlaylist, current)
            .ShouldBe(current);
    }

    [Fact]
    public void ManualSongJumpStartsFreshShuffleSession()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(7070));

        var first = shuffle.NextIndex(playlist, 0);
        var second = shuffle.NextIndex(playlist, first);
        var manual = Enumerable.Range(0, playlist.Songs.Count)
            .First(index => index != first && index != second);

        shuffle.PreviousIndex(playlist, manual)
            .ShouldBe(manual);
    }

    [Fact]
    public void AddingSongStartsFreshShuffleSession()
    {
        var playlist = PlaylistWithSongs(4);
        var shuffle = new PlaylistShuffleSession(new Random(8080));

        var first = shuffle.NextIndex(playlist, 0);
        var second = shuffle.NextIndex(playlist, first);

        playlist.AddSong(new PlaylistSong
        {
            Song = new Song
            {
                Id = 99,
                Name = "Added",
                FilePath = "/music/99.mid",
            },
        });

        shuffle.PreviousIndex(playlist, second)
            .ShouldBe(second);
    }

    [Fact]
    public void RemovingSongStartsFreshShuffleSession()
    {
        var playlist = PlaylistWithSongs(5);
        var shuffle = new PlaylistShuffleSession(new Random(9090));

        var first = shuffle.NextIndex(playlist, 0);
        var second = shuffle.NextIndex(playlist, first);
        var currentSong = playlist.Songs[second];
        var removeIndex = Enumerable.Range(0, playlist.Songs.Count)
            .First(index => index != second);

        playlist.RemoveSongAt(removeIndex).ShouldBeTrue();
        var remappedCurrent = playlist.Songs.IndexOf(currentSong);

        shuffle.PreviousIndex(playlist, remappedCurrent)
            .ShouldBe(remappedCurrent);
    }

    [Fact]
    public void TwoSongPlaylistAlternatesAcrossShuffleCycles()
    {
        var playlist = PlaylistWithSongs(2);
        var shuffle = new PlaylistShuffleSession(new Random(1010));
        var current = 0;

        current = shuffle.NextIndex(playlist, current);
        current.ShouldBe(1);

        current = shuffle.NextIndex(playlist, current);
        current.ShouldBe(0);

        current = shuffle.NextIndex(playlist, current);
        current.ShouldBe(1);

        current = shuffle.NextIndex(playlist, current);
        current.ShouldBe(0);
    }

    [Fact]
    public void EmptyAndNullPlaylistsLeaveCurrentIndexUnchanged()
    {
        var shuffle = new PlaylistShuffleSession(new Random(1));
        var empty = PlaylistWithSongs(0);

        shuffle.NextIndex(null, 7).ShouldBe(7);
        shuffle.PreviousIndex(null, 7).ShouldBe(7);
        shuffle.NextIndex(empty, 7).ShouldBe(7);
        shuffle.PreviousIndex(empty, 7).ShouldBe(7);
    }

    [Fact]
    public void SingleSongPlaylistStaysOnOnlySong()
    {
        var playlist = PlaylistWithSongs(1);
        var shuffle = new PlaylistShuffleSession(new Random(1));

        shuffle.NextIndex(playlist, 0).ShouldBe(0);
        shuffle.PreviousIndex(playlist, 0).ShouldBe(0);
    }

    private static PlaylistModel PlaylistWithSongs(int count)
    {
        return new PlaylistModel
        {
            Id = 7,
            Name = "Shuffle",
            Songs = Enumerable.Range(1, count)
                .Select(id => new PlaylistSong
                {
                    Song = new Song
                    {
                        Id = id,
                        Name = "Song " + id,
                        FilePath = $"/music/{id}.mid",
                    },
                })
                .ToList(),
        };
    }
}
