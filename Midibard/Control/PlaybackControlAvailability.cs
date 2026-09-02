#nullable enable

namespace MidiBard.Control;

internal enum PlaybackControlState
{
    Idle,
    Ready,
    Playing,
    Paused,
    Completed,
}

internal sealed record PerformanceAvailabilitySnapshot(
    bool PlayerLoaded,
    uint ClassJobId,
    string ClassJobAbbreviation,
    bool CanPerform);

internal sealed record PlaybackControlAvailabilitySnapshot(
    PerformanceAvailabilitySnapshot Player,
    bool CanLoad,
    bool CanPlay,
    bool CanPause,
    bool CanStop,
    bool CanStartEnsemble);

internal static class PlaybackControlAvailability
{
    internal const uint BardClassJobId = 23;

    public static PerformanceAvailabilitySnapshot GetPlayerSnapshot()
    {
        var playerState = DalamudApi.PlayerState;
        if (!playerState.IsLoaded)
            return EvaluatePlayer(false, 0, string.Empty);

        var classJob = playerState.ClassJob;
        var abbreviation = classJob.ValueNullable?.Abbreviation.ToString() ?? string.Empty;
        return EvaluatePlayer(true, classJob.RowId, abbreviation);
    }

    internal static PerformanceAvailabilitySnapshot EvaluatePlayer(
        bool playerLoaded,
        uint classJobId,
        string? classJobAbbreviation)
    {
        return new PerformanceAvailabilitySnapshot(
            playerLoaded,
            playerLoaded ? classJobId : 0,
            playerLoaded ? classJobAbbreviation ?? string.Empty : string.Empty,
            playerLoaded && classJobId == BardClassJobId);
    }

    internal static PlaybackControlAvailabilitySnapshot Evaluate(
        PerformanceAvailabilitySnapshot player,
        PlaybackControlState playbackState,
        bool hasPlayback,
        bool ensembleRunning,
        bool inParty,
        bool isPartyLeader,
        bool monitoringEnabled,
        bool syncClientsEnabled)
    {
        var canLoad = player.CanPerform
            && playbackState != PlaybackControlState.Playing
            && !ensembleRunning;
        var canPlay = player.CanPerform
            && hasPlayback
            && playbackState is PlaybackControlState.Ready
                or PlaybackControlState.Paused
                or PlaybackControlState.Completed
            && !ensembleRunning;
        var canPause = hasPlayback
            && playbackState == PlaybackControlState.Playing
            && !ensembleRunning;
        var canStop = hasPlayback;
        var canStartEnsemble = player.CanPerform
            && hasPlayback
            && playbackState == PlaybackControlState.Ready
            && !ensembleRunning
            && inParty
            && isPartyLeader
            && monitoringEnabled
            && syncClientsEnabled;

        return new PlaybackControlAvailabilitySnapshot(
            player,
            canLoad,
            canPlay,
            canPause,
            canStop,
            canStartEnsemble);
    }
}
