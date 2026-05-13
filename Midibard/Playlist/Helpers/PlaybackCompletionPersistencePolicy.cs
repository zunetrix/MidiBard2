namespace MidiBard.Playlist.Helpers;

internal static class PlaybackCompletionPersistencePolicy
{
    public static bool ShouldPersist(
        bool isInParty,
        bool isPartyLeader,
        bool actualEnsembleModeRunning,
        bool ensemblePlayEnabled,
        bool playOnMultipleDevices)
    {
        if (actualEnsembleModeRunning)
            return isInParty && isPartyLeader;

        if (!isInParty)
            return true;

        if (!ensemblePlayEnabled && !playOnMultipleDevices)
            return true;

        return isPartyLeader;
    }
}
