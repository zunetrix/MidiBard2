using MidiBard.Playlist;
using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlPlaylistTests
{
    [Fact]
    public void RichPlaylistMappingUsesExistingModelAndPreservesPlaylistOrder()
    {
        var playlist = new Playlist.Playlist
        {
            Id = 4,
            Name = "FFXIV",
            Songs =
            [
                new PlaylistSong
                {
                    IsPlayed = true,
                    AddedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                    Song = new Song
                    {
                        Id = 10,
                        FilePath = "/music/B.mid",
                        Name = "B",
                        Artist = "Composer B",
                        Duration = TimeSpan.FromSeconds(20),
                        PlayCount = 2,
                        IsValid = true,
                    },
                },
                new PlaylistSong
                {
                    Song = new Song
                    {
                        Id = 20,
                        FilePath = "/music/A.mid",
                        Name = "A",
                        Artist = "Composer A",
                        Duration = TimeSpan.FromSeconds(10),
                        IsValid = true,
                    },
                },
            ],
        };

        var response = RemoteControlService.ToPlaylistResponse(playlist, isCurrent: false);

        response.Songs.Select(song => song.SongId).ShouldBe(new[] { 10, 20 });
        response.Songs.Select(song => song.Position).ShouldBe(new[] { 1, 2 });
        response.Songs[0].FileName.ShouldBe("B.mid");
        response.Songs[0].IsPlayed.ShouldBeTrue();
        response.DurationMs.ShouldBe(30000);
        response.IsCurrent.ShouldBeFalse();
    }
}
