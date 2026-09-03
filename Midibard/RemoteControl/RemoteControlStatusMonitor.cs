namespace MidiBard.RemoteControl;

internal sealed record RemoteControlStatusFingerprint(
    bool PlayerLoaded,
    uint ClassJobId,
    bool InParty,
    bool IsPartyLeader,
    bool MonitorOnEnsemble,
    bool SyncClients,
    int PlayMode,
    bool EnsembleAutoAdvanceEnabled,
    int? CurrentPlaylistId,
    string CurrentPlaylistName,
    bool CurrentPlaylistIsTemporary);

internal sealed class RemoteControlStatusMonitor
{
    private readonly RemoteEventJournal _events;
    private RemoteControlStatusFingerprint? _last;

    public RemoteControlStatusMonitor(RemoteEventJournal events)
    {
        _events = events;
    }

    public bool Observe(RemoteControlStatusFingerprint current)
    {
        if (_last == null)
        {
            _last = current;
            return false;
        }

        if (_last == current)
            return false;

        _last = current;
        _events.Publish(null, RemotePlaybackEventType.StatusChanged);
        return true;
    }
}
