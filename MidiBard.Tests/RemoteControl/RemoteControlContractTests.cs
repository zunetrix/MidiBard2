using System.Text.Json;

using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlContractTests
{
    [Fact]
    public void StatusSerializesOnlyDefinedPlaybackAndEnsembleState()
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
                true));

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

        var ensemble = root.GetProperty("ensemble");
        ensemble.GetProperty("inParty").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("isPartyLeader").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("running").GetBoolean().ShouldBeFalse();
        ensemble.GetProperty("monitoringEnabled").GetBoolean().ShouldBeTrue();
        ensemble.GetProperty("syncClientsEnabled").GetBoolean().ShouldBeTrue();

        root.EnumerateObject().Select(property => property.Name).ShouldBe(
            new[] { "latestEventSequence", "playback", "ensemble" });
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
    public void PlaybackHandleRequestReadsCamelCasePlaybackId()
    {
        var playbackId = Guid.NewGuid();

        var request = JsonSerializer.Deserialize<PlaybackHandleRequest>(
            $$"""{"playbackId":"{{playbackId}}"}""",
            RemoteControlJson.Options);

        request.ShouldNotBeNull();
        request!.PlaybackId.ShouldBe(playbackId);
    }
}
