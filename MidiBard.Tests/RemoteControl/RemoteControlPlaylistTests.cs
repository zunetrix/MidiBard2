using MidiBard.Playlist;
using MidiBard.RemoteControl;
using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlPlaylistTests
{
    [Fact]
    public void RichPlaylistMappingUsesExistingModelAndPreservesPlaylistOrder()
    {
        var playlist = new PlaylistModel
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

    [Fact]
    public void PlaylistSummaryUsesHydratedSongDurations()
    {
        var playlist = new PlaylistModel
        {
            Id = 7,
            Name = "Long Set",
            Songs =
            [
                new PlaylistSong
                {
                    Song = new Song { Duration = TimeSpan.FromHours(2) },
                },
                new PlaylistSong
                {
                    Song = new Song { Duration = TimeSpan.FromMinutes(30) },
                },
            ],
        };

        var response = RemoteControlService.ToPlaylistSummaryResponse(
            playlist,
            currentId: 7);

        response.SongCount.ShouldBe(2);
        response.DurationMs.ShouldBe((long)TimeSpan.FromHours(2.5).TotalMilliseconds);
        response.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void EnsemblePartGroupingDoesNotDropDistinctPartsAfterFirstEightTracks()
    {
        var candidates = new[]
        {
            new RemoteControlService.EnsemblePartCandidate(0, 1, 5),
            new RemoteControlService.EnsemblePartCandidate(1, 1, 5),
            new RemoteControlService.EnsemblePartCandidate(2, 2, 6),
            new RemoteControlService.EnsemblePartCandidate(3, 2, 6),
            new RemoteControlService.EnsemblePartCandidate(4, 3, 7),
            new RemoteControlService.EnsemblePartCandidate(5, 3, 7),
            new RemoteControlService.EnsemblePartCandidate(6, 4, 8),
            new RemoteControlService.EnsemblePartCandidate(7, 4, 8),
            new RemoteControlService.EnsemblePartCandidate(8, 5, 9),
            new RemoteControlService.EnsemblePartCandidate(9, 6, 10),
        };

        var parts = RemoteControlService.DistinctEnsembleParts(candidates);

        parts.Select(part => part.TrackIndex)
            .ShouldBe(new[] { 0, 2, 4, 6, 8, 9 });
    }
}
