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
                    9000,
                    4,
                    42)),
            new EnsembleStatusResponse(
                true,
                true,
                false,
                true,
                true,
                true),
            new PlayerStatusResponse(true, 23, "BRD", true),
            new PlaybackControlsResponse(
                false,
                false,
                true,
                true,
                false,
                true,
                true,
                true),
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
        nowPlaying.GetProperty("positionMs").GetInt64().ShouldBe(1234);
        nowPlaying.GetProperty("durationMs").GetInt64().ShouldBe(9000);
        nowPlaying.GetProperty("playlistId").GetInt32().ShouldBe(4);
        nowPlaying.GetProperty("songId").GetInt32().ShouldBe(42);

        var ensemble = root.GetProperty("ensemble");
        ensemble.GetProperty("inParty").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("isPartyLeader").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("running").GetBoolean().ShouldBeFalse();
        ensemble.GetProperty("monitoringEnabled").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("syncClientsEnabled").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("autoAdvanceEnabled").GetBoolean().ShouldBeTrue();

        root.GetProperty("player").GetProperty("classJobAbbreviation").GetString().ShouldBe("BRD");
        root.GetProperty("player").GetProperty("canPerform").GetBoolean().ShouldBeTrue();
        root.GetProperty("controls").GetProperty("canLoad").GetBoolean().ShouldBeFalse();
        root.GetProperty("controls").GetProperty("canPause").GetBoolean().ShouldBeTrue();
        root.GetProperty("controls").GetProperty("canPrevious").GetBoolean().ShouldBeTrue();
        root.GetProperty("controls").GetProperty("canNext").GetBoolean().ShouldBeTrue();
        root.GetProperty("controls").GetProperty("canSeek").GetBoolean().ShouldBeTrue();
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
    public void EnsembleVisualizationSerializesCompactReadOnlyAssignments()
    {
        var playbackId = Guid.NewGuid();
        var response = new EnsembleVisualizationResponse(
            playbackId,
            new[]
            {
                new EnsembleInstrumentResponse(
                    5,
                    "Flute",
                    "Punching Baggins·Gilgamesh"),
                new EnsembleInstrumentResponse(
                    23,
                    "Double Bass",
                    null),
            });

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, RemoteControlJson.Options));

        var root = document.RootElement;
        root.GetProperty("playbackId").GetGuid().ShouldBe(playbackId);
        root.GetProperty("instruments").GetArrayLength().ShouldBe(2);
        root.GetProperty("instruments")[0]
            .GetProperty("performerName").GetString()
            .ShouldBe("Punching Baggins·Gilgamesh");
        root.GetProperty("instruments")[1]
            .GetProperty("performerName").ValueKind
            .ShouldBe(JsonValueKind.Null);
        root.GetProperty("instruments")[0]
            .TryGetProperty("activity", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void StatusEventSerializesWithoutPlaybackIdentity()
    {
        var response = new PlaybackEventResponse(
            13,
            "status_changed",
            null);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, RemoteControlJson.Options));

        document.RootElement.GetProperty("sequence").GetInt64().ShouldBe(13);
        document.RootElement.GetProperty("type").GetString().ShouldBe("status_changed");
        document.RootElement.GetProperty("playbackId").ValueKind.ShouldBe(JsonValueKind.Null);
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

        var seekRequest = JsonSerializer.Deserialize<SeekPlaybackRequest>(
            "{\"playbackId\":\"" + playbackId + "\",\"positionMs\":1234}",
            RemoteControlJson.Options);
        seekRequest.ShouldNotBeNull();
        seekRequest!.PlaybackId.ShouldBe(playbackId);
        seekRequest.PositionMs.ShouldBe(1234);

        var playModeRequest = JsonSerializer.Deserialize<SetPlayModeRequest>(
            """{"playMode":"list_repeat"}""",
            RemoteControlJson.Options);
        playModeRequest.ShouldNotBeNull();
        playModeRequest!.PlayMode.ShouldBe("list_repeat");

        var autoAdvanceRequest =
            JsonSerializer.Deserialize<SetEnsembleAutoAdvanceRequest>(
                """{"enabled":true}""",
                RemoteControlJson.Options);
        autoAdvanceRequest.ShouldNotBeNull();
        autoAdvanceRequest!.Enabled.ShouldBeTrue();
    }
}
