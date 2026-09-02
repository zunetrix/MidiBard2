using System.Text.Json;

using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlContractTests
{
    [Fact]
    public void StatusSerializesPlaybackPlayerCapabilitiesAndPlaylistIdentity()
    {
        var playbackId = Guid.NewGuid();
        var response = new StatusResponse(
            12,
            new PlaybackStatusResponse(
                "playing",
                "single",
                new NowPlayingResponse(
                    playbackId,
                    "Frog's Theme.mid",
                    1234,
                    9000)),
            new EnsembleStatusResponse(
                true,
                true,
                false,
                true,
                true),
            new PlayerStatusResponse(true, 23, "BRD", true),
            new PlaybackControlsResponse(false, false, true, true, false),
            new CurrentPlaylistResponse(4, "FFXIV", false));

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, RemoteControlJson.Options));

        var root = document.RootElement;
        root.GetProperty("latestEventSequence").GetInt64().ShouldBe(12);
        root.GetProperty("playback").GetProperty("state").GetString().ShouldBe("playing");
        root.GetProperty("playback").GetProperty("playMode").GetString().ShouldBe("single");

        var nowPlaying = root.GetProperty("playback").GetProperty("nowPlaying");
        nowPlaying.GetProperty("playbackId").GetGuid().ShouldBe(playbackId);
        nowPlaying.GetProperty("fileName").GetString().ShouldBe("Frog's Theme.mid");

        root.GetProperty("player").GetProperty("classJobAbbreviation").GetString().ShouldBe("BRD");
        root.GetProperty("player").GetProperty("canPerform").GetBoolean().ShouldBeTrue();
        root.GetProperty("controls").GetProperty("canLoad").GetBoolean().ShouldBeFalse();
        root.GetProperty("controls").GetProperty("canPause").GetBoolean().ShouldBeTrue();
        root.GetProperty("currentPlaylist").GetProperty("id").GetInt32().ShouldBe(4);

        root.EnumerateObject().Select(property => property.Name).ShouldBe(
            new[]
            {
                "latestEventSequence",
                "playback",
                "ensemble",
                "player",
                "controls",
                "currentPlaylist",
            });
    }

    [Fact]
    public void RichPlaylistPreservesFileNameWhileAddingLibraryMetadata()
    {
        var response = new PlaylistResponse(
            4,
            "FFXIV",
            true,
            false,
            1,
            9000,
            new[]
            {
                new PlaylistSongResponse(
                    42,
                    1,
                    "Frog's Theme.mid",
                    "Frog's Theme",
                    "Yasunori Mitsuda",
                    1995,
                    9000,
                    3,
                    "2026-09-01T12:00:00.0000000Z",
                    true,
                    5,
                    new[] { "Chrono Trigger" },
                    "Octet",
                    true,
                    "2026-08-01T12:00:00.0000000Z",
                    "2026-07-01T12:00:00.0000000Z"),
            });

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, RemoteControlJson.Options));

        var root = document.RootElement;
        root.GetProperty("id").GetInt32().ShouldBe(4);
        root.GetProperty("isCurrent").GetBoolean().ShouldBeTrue();

        var song = root.GetProperty("songs")[0];
        song.GetProperty("fileName").GetString().ShouldBe("Frog's Theme.mid");
        song.GetProperty("songId").GetInt32().ShouldBe(42);
        song.GetProperty("artist").GetString().ShouldBe("Yasunori Mitsuda");
        song.GetProperty("playCount").GetInt32().ShouldBe(3);
        song.GetProperty("tags")[0].GetString().ShouldBe("Chrono Trigger");
    }

    [Fact]
    public void ErrorResponseContainsOnlyStableCodeAndMessage()
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new ErrorResponse("playback_changed", "The requested playback is no longer active."),
                RemoteControlJson.Options));

        document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ShouldBe(new[] { "code", "message" });
    }

    [Fact]
    public void PlaybackRequestsReadCamelCaseIds()
    {
        var playbackId = Guid.NewGuid();

        var playbackRequest = JsonSerializer.Deserialize<PlaybackHandleRequest>(
            $$"""{"playbackId":"{{playbackId}}"}""",
            RemoteControlJson.Options);
        playbackRequest.ShouldNotBeNull();
        playbackRequest!.PlaybackId.ShouldBe(playbackId);

        var songRequest = JsonSerializer.Deserialize<LoadPlaylistSongRequest>(
            """{"playlistId":4,"songId":42}""",
            RemoteControlJson.Options);
        songRequest.ShouldNotBeNull();
        songRequest!.PlaylistId.ShouldBe(4);
        songRequest.SongId.ShouldBe(42);
    }
}
