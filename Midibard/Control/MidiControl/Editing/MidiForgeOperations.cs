using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
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
    private static readonly int[] GuitarToneProgramFallbacks = [29, 27, 28, 30, 31];
    private static readonly int[] GuitarToneMergeChannels = Enumerable.Range(0, 16)
        .Where(channel => channel != MidiForgeAnalysis.DrumChannel)
        .ToArray();

    public static int MaximumGuitarToneMergeTracks => GuitarToneMergeChannels.Length;

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
    {
        var pickResult = PickChordLines(
            file,
            trackIndices,
            new MidiForgePickChordLinesOptions(
                MaxSimultaneousNotes: options.MaxSimultaneousNotes,
                PickStrategy: options.PickStrategy,
                CreateNewTracks: options.CreateNewTracks,
                RenameTracks: options.RenameTracks));

        var changedNotes = 0;
        if (options.AdaptOutOfRangeNotes && pickResult.OutputTrackIndices.Count > 0)
        {
            var adaptResult = AdaptTracksToPlayableRange(
                file,
                pickResult.OutputTrackIndices,
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: false,
                    RangeStrategy: options.RangeStrategy,
                    RenameTracks: false));
            changedNotes = adaptResult.ChangedNotes;
        }

        return new MidiForgeAutoEditResult(
            pickResult.SourceTracks,
            pickResult.CreatedTracks,
            pickResult.ReplacedTracks,
            pickResult.PickedParts,
            changedNotes);
    }

    public static MidiForgePrepareForPlaybackResult PrepareForPlayback(
        EditableMidiFile file,
        MidiForgePrepareForPlaybackOptions options)
    {
        var sourceTracks = GetPerformanceTrackIndices(file).Length;
        var filledTrackNames = 0;
        var trackNameTransposeTracks = 0;
        var trackNameTransposeChangedNotes = 0;
        var drumSourceTracks = 0;
        var drumTracksCreated = 0;
        var drumSourceTracksDeleted = 0;
        var drumRestTracks = 0;
        var drumAutoEditedTracks = 0;
        var drumTransposedNotes = 0;

        if (options.FillEmptyTrackNames)
        {
            var fillResult = FillEmptyTrackNames(
                file,
                GetPerformanceTrackIndices(file),
                MidiForgeTrackNameFillMode.Ffxiv);
            filledTrackNames = fillResult.RenamedTracks;
        }

        if (options.ApplyTrackNameTransposes)
        {
            var transposedTrackIndices = GetPerformanceTrackIndices(file)
                .Where(index => TrackInfo.GetTransposeByName(file.Tracks[index].Name) != 0)
                .ToArray();
            var transposeResult = ApplyTrackNameTransposes(
                file,
                transposedTrackIndices,
                new MidiForgeApplyTrackNameTransposeOptions(CreateNewTracks: false));
            trackNameTransposeTracks = transposeResult.SourceTracks;
            trackNameTransposeChangedNotes = transposeResult.ChangedNotes;
        }

        if (options.SplitDrumkits)
        {
            var drumTrackIndices = GetDrumOnlyPerformanceTrackIndices(file);
            var drumResult = SplitDrumkitTracks(
                file,
                drumTrackIndices,
                new MidiForgeSplitDrumkitOptions(
                    AutoEditAfterSplit: true,
                    CreateRestTrack: true,
                    MoveSourceTracksToEnd: false,
                    TransposePreset: options.DrumTransposePreset,
                    DeleteOriginalTracks: true));
            drumSourceTracks = drumResult.SourceTracks;
            drumTracksCreated = drumResult.CreatedTracks;
            drumSourceTracksDeleted = drumResult.DeletedSourceTracks;
            drumRestTracks = drumResult.RestTracks;
            drumAutoEditedTracks = drumResult.AutoEditedTracks;
            drumTransposedNotes = drumResult.TransposedNotes;
        }

        var autoEditResult = AutoEditTracks(
            file,
            GetNonDrumPerformanceTrackIndices(file),
            new MidiForgeAutoEditOptions(
                MaxSimultaneousNotes: options.MaxSimultaneousNotes,
                PickStrategy: options.PickStrategy,
                AdaptOutOfRangeNotes: true,
                CreateNewTracks: false,
                RangeStrategy: options.RangeStrategy,
                RenameTracks: false));

        return new MidiForgePrepareForPlaybackResult(
            sourceTracks,
            filledTrackNames,
            trackNameTransposeTracks,
            trackNameTransposeChangedNotes,
            drumSourceTracks,
            drumTracksCreated,
            drumSourceTracksDeleted,
            drumRestTracks,
            drumAutoEditedTracks,
            drumTransposedNotes,
            autoEditResult.SourceTracks,
            autoEditResult.ReplacedTracks,
            autoEditResult.PickedParts,
            autoEditResult.ChangedNotes);
    }

    public static MidiForgeSplitDrumkitResult SplitDrumkitTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitDrumkitOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var restTracks = 0;
        var autoEditedTracks = 0;
        var transposedNotes = 0;
        var sourceTrackRefs = new List<EditableTrack>();

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var drumNotes = sourceChunk.GetNotes()
                .Where(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel)
                .ToArray();
            if (drumNotes.Length == 0)
                continue;

            var createdFromSource = 0;
            sourceTracks++;

            foreach (var mapping in MidiForgeDrumMaps.DefaultDrumkitMappings.Where(mapping => mapping.SourceNotes.Count > 0))
            {
                var mappedNotes = drumNotes
                    .Where(note => mapping.SourceNotes.Contains((byte)note.NoteNumber))
                    .Select(note =>
                    {
                        var sourceNoteNumber = (byte)note.NoteNumber;
                        var outputNoteNumber = MidiForgeDrumMaps.TransposeToOutputNote(
                            sourceNoteNumber,
                            options.TransposePreset);
                        if (outputNoteNumber != sourceNoteNumber)
                            transposedNotes++;
                        return CloneNoteWithNumber(note, outputNoteNumber);
                    })
                    .ToArray();

                if (mappedNotes.Length == 0)
                    continue;

                if (options.AutoEditAfterSplit)
                    mappedNotes = AutoEditDrumNotes(mappedNotes, mapping.TrackName, ref autoEditedTracks, ref transposedNotes);

                file.Tracks.Add(new EditableTrack(
                    CreateTrackFromNotes(sourceChunk, mapping.TrackName, mappedNotes),
                    file.Tracks.Count));
                createdTracks++;
                createdFromSource++;
            }

            if (options.CreateRestTrack)
            {
                var restNotes = drumNotes
                    .Where(note => !MidiForgeDrumMaps.IsMappedSourceNote((byte)note.NoteNumber))
                    .Select(note => CloneNoteWithNumber(note, (byte)note.NoteNumber))
                    .ToArray();

                if (restNotes.Length > 0)
                {
                    file.Tracks.Add(new EditableTrack(
                        CreateTrackFromNotes(sourceChunk, MidiForgeDrumMaps.RestTrackName, restNotes),
                        file.Tracks.Count));
                    createdTracks++;
                    createdFromSource++;
                    restTracks++;
                }
            }

            if (createdFromSource > 0)
                sourceTrackRefs.Add(track);
        }

        var deletedSourceTracks = 0;
        if (createdTracks > 0 && options.DeleteOriginalTracks)
        {
            foreach (var track in sourceTrackRefs)
            {
                var index = file.Tracks.IndexOf(track);
                if (index < 0)
                    continue;

                track.Dispose();
                file.Tracks.RemoveAt(index);
                deletedSourceTracks++;
            }
        }
        else if (createdTracks > 0 && options.MoveSourceTracksToEnd)
        {
            MoveTracksToEnd(file, sourceTrackRefs);
        }

        if (createdTracks > 0 || deletedSourceTracks > 0)
            RefreshTrackIndexesAndDirty(file);

        return new MidiForgeSplitDrumkitResult(
            sourceTracks,
            createdTracks,
            restTracks,
            autoEditedTracks,
            transposedNotes,
            deletedSourceTracks);
    }

    public static MidiForgeDisassembleDrumkitResult DisassembleDrumkitTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeDisassembleDrumkitOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var sourceTrackRefs = new List<EditableTrack>();

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var drumNotes = sourceChunk.GetNotes()
                .Where(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel)
                .ToArray();
            if (drumNotes.Length == 0)
                continue;

            var createdFromSource = 0;

            foreach (var group in drumNotes
                .GroupBy(note => (byte)note.NoteNumber)
                .OrderBy(group => group.Key))
            {
                var trackName = MidiForgeDrumMaps.GetDrumkitInstrumentName(group.Key);
                file.Tracks.Add(new EditableTrack(
                    CreateTrackFromNotes(sourceChunk, trackName, group.Select(note => CloneNoteWithNumber(note, group.Key))),
                    file.Tracks.Count));
                createdTracks++;
                createdFromSource++;
            }

            if (createdFromSource > 0)
            {
                sourceTracks++;
                sourceTrackRefs.Add(track);
            }
        }

        var deletedSourceTracks = 0;
        if (options.DeleteOriginalTracks && sourceTrackRefs.Count > 0)
        {
            foreach (var track in sourceTrackRefs)
            {
                var index = file.Tracks.IndexOf(track);
                if (index < 0)
                    continue;

                track.Dispose();
                file.Tracks.RemoveAt(index);
                deletedSourceTracks++;
            }
        }

        if (createdTracks > 0 || deletedSourceTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeDisassembleDrumkitResult(
            sourceTracks,
            createdTracks,
            deletedSourceTracks);
    }

    public static MidiForgeTransposeToDrumNoteResult TransposeSingleNoteTracksToDrumNote(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeTransposeToDrumNoteOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var targetNote = Math.Clamp(options.TargetNote, 0, 127);
        var sourceTracks = validTrackIndices.Length;
        var createdTracks = 0;
        var deletedSourceTracks = 0;
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            var uniqueNoteNumbers = notes
                .Select(note => (int)(byte)note.NoteNumber)
                .Distinct()
                .Take(2)
                .ToArray();

            if (uniqueNoteNumbers.Length != 1)
            {
                skippedTracks++;
                continue;
            }

            var transposeSemitones = targetNote - uniqueNoteNumbers[0];
            var trackName = string.IsNullOrWhiteSpace(options.TrackName)
                ? $"{track.DisplayName} (Transposed {transposeSemitones})"
                : options.TrackName.Trim();
            var transposedChunk = CreateTrackFromNotes(
                sourceChunk,
                trackName,
                notes.Select(note => CloneNoteWithNumber(note, targetNote)));

            if (options.DeleteOriginalTracks)
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(transposedChunk, trackIndex);
                deletedSourceTracks++;
            }
            else
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(transposedChunk, trackIndex + 1));
            }

            createdTracks++;
        }

        if (createdTracks > 0 || deletedSourceTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeTransposeToDrumNoteResult(
            sourceTracks,
            createdTracks,
            deletedSourceTracks,
            skippedTracks);
    }

    public static MidiForgeSplitNotesRangeResult SplitTracksByToneRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitToneRangeOptions options)
    {
        var minimumNote = Math.Clamp(options.MinimumNote, 0, 127);
        var maximumNote = Math.Clamp(options.MaximumNote, 0, 127);
        if (minimumNote > maximumNote)
            (minimumNote, maximumNote) = (maximumNote, minimumNote);

        var rangeLabel = $"{GetMidiNoteName(minimumNote)} ({minimumNote}) - {GetMidiNoteName(maximumNote)} ({maximumNote})";
        return SplitTracksByRange(
            file,
            trackIndices,
            note =>
            {
                var noteNumber = (byte)note.NoteNumber;
                return noteNumber >= minimumNote && noteNumber <= maximumNote;
            },
            trackName => $"{trackName} (In Range {rangeLabel})",
            trackName => $"{trackName} (Out of Range {rangeLabel})");
    }

    public static MidiForgeSplitNotesRangeResult SplitTracksByLengthRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitLengthRangeOptions options)
    {
        var minimumLengthTicks = Math.Max(0, options.MinimumLengthTicks);
        var maximumLengthTicks = Math.Max(0, options.MaximumLengthTicks);
        if (minimumLengthTicks > maximumLengthTicks)
            (minimumLengthTicks, maximumLengthTicks) = (maximumLengthTicks, minimumLengthTicks);

        var rangeLabel = $"{minimumLengthTicks} - {maximumLengthTicks}";
        return SplitTracksByRange(
            file,
            trackIndices,
            note => note.Length >= minimumLengthTicks && note.Length <= maximumLengthTicks,
            trackName => $"{trackName} (In Range {rangeLabel})",
            trackName => $"{trackName} (Out of Range {rangeLabel})");
    }

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
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var generatedNotes = 0;
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes()
                .OrderBy(note => note.Time)
                .ThenBy(note => (byte)note.NoteNumber)
                .ToArray();
            var pitchBends = sourceChunk.GetTimedEvents()
                .Where(timedEvent => timedEvent.Event is PitchBendEvent)
                .OrderBy(timedEvent => timedEvent.Time)
                .ToArray();

            if (notes.Length == 0 || pitchBends.Length == 0)
            {
                skippedTracks++;
                continue;
            }

            var generatedTrackNotes = new List<Note>();
            foreach (var note in notes)
                generatedTrackNotes.AddRange(GeneratePitchBendNotesForNote(note, pitchBends));

            if (generatedTrackNotes.Count == 0)
            {
                skippedTracks++;
                continue;
            }

            var generatedTrack = new EditableTrack(
                CreateTrackFromNotes(
                    sourceChunk,
                    $"{track.DisplayName} (Pitch Bend Notes)",
                    generatedTrackNotes,
                    includePitchBendEvents: false),
                0);

            sourceTracks++;
            generatedNotes += generatedTrackNotes.Count;

            if (options.DeleteOriginalTracks)
            {
                track.Dispose();
                file.Tracks[trackIndex] = generatedTrack;
                replacedTracks++;
            }
            else
            {
                file.Tracks.Insert(trackIndex + 1, generatedTrack);
                createdTracks++;
            }
        }

        if (createdTracks > 0 || replacedTracks > 0)
            RefreshTrackIndexesAndDirty(file);

        return new MidiForgeGeneratePitchBendNotesResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            generatedNotes,
            skippedTracks);
    }

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
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var sourceTracks = new List<GuitarToneMergeSource>();
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            if (sourceTracks.Count >= GuitarToneMergeChannels.Length ||
                options.ToneByTrackIndex == null ||
                !options.ToneByTrackIndex.TryGetValue(trackIndex, out var tone) ||
                !TryResolveGuitarProgramForTone(tone, out var programNumber))
            {
                skippedTracks++;
                continue;
            }

            var sourceChunk = file.Tracks[trackIndex].CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
            {
                skippedTracks++;
                continue;
            }

            sourceTracks.Add(new GuitarToneMergeSource(
                trackIndex,
                file.Tracks[trackIndex],
                sourceChunk,
                notes,
                (FourBitNumber)(byte)GuitarToneMergeChannels[sourceTracks.Count],
                programNumber));
        }

        if (sourceTracks.Count == 0)
        {
            return new MidiForgeMergeGuitarToneTracksResult(
                0,
                0,
                0,
                skippedTracks,
                0,
                0,
                0);
        }

        var mergedChunk = new TrackChunk();
        var generatedProgramChanges = 0;
        var mergedNotes = 0;
        var mergedChannelEvents = 0;
        var mergedTrackName = string.IsNullOrWhiteSpace(options.TrackName)
            ? "ProgramElectricGuitar"
            : options.TrackName.Trim();

        using (var manager = mergedChunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(mergedTrackName), 0));

            foreach (var source in sourceTracks)
            {
                manager.Objects.Add(new TimedEvent(
                    new ProgramChangeEvent(source.ProgramNumber) { Channel = source.OutputChannel },
                    0));
                generatedProgramChanges++;
            }

            foreach (var source in sourceTracks)
            {
                foreach (var note in source.Notes.OrderBy(note => note.Time).ThenBy(note => (byte)note.NoteNumber))
                {
                    manager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = source.OutputChannel },
                        note.Time));
                    manager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = source.OutputChannel },
                        note.EndTime));
                    mergedNotes++;
                }

                foreach (var timedEvent in source.Chunk.GetTimedEvents()
                    .Where(timedEvent => ShouldMergeGuitarToneChannelEvent(timedEvent.Event, options)))
                {
                    var channelEvent = (ChannelEvent)timedEvent.Event.Clone();
                    channelEvent.Channel = source.OutputChannel;
                    manager.Objects.Add(new TimedEvent(channelEvent, timedEvent.Time));
                    mergedChannelEvents++;
                }
            }
        }

        var mergedTrack = new EditableTrack(mergedChunk, 0);
        var deletedSourceTracks = 0;

        if (options.DeleteOriginalTracks)
        {
            var insertIndex = sourceTracks.Min(source => source.TrackIndex);
            foreach (var source in sourceTracks.OrderByDescending(source => source.TrackIndex))
            {
                source.Track.Dispose();
                file.Tracks.RemoveAt(source.TrackIndex);
                deletedSourceTracks++;
            }

            file.Tracks.Insert(insertIndex, mergedTrack);
        }
        else
        {
            file.Tracks.Insert(sourceTracks.Max(source => source.TrackIndex) + 1, mergedTrack);
        }

        RefreshTrackIndexesAndDirty(file);

        return new MidiForgeMergeGuitarToneTracksResult(
            sourceTracks.Count,
            1,
            deletedSourceTracks,
            skippedTracks,
            generatedProgramChanges,
            mergedNotes,
            mergedChannelEvents);
    }

    public static bool TryResolveGuitarToneFromTrackName(string trackName, out int tone)
    {
        var instrumentId = TrackInfo.GetInstrumentIdByName(trackName);
        return TryResolveGuitarToneFromInstrumentId(instrumentId, out tone);
    }

    public static bool TryResolveGuitarToneFromProgram(SevenBitNumber programNumber, out int tone)
    {
        if (InstrumentHelper.ProgramInstruments != null &&
            InstrumentHelper.ProgramInstruments.TryGetValue(programNumber, out var instrumentId) &&
            TryResolveGuitarToneFromInstrumentId(instrumentId, out tone))
        {
            return true;
        }

        for (var i = 0; i < GuitarToneProgramFallbacks.Length; i++)
        {
            if ((byte)programNumber == GuitarToneProgramFallbacks[i])
            {
                tone = i;
                return true;
            }
        }

        tone = 0;
        return false;
    }

    public static bool TryResolveGuitarToneFromInstrumentId(uint? instrumentId, out int tone)
    {
        tone = instrumentId switch
        {
            24 => 0,
            25 => 1,
            26 => 2,
            27 => 3,
            28 => 4,
            _ => -1,
        };

        return tone >= 0;
    }

    public static bool TryResolveGuitarProgramForTone(int tone, out SevenBitNumber programNumber)
    {
        tone = Math.Clamp(tone, 0, GuitarToneProgramFallbacks.Length - 1);
        var instrumentId = 24 + tone;

        if (InstrumentHelper.Instruments != null &&
            instrumentId >= 0 &&
            instrumentId < InstrumentHelper.Instruments.Length)
        {
            programNumber = InstrumentHelper.Instruments[instrumentId].ProgramNumber;
            return true;
        }

        programNumber = (SevenBitNumber)(byte)GuitarToneProgramFallbacks[tone];
        return true;
    }

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

    private static MidiForgeSplitNotesRangeResult SplitTracksByRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        Func<Note, bool> isInRange,
        Func<string, string> getInRangeTrackName,
        Func<string, string> getOutOfRangeTrackName)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var inRangeTracks = 0;
        var outOfRangeTracks = 0;
        var inRangeNotesTotal = 0;
        var outOfRangeNotesTotal = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var inRangeNotes = notes
                .Where(isInRange)
                .Select(note => CloneNoteWithLength(note, note.Length))
                .ToArray();
            var outOfRangeNotes = notes
                .Where(note => !isInRange(note))
                .Select(note => CloneNoteWithLength(note, note.Length))
                .ToArray();

            sourceTracks++;

            if (outOfRangeNotes.Length > 0)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                    CreateTrackFromNotes(sourceChunk, getOutOfRangeTrackName(track.DisplayName), outOfRangeNotes),
                    trackIndex + 1));
                createdTracks++;
                outOfRangeTracks++;
                outOfRangeNotesTotal += outOfRangeNotes.Length;
            }

            if (inRangeNotes.Length > 0)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                    CreateTrackFromNotes(sourceChunk, getInRangeTrackName(track.DisplayName), inRangeNotes),
                    trackIndex + 1));
                createdTracks++;
                inRangeTracks++;
                inRangeNotesTotal += inRangeNotes.Length;
            }
        }

        if (createdTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeSplitNotesRangeResult(
            sourceTracks,
            createdTracks,
            inRangeTracks,
            outOfRangeTracks,
            inRangeNotesTotal,
            outOfRangeNotesTotal);
    }

    private static void RefreshTrackIndexesAndDirty(EditableMidiFile file)
    {
        for (int i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
        file.MarkChanged();
    }

    private static bool ShouldMergeGuitarToneChannelEvent(
        MidiEvent midiEvent,
        MidiForgeMergeGuitarToneTracksOptions options)
        => midiEvent switch
        {
            PitchBendEvent => options.IncludePitchBendEvents,
            ControlChangeEvent => options.IncludeControlChangeEvents,
            _ => false,
        };

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

    private static Note[] AutoEditDrumNotes(
        Note[] notes,
        string trackName,
        ref int autoEditedTracks,
        ref int transposedNotes)
    {
        var pickedNotes = MidiForgeNotePrimitives.SplitChordNotes(
            notes,
            trackName,
            MidiForgeChordSplitStrategy.SameStartTick,
            MidiForgeChordGroupMode.GroupMerged,
            2)
            .Where(group => !group.IsChord || group.Order == 1)
            .SelectMany(group => group.Notes)
            .ToArray();

        var changed = pickedNotes.Length != notes.Length;

        if (pickedNotes.Count(note => (byte)note.NoteNumber == MidiForgeAnalysis.PlayableLowestMidiNote) > pickedNotes.Length / 2)
        {
            pickedNotes = pickedNotes
                .Select(note => CloneNoteWithNumber(note, Math.Clamp((byte)note.NoteNumber + 4, 0, 127)))
                .ToArray();
            transposedNotes += pickedNotes.Length;
            changed = true;
        }

        if (changed)
            autoEditedTracks++;

        return pickedNotes;
    }

    private static Note CloneNoteWithNumber(Note note, int noteNumber)
        => new(
            (SevenBitNumber)(byte)Math.Clamp(noteNumber, 0, 127),
            note.Length,
            note.Time)
        {
            Channel = note.Channel,
            Velocity = note.Velocity,
            OffVelocity = note.OffVelocity,
        };

    private static Note CloneNoteWithLength(Note note, long length)
        => MidiForgeNotePrimitives.CloneNoteWithLength(note, length);

    private static IEnumerable<Note> GeneratePitchBendNotesForNote(
        Note note,
        IReadOnlyList<TimedEvent> pitchBends)
    {
        var noteStartTick = note.Time;
        var noteEndTick = note.EndTime;
        var notePitchBends = pitchBends
            .Where(timedEvent => timedEvent.Event is PitchBendEvent bend && bend.Channel == note.Channel)
            .OrderBy(timedEvent => timedEvent.Time)
            .ToArray();

        if (notePitchBends.Length == 0)
            return [CloneNoteWithLength(note, note.Length)];

        var firstNotePitchBendEvent = notePitchBends.LastOrDefault(timedEvent => timedEvent.Time <= noteStartTick);
        var pitchBendsDuringNote = notePitchBends
            .Where(timedEvent =>
                (timedEvent.Time > noteStartTick && timedEvent.Time <= noteEndTick) ||
                ReferenceEquals(timedEvent, firstNotePitchBendEvent))
            .OrderBy(timedEvent => timedEvent.Time)
            .ToArray();

        var uniquePitchBends = new List<TimedEvent>();
        foreach (var pitchBend in pitchBendsDuringNote)
        {
            if (uniquePitchBends.Count == 0 ||
                GetPitchBendSemitones((PitchBendEvent)pitchBend.Event) !=
                GetPitchBendSemitones((PitchBendEvent)uniquePitchBends[^1].Event))
            {
                uniquePitchBends.Add(pitchBend);
            }
        }

        if (uniquePitchBends.Count == 0)
            return [CloneNoteWithLength(note, note.Length)];

        var generatedNotes = new List<Note>();
        for (int i = 0; i < uniquePitchBends.Count; i++)
        {
            var pitchBend = uniquePitchBends[i];
            if (i == 0 && pitchBend.Time > noteStartTick)
                AddPitchBendGeneratedNote(generatedNotes, note, (byte)note.NoteNumber, noteStartTick, pitchBend.Time);

            var semitones = GetPitchBendSemitones((PitchBendEvent)pitchBend.Event);
            var noteNumber = Math.Clamp((byte)note.NoteNumber + semitones, 0, 127);
            var segmentStartTick = i == 0 && pitchBend.Time <= noteStartTick
                ? noteStartTick
                : pitchBend.Time;
            var segmentEndTick = i == uniquePitchBends.Count - 1
                ? noteEndTick
                : uniquePitchBends[i + 1].Time;

            AddPitchBendGeneratedNote(generatedNotes, note, noteNumber, segmentStartTick, segmentEndTick);
        }

        return generatedNotes;
    }

    private static void AddPitchBendGeneratedNote(
        ICollection<Note> notes,
        Note sourceNote,
        int noteNumber,
        long startTick,
        long endTick)
    {
        var length = endTick - startTick;
        if (length <= 0) return;

        var note = CloneNoteWithNumber(sourceNote, noteNumber);
        note.Time = Math.Max(0, startTick);
        note.Length = length;
        notes.Add(note);
    }

    private static int GetPitchBendSemitones(PitchBendEvent pitchBend)
        => pitchBend.PitchValue switch
        {
            < 4096 => -2,
            < 8192 => -1,
            < 12288 => 0,
            < 16383 => 1,
            _ => 2,
        };

    private static string GetMidiNoteName(int noteNumber)
    {
        var noteNames = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var clampedNoteNumber = Math.Clamp(noteNumber, 0, 127);
        return $"{noteNames[clampedNoteNumber % 12]}{clampedNoteNumber / 12 - 1}";
    }

    private static int[] GetPerformanceTrackIndices(EditableMidiFile file)
        => file.Tracks
            .Select((track, index) => (track, index))
            .Where(item => !item.track.IsConductorTrack)
            .Select(item => item.index)
            .ToArray();

    private static int[] GetDrumOnlyPerformanceTrackIndices(EditableMidiFile file)
        => file.Tracks
            .Select((track, index) => (track, index))
            .Where(item => !item.track.IsConductorTrack && IsDrumOnlyTrack(item.track))
            .Select(item => item.index)
            .ToArray();

    private static int[] GetNonDrumPerformanceTrackIndices(EditableMidiFile file)
        => file.Tracks
            .Select((track, index) => (track, index))
            .Where(item => !item.track.IsConductorTrack && !HasDrumNotes(item.track))
            .Select(item => item.index)
            .ToArray();

    private static bool IsDrumOnlyTrack(EditableTrack track)
    {
        var notes = track.CloneCurrentChunk().GetNotes().ToArray();
        return notes.Length > 0 && notes.All(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel);
    }

    private static bool HasDrumNotes(EditableTrack track)
        => track.CloneCurrentChunk()
            .GetNotes()
            .Any(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel);

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

    private static void MoveTracksToEnd(EditableMidiFile file, IEnumerable<EditableTrack> tracks)
    {
        foreach (var track in tracks)
        {
            var index = file.Tracks.IndexOf(track);
            if (index < 0)
                continue;

            file.Tracks.RemoveAt(index);
            file.Tracks.Add(track);
        }
    }

    private static TrackChunk CreateTrackFromNotes(
        TrackChunk sourceChunk,
        string trackName,
        IEnumerable<Note> notes,
        bool includePitchBendEvents = true)
        => MidiForgeNotePrimitives.CreateTrackFromNotes(
            sourceChunk,
            trackName,
            notes,
            includePitchBendEvents);

    private sealed record GuitarToneMergeSource(
        int TrackIndex,
        EditableTrack Track,
        TrackChunk Chunk,
        Note[] Notes,
        FourBitNumber OutputChannel,
        SevenBitNumber ProgramNumber);
}
