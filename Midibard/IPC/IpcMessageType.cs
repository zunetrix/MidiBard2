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
