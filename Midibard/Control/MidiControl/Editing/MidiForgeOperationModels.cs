using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Editing;

public enum MidiForgeRangeFitStrategy
{
    FitNotesIndividually,
    LowerHighNotesFirst,
    BestOctaveFit,
}

public sealed record MidiForgeAdaptToRangeOptions(
    bool CreateNewTracks = true,
    MidiForgeRangeFitStrategy RangeStrategy = MidiForgeRangeFitStrategy.BestOctaveFit,
    bool RenameTracks = true);

public sealed record MidiForgeAdaptToRangeResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int OctaveShiftedTracks,
    int ChangedNotes);

public enum MidiForgeChordSplitStrategy
{
    SameStartTick,
    SameStartTickAndLength,
}

public enum MidiForgeChordGroupMode
{
    GroupMerged,
    Individual,
    Group,
}

public sealed record MidiForgeSplitChordsOptions(
    MidiForgeChordSplitStrategy Strategy = MidiForgeChordSplitStrategy.SameStartTick,
    MidiForgeChordGroupMode GroupMode = MidiForgeChordGroupMode.GroupMerged,
    int MinimumSimultaneousNotes = 2,
    bool InsertPartsAtEnd = true);

public sealed record MidiForgeSplitChordsResult(
    int SourceTracks,
    int CreatedTracks,
    int ChordGroups);

public enum MidiForgeChordPickStrategy
{
    HighestChords,
    OddChords,
}

public sealed record MidiForgePickChordLinesOptions(
    int MaxSimultaneousNotes = 1,
    MidiForgeChordPickStrategy PickStrategy = MidiForgeChordPickStrategy.HighestChords,
    bool CreateNewTracks = true,
    bool RenameTracks = true);

public sealed record MidiForgePickChordLinesResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int PickedParts,
    IReadOnlyList<int> OutputTrackIndices);

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

public sealed record MidiForgeSplitToneRangeOptions(
    int MinimumNote = MidiForgeAnalysis.PlayableLowestMidiNote,
    int MaximumNote = MidiForgeAnalysis.PlayableHighestMidiNote);

public sealed record MidiForgeSplitLengthRangeOptions(
    long MinimumLengthTicks = 0,
    long MaximumLengthTicks = 0);

public sealed record MidiForgeSplitNotesRangeResult(
    int SourceTracks,
    int CreatedTracks,
    int InRangeTracks,
    int OutOfRangeTracks,
    int InRangeNotes,
    int OutOfRangeNotes);

public sealed record MidiForgeSplitOverlappedNotesResult(
    int SourceTracks,
    int CreatedTracks,
    int OverlapGroups,
    int OverlappedNotes,
    int NonOverlappedNotes);

public sealed record MidiForgeTrimOverlappedNotesResult(
    int SourceTracks,
    int CreatedTracks,
    int ChangedNotes);

public sealed record MidiForgeExtendNotesDurationOptions(
    long MaximumDurationTicks = 0,
    bool RespectEmptyMeasures = true);

public sealed record MidiForgeExtendNotesDurationResult(
    int SourceTracks,
    int CreatedTracks,
    int ChangedNotes);

public sealed record MidiForgeSplitEqualNotesResult(
    int SourceTracks,
    int CreatedTracks,
    int EqualNotes,
    int NonEqualNotes);

public sealed record MidiForgeDifferenceTracksResult(
    int SourceTracks,
    int CreatedTracks,
    int DiffNotes,
    int RestNotes);

public sealed record MidiForgeSplitNotesIntoTracksOptions(
    int NumberOfTracks = 2,
    int EveryNotesAmount = 1);

public sealed record MidiForgeSplitNotesIntoTracksResult(
    int SourceTracks,
    int CreatedTracks,
    int DistributedNotes);

public sealed record MidiForgeGeneratePitchBendNotesOptions(
    bool DeleteOriginalTracks = false);

public sealed record MidiForgeGeneratePitchBendNotesResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int GeneratedNotes,
    int SkippedTracks);

public sealed record MidiForgeChangeNoteLengthOptions(
    long MinimumLengthTicks = 0,
    long MaximumLengthTicks = 0,
    long NewLengthTicks = 240,
    bool DeleteOriginalTracks = false);

public sealed record MidiForgeChangeNoteLengthResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int ChangedNotes);

public sealed record MidiForgeApplyTrackNameTransposeOptions(
    bool CreateNewTracks = false);

public sealed record MidiForgeApplyTrackNameTransposeResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int CleanedTrackNames,
    int ChangedNotes,
    int SkippedTracks);

public sealed record MidiForgeMergeGuitarToneTracksOptions(
    IReadOnlyDictionary<int, int> ToneByTrackIndex,
    bool DeleteOriginalTracks = false,
    string TrackName = "ProgramElectricGuitar",
    bool IncludePitchBendEvents = true,
    bool IncludeControlChangeEvents = true);

public sealed record MidiForgeMergeGuitarToneTracksResult(
    int SourceTracks,
    int CreatedTracks,
    int DeletedSourceTracks,
    int SkippedTracks,
    int GeneratedProgramChanges,
    int MergedNotes,
    int MergedChannelEvents);

public enum MidiForgeTrackNameFillMode
{
    Ffxiv,
    Midi,
}

public sealed record MidiForgeTrackNameResult(
    int SourceTracks,
    int RenamedTracks);

public sealed record MidiForgeSetTrackProgramOptions(
    int ProgramNumber,
    bool ReplaceAllProgramChanges = true,
    bool RenameTracks = true,
    MidiForgeTrackNameFillMode RenameMode = MidiForgeTrackNameFillMode.Ffxiv);

public sealed record MidiForgeSetTrackProgramResult(
    int SourceTracks,
    int ChangedTracks,
    int AddedProgramChanges,
    int UpdatedProgramChanges,
    int RenamedTracks);

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
