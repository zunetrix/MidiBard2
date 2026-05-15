using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Editing;

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

public sealed record MidiForgeChordTimingToleranceOptions(
    MidiForgeChordTimingToleranceMode Mode = MidiForgeChordTimingToleranceMode.Exact,
    long CustomTicks = 0);

public sealed record MidiForgeSplitChordsOptions(
    MidiForgeChordSplitStrategy Strategy = MidiForgeChordSplitStrategy.SameStartTick,
    MidiForgeChordGroupMode GroupMode = MidiForgeChordGroupMode.GroupMerged,
    int MinimumSimultaneousNotes = 2,
    bool InsertPartsAtEnd = true,
    MidiForgeChordTimingToleranceOptions? ChordTimingTolerance = null);

public sealed record MidiForgeSplitChordsResult(
    int SourceTracks,
    int CreatedTracks,
    int ChordGroups);

public sealed record MidiForgePickChordLinesOptions(
    int MaxSimultaneousNotes = 1,
    MidiForgeChordPickStrategy PickStrategy = MidiForgeChordPickStrategy.HighestChords,
    bool CreateNewTracks = true,
    bool RenameTracks = true,
    MidiForgeChordTimingToleranceOptions? ChordTimingTolerance = null);

public sealed record MidiForgePickChordLinesResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int PickedParts,
    IReadOnlyList<int> OutputTrackIndices);

public sealed record MidiForgeLimitSimultaneousNotesOptions(
    bool CreateNewTracks = true,
    MidiForgeSimultaneousLimitMode LimitMode = MidiForgeSimultaneousLimitMode.ActiveOverlaps,
    int MaximumActiveNotes = 1,
    MidiForgeNoteKeepPolicy KeepPolicy = MidiForgeNoteKeepPolicy.Highest);

public sealed record MidiForgeLimitSimultaneousNotesResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int RemovedNotes);

public sealed record MidiForgeStrumNotesOptions(
    bool CreateNewTracks = true,
    MidiForgeStrumDirection Direction = MidiForgeStrumDirection.LowToHigh,
    long StepTicks = 5,
    bool PreserveNoteEnds = true,
    long? StartTick = null,
    long? EndTick = null,
    MidiForgeChordTimingToleranceOptions? ChordTimingTolerance = null);

public sealed record MidiForgeStrumNotesResult(
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int StrummedChordGroups,
    int ChangedNotes);

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
