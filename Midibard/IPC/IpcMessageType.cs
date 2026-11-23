namespace MidiBard.Ipc;

public enum IpcMessageType
{
    LoadPlaylist,
    RemoveTrackIndex,
    MoveSongToIndex,
    ChangeSongPlayedStatus,
    ResetAllSongsPlayedStatus,
    LoadPlaybackIndex,

    UpdateMidiFileConfig,
    UpdateEnsembleMember,
    MidiEvent,
    SetInstrument,
    EnsembleStartTime,
    UpdateDefaultPerformer,

    SetOption,
    ShowWindow,
    SyncAllSettings,
    Object,
    SyncPlayStatus,
    PlaybackSpeed,
    GlobalTranspose,
    MoveToTime,
    ReloadLyrics,
    SendDownloadedSong,

    ErrPlaybackNull,

    DisconnectDatabase,
    ReconnectDatabase,

    LoadTempPlaylist,

    ArmHeartbeatSync,

    /// <summary>
    /// Remote-sync protocol message relayed by a gateway client to its same-machine
    /// siblings. StringData[0] carries the protocol JSON (see MidiBard.RemoteSync.SyncProtocol).
    /// </summary>
    RemoteSyncEnvelope,
}

enum PlaylistOperation
{
    SyncAll = 1,
    AddIndex,
    CloneIndex,
    RemoveIndex,
    ReorderIndex,
    RenameIndex,
}
