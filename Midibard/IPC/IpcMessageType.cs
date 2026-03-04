namespace MidiBard.Ipc;

public enum IpcMessageType
{
    Hello = 1,
    Bye,
    Acknowledge,

    GetMaster,
    SetSlave,
    SetUnslave,

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
