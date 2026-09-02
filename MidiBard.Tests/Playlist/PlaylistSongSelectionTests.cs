using MidiBard.Playlist;

namespace MidiBard.Tests.Playlist;

public class PlaylistSongSelectionTests
{
    [Fact]
    public void SongIdSelectionIsStableAcrossDuplicateFileNames()
    {
        var playlist = new Playlist.Playlist
        {
            Name = "Recording",
            Songs =
            [
                new PlaylistSong
                {
                    Song = new Song { Id = 10, FilePath = "/a/Test.mid", Name = "First" },
                },
                new PlaylistSong
                {
                    Song = new Song { Id = 20, FilePath = "/b/Test.mid", Name = "Second" },
                },
            ],
        };

        global::MidiBard.PlaylistManager.FindSongIndexById(playlist, 20).ShouldBe(1);
    }

    [Fact]
    public void MissingSongIdDoesNotResolve()
    {
        var playlist = new Playlist.Playlist
        {
            Name = "Recording",
            Songs =
            [
                new PlaylistSong { Song = new Song { Id = 10, Name = "Song" } },
            ],
        };

        global::MidiBard.PlaylistManager.FindSongIndexById(playlist, 99).ShouldBe(-1);
        global::MidiBard.PlaylistManager.FindSongIndexById(null, 10).ShouldBe(-1);
    }
}
