namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeSplitDrumkitOptions(
    bool AutoEditAfterSplit = true,
    bool CreateRestTrack = true,
    bool MoveSourceTracksToEnd = true,
    MidiForgeDrumTransposePreset TransposePreset = MidiForgeDrumTransposePreset.Default,
    bool DeleteOriginalTracks = false);

public sealed record MidiForgeSplitDrumkitResult(
    int SourceTracks,
    int CreatedTracks,
    int RestTracks,
    int AutoEditedTracks,
    int TransposedNotes,
    int DeletedSourceTracks);

public sealed record MidiForgeDisassembleDrumkitOptions(
    bool DeleteOriginalTracks = false);

public sealed record MidiForgeDisassembleDrumkitResult(
    int SourceTracks,
    int CreatedTracks,
    int DeletedSourceTracks);

public sealed record MidiForgeTransposeToDrumNoteOptions(
    int TargetNote,
    string TrackName = "",
    bool DeleteOriginalTracks = true);

public sealed record MidiForgeTransposeToDrumNoteResult(
    int SourceTracks,
    int CreatedTracks,
    int DeletedSourceTracks,
    int SkippedTracks);
