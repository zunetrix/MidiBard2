namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeAutoEditOptions(
    int MaxSimultaneousNotes = 1,
    MidiForgeChordPickStrategy PickStrategy = MidiForgeChordPickStrategy.HighestChords,
    bool AdaptOutOfRangeNotes = true,
    bool CreateNewTracks = true,
    MidiForgeRangeFitStrategy RangeStrategy = MidiForgeRangeFitStrategy.FitNotesIndividually,
    bool RenameTracks = true);

public sealed record MidiForgeAutoEditResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int PickedParts,
    int ChangedNotes);

public sealed record MidiForgePrepareForPlaybackOptions(
    bool FillEmptyTrackNames = true,
    bool ApplyTrackNameTransposes = true,
    bool SplitDrumkits = true,
    int MaxSimultaneousNotes = 1,
    MidiForgeChordPickStrategy PickStrategy = MidiForgeChordPickStrategy.HighestChords,
    MidiForgeRangeFitStrategy RangeStrategy = MidiForgeRangeFitStrategy.LowerHighNotesFirst,
    MidiForgeDrumTransposePreset DrumTransposePreset = MidiForgeDrumTransposePreset.Default);

public sealed record MidiForgePrepareForPlaybackResult(
    int SourceTracks,
    int FilledTrackNames,
    int TrackNameTransposeTracks,
    int TrackNameTransposeChangedNotes,
    int DrumSourceTracks,
    int DrumTracksCreated,
    int DrumSourceTracksDeleted,
    int DrumRestTracks,
    int DrumAutoEditedTracks,
    int DrumTransposedNotes,
    int AutoEditedTracks,
    int AutoEditedReplacedTracks,
    int AutoEditPickedParts,
    int AutoEditChangedNotes);
