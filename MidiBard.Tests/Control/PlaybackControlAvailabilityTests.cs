using MidiBard.Control;

namespace MidiBard.Tests.Control;

public class PlaybackControlAvailabilityTests
{
    [Fact]
    public void OnlyLoadedBardCanPerform()
    {
        PlaybackControlAvailability.EvaluatePlayer(false, 23, "BRD").CanPerform.ShouldBeFalse();
        PlaybackControlAvailability.EvaluatePlayer(true, 5, "ARC").CanPerform.ShouldBeFalse();

        var bard = PlaybackControlAvailability.EvaluatePlayer(true, 23, "BRD");
        bard.CanPerform.ShouldBeTrue();
        bard.ClassJobAbbreviation.ShouldBe("BRD");
    }

    [Theory]
    [InlineData(PlaybackControlState.Idle, false, true, false, false, false)]
    [InlineData(PlaybackControlState.Ready, true, true, false, true, true)]
    [InlineData(PlaybackControlState.Playing, true, false, true, true, false)]
    [InlineData(PlaybackControlState.Paused, true, true, false, true, false)]
    [InlineData(PlaybackControlState.Completed, true, true, false, true, false)]
    public void BardControlCapabilitiesFollowPlaybackState(
        PlaybackControlState state,
        bool hasPlayback,
        bool canLoad,
        bool canPause,
        bool canStop,
        bool canStartEnsemble)
    {
        var result = PlaybackControlAvailability.Evaluate(
            PlaybackControlAvailability.EvaluatePlayer(true, 23, "BRD"),
            state,
            hasPlayback,
            ensembleRunning: false,
            inParty: true,
            isPartyLeader: true,
            monitoringEnabled: true,
            syncClientsEnabled: true);

        result.CanLoad.ShouldBe(canLoad);
        result.CanPause.ShouldBe(canPause);
        result.CanStop.ShouldBe(canStop);
        result.CanStartEnsemble.ShouldBe(canStartEnsemble);
        result.CanPlay.ShouldBe(
            hasPlayback && state is PlaybackControlState.Ready
                or PlaybackControlState.Paused
                or PlaybackControlState.Completed);
    }

    [Fact]
    public void EnsembleRunningBlocksStartingOrLoadingButKeepsStopAvailable()
    {
        var result = PlaybackControlAvailability.Evaluate(
            PlaybackControlAvailability.EvaluatePlayer(true, 23, "BRD"),
            PlaybackControlState.Completed,
            hasPlayback: true,
            ensembleRunning: true,
            inParty: true,
            isPartyLeader: true,
            monitoringEnabled: true,
            syncClientsEnabled: true);

        result.CanLoad.ShouldBeFalse();
        result.CanPlay.ShouldBeFalse();
        result.CanPause.ShouldBeFalse();
        result.CanStop.ShouldBeTrue();
        result.CanStartEnsemble.ShouldBeFalse();
    }

    [Fact]
    public void NonBardCannotInitiatePerformanceButCanPauseOrStopExistingPlayback()
    {
        var nonBard = PlaybackControlAvailability.EvaluatePlayer(true, 5, "ARC");

        var playing = PlaybackControlAvailability.Evaluate(
            nonBard,
            PlaybackControlState.Playing,
            hasPlayback: true,
            ensembleRunning: false,
            inParty: false,
            isPartyLeader: false,
            monitoringEnabled: true,
            syncClientsEnabled: true);

        playing.CanLoad.ShouldBeFalse();
        playing.CanPlay.ShouldBeFalse();
        playing.CanPause.ShouldBeTrue();
        playing.CanStop.ShouldBeTrue();
        playing.CanStartEnsemble.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void EnsembleRequiresEveryExistingPrerequisite(
        bool inParty,
        bool isPartyLeader,
        bool monitoringEnabled,
        bool syncClientsEnabled)
    {
        var result = PlaybackControlAvailability.Evaluate(
            PlaybackControlAvailability.EvaluatePlayer(true, 23, "BRD"),
            PlaybackControlState.Ready,
            hasPlayback: true,
            ensembleRunning: false,
            inParty,
            isPartyLeader,
            monitoringEnabled,
            syncClientsEnabled);

        result.CanStartEnsemble.ShouldBeFalse();
    }
}
