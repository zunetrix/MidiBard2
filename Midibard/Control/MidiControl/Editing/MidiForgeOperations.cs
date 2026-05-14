using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.Commands.Drum;
using MidiBard.Control.MidiControl.Editing.Commands.Guitar;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;
using MidiBard.Util;

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

public static class MidiForgeOperations
{
    public static int MaximumGuitarToneMergeTracks => MidiForgeGuitarTonePrimitives.MaximumMergeTracks;

    public static MidiForgeAdaptToRangeResult AdaptTracksToPlayableRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeAdaptToRangeOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new AdaptTracksToPlayableRangeCommand(),
            new AdaptTracksToPlayableRangeCommandOptions(trackIndices.ToArray(), options));

    public static int AdaptMidiNoteToPlayableRange(int midiNote)
        => MidiForgeNotePrimitives.AdaptMidiNoteToPlayableRange(midiNote);

    public static MidiForgeSplitChordsResult SplitTracksChords(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitChordsOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new SplitTracksChordsCommand(),
            new SplitTracksChordsCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgePickChordLinesResult PickChordLines(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgePickChordLinesOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new PickChordLinesCommand(),
            new PickChordLinesCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeSplitOverlappedNotesResult SplitTracksOverlappedNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
        => ExecuteCompatibilityCommand(
            file,
            new SplitTracksOverlappedNotesCommand(),
            new SplitTracksOverlappedNotesCommandOptions(trackIndices.ToArray()));

    public static MidiForgeTrimOverlappedNotesResult TrimOverlappedSustainedNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
        => ExecuteCompatibilityCommand(
            file,
            new TrimOverlappedSustainedNotesCommand(),
            new TrimOverlappedSustainedNotesCommandOptions(trackIndices.ToArray()));

    public static MidiForgeAutoEditResult AutoEditTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeAutoEditOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new AutoEditSelectedTracksCommand(),
            new AutoEditSelectedTracksCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgePrepareForPlaybackResult PrepareForPlayback(
        EditableMidiFile file,
        MidiForgePrepareForPlaybackOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new PrepareForPlaybackCommand(),
            new PrepareForPlaybackCommandOptions(options));

    public static MidiForgeSplitDrumkitResult SplitDrumkitTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitDrumkitOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new SplitDrumkitTracksCommand(),
            new SplitDrumkitTracksCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeDisassembleDrumkitResult DisassembleDrumkitTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeDisassembleDrumkitOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new DisassembleDrumkitTracksCommand(),
            new DisassembleDrumkitTracksCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeTransposeToDrumNoteResult TransposeSingleNoteTracksToDrumNote(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeTransposeToDrumNoteOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new TransposeSingleNoteTracksToDrumNoteCommand(),
            new TransposeSingleNoteTracksToDrumNoteCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeSplitNotesRangeResult SplitTracksByToneRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitToneRangeOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new SplitTracksByToneRangeCommand(),
            new SplitTracksByToneRangeCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeSplitNotesRangeResult SplitTracksByLengthRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitLengthRangeOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new SplitTracksByLengthRangeCommand(),
            new SplitTracksByLengthRangeCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeExtendNotesDurationResult ExtendNotesDuration(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeExtendNotesDurationOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new ExtendNotesDurationCommand(),
            new ExtendNotesDurationCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeSplitEqualNotesResult SplitTracksEqualNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        int targetTrackIndex)
        => ExecuteCompatibilityCommand(
            file,
            new SplitTracksEqualNotesCommand(),
            new SplitTracksEqualNotesCommandOptions(trackIndices.ToArray(), targetTrackIndex));

    public static MidiForgeDifferenceTracksResult DifferenceTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        int targetTrackIndex)
        => ExecuteCompatibilityCommand(
            file,
            new DifferenceTracksCommand(),
            new DifferenceTracksCommandOptions(trackIndices.ToArray(), targetTrackIndex));

    public static MidiForgeSplitNotesIntoTracksResult SplitNotesIntoTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitNotesIntoTracksOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new SplitNotesIntoTracksCommand(),
            new SplitNotesIntoTracksCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeGeneratePitchBendNotesResult GeneratePitchBendNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeGeneratePitchBendNotesOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new GeneratePitchBendNotesCommand(),
            new GeneratePitchBendNotesCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeChangeNoteLengthResult ChangeTrackNoteLengths(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeChangeNoteLengthOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new ChangeTrackNoteLengthsCommand(),
            new ChangeTrackNoteLengthsCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeApplyTrackNameTransposeResult ApplyTrackNameTransposes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeApplyTrackNameTransposeOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new ApplyTrackNameTransposesCommand(),
            new ApplyTrackNameTransposesCommandOptions(trackIndices.ToArray(), options));

    public static MidiForgeMergeGuitarToneTracksResult MergeGuitarToneTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeMergeGuitarToneTracksOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new MergeGuitarToneTracksCommand(),
            new MergeGuitarToneTracksCommandOptions(trackIndices.ToArray(), options));

    public static bool TryResolveGuitarToneFromTrackName(string trackName, out int tone)
        => MidiForgeGuitarTonePrimitives.TryResolveToneFromTrackName(trackName, out tone);

    public static bool TryResolveGuitarToneFromProgram(SevenBitNumber programNumber, out int tone)
        => MidiForgeGuitarTonePrimitives.TryResolveToneFromProgram(programNumber, out tone);

    public static bool TryResolveGuitarToneFromInstrumentId(uint? instrumentId, out int tone)
        => MidiForgeGuitarTonePrimitives.TryResolveToneFromInstrumentId(instrumentId, out tone);

    public static bool TryResolveGuitarProgramForTone(int tone, out SevenBitNumber programNumber)
        => MidiForgeGuitarTonePrimitives.TryResolveProgramForTone(tone, out programNumber);

    public static MidiForgeTrackNameResult FillEmptyTrackNames(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeTrackNameFillMode fillMode)
        => ExecuteCompatibilityCommand(
            file,
            new FillEmptyTrackNamesCommand(),
            new FillEmptyTrackNamesOptions(trackIndices.ToArray(), fillMode));

    public static MidiForgeTrackNameResult ClearTrackNames(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        bool preserveDrumInstrumentNames = true)
        => ExecuteCompatibilityCommand(
            file,
            new ClearTrackNamesCommand(),
            new ClearTrackNamesOptions(trackIndices.ToArray(), preserveDrumInstrumentNames));

    public static MidiForgeSetTrackProgramResult SetTrackPrograms(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSetTrackProgramOptions options)
        => ExecuteCompatibilityCommand(
            file,
            new SetTrackProgramsCommand(),
            new SetTrackProgramsCommandOptions(trackIndices.ToArray(), options));

    private static long LimitDurationToCurrentMeasureWhenNextMeasureIsEmpty(
        Note note,
        IReadOnlyCollection<Note> trackNotes,
        long newLength,
        long barDurationTicks)
        => MidiForgeNotePrimitives.LimitDurationToCurrentMeasureWhenNextMeasureIsEmpty(
            note,
            trackNotes,
            newLength,
            barDurationTicks);

    private static long GetBarDurationTicks(EditableMidiFile file)
        => MidiForgeNotePrimitives.GetBarDurationTicks(file);

    private static int TransposeChunkNotes(TrackChunk chunk, int semitones)
    {
        var changedNotes = chunk.GetNotes()
            .Count(note => Math.Clamp((byte)note.NoteNumber + semitones, 0, 127) != (byte)note.NoteNumber);

        foreach (var midiEvent in chunk.Events)
        {
            if (midiEvent is NoteEvent noteEvent)
                noteEvent.NoteNumber = (SevenBitNumber)(byte)Math.Clamp((byte)noteEvent.NoteNumber + semitones, 0, 127);
        }

        return changedNotes;
    }

    private static void SetTrackName(TrackChunk chunk, string name)
    {
        chunk.Events.RemoveAll(e => e is SequenceTrackNameEvent);
        chunk.Events.Insert(0, new SequenceTrackNameEvent(name));
    }

    private static Note CloneNoteWithNumber(Note note, int noteNumber)
        => MidiForgeNotePrimitives.CloneNoteWithNumber(note, noteNumber);

    private static TResult ExecuteCompatibilityCommand<TOptions, TResult>(
        EditableMidiFile file,
        IEditorCommand<TOptions, TResult> command,
        TOptions options)
    {
        var session = new MidiEditorSessionState { File = file };
        var execution = new EditorCommandExecutor().Execute(
            command,
            EditorCommandContext.Create(session),
            options,
            EditorCommandExecutionOptions.WithoutHistory);

        if (!execution.Succeeded)
            throw new InvalidOperationException(execution.Message);

        return execution.Result.Value;
    }

}
