using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeAdaptToRangeOptions(
    bool CreateNewTracks = true,
    bool SmartTranspose = true,
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

public sealed record MidiForgeAutoEditOptions(
    int MaxSimultaneousNotes = 1,
    MidiForgeChordPickStrategy PickStrategy = MidiForgeChordPickStrategy.HighestChords,
    bool AdaptOutOfRangeNotes = true,
    bool CreateNewTracks = true);

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
    MidiForgeDrumTransposePreset TransposePreset = MidiForgeDrumTransposePreset.Default);

public sealed record MidiForgeSplitDrumkitResult(
    int SourceTracks,
    int CreatedTracks,
    int RestTracks,
    int AutoEditedTracks,
    int TransposedNotes);

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

public static class MidiForgeOperations
{
    private static readonly HashSet<string> PreservedDrumTrackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BassDrum",
        "SnareDrum",
        "Cymbal",
        "Bongo",
        "Timpani",
        "Drumkit",
    };

    public static MidiForgeAdaptToRangeResult AdaptTracksToPlayableRange(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeAdaptToRangeOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var octaveShiftedTracks = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            sourceTracks++;

            var octaveShift = options.SmartTranspose
                ? MidiForgeAnalysis.GetOptimalTransposeAmount(notes.Select(note => (int)(byte)note.NoteNumber))
                : 0;
            if (octaveShift != 0)
                octaveShiftedTracks++;

            var adaptedChunk = new TrackChunk(sourceChunk.Events.Select(e => e.Clone()));
            var changedNotesInTrack = AdaptChunkNoteNumbers(adaptedChunk, octaveShift);
            changedNotes += changedNotesInTrack;

            if (options.RenameTracks)
                SetTrackName(adaptedChunk, $"{track.DisplayName} (Adapted {changedNotesInTrack} notes)");

            if (options.CreateNewTracks)
            {
                var newTrack = new EditableTrack(adaptedChunk, trackIndex + 1);
                file.Tracks.Insert(trackIndex + 1, newTrack);
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(adaptedChunk, trackIndex);
                replacedTracks++;
            }
        }

        if (createdTracks > 0 || replacedTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeAdaptToRangeResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            octaveShiftedTracks,
            changedNotes);
    }

    public static int AdaptMidiNoteToPlayableRange(int midiNote)
        => TrackInfo.TranslateNoteNumber(midiNote, adaptOOR: true) + MidiForgeAnalysis.PlayableLowestMidiNote;

    public static MidiForgeSplitChordsResult SplitTracksChords(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitChordsOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var chordGroups = 0;
        var minimumSimultaneousNotes = Math.Clamp(options.MinimumSimultaneousNotes, 2, 10);

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var splitGroups = SplitChordNotes(
                notes,
                track.DisplayName,
                options.Strategy,
                options.GroupMode,
                minimumSimultaneousNotes)
                .ToArray();
            if (splitGroups.Length == 0)
                continue;

            sourceTracks++;
            chordGroups += splitGroups.Count(group => group.IsChord);

            var splitTracks = splitGroups
                .Select(group => CreateTrackFromNotes(sourceChunk, group.TrackName, group.Notes))
                .Select(chunk => new EditableTrack(chunk, 0))
                .ToArray();

            if (options.InsertPartsAtEnd)
            {
                foreach (var splitTrack in splitTracks)
                    file.Tracks.Insert(file.Tracks.Count, splitTrack);
            }
            else
            {
                foreach (var splitTrack in splitTracks.Reverse())
                    file.Tracks.Insert(trackIndex + 1, splitTrack);
            }

            createdTracks += splitTracks.Length;
        }

        if (createdTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeSplitChordsResult(sourceTracks, createdTracks, chordGroups);
    }

    public static MidiForgeAutoEditResult AutoEditTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeAutoEditOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var pickedParts = 0;
        var changedNotes = 0;
        var maxSimultaneousNotes = Math.Clamp(options.MaxSimultaneousNotes, 1, 3);

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var splitGroups = SplitChordNotes(
                notes,
                track.DisplayName,
                MidiForgeChordSplitStrategy.SameStartTick,
                MidiForgeChordGroupMode.GroupMerged,
                2)
                .Where(group => ShouldPickAutoEditGroup(group, maxSimultaneousNotes, options.PickStrategy))
                .ToArray();
            if (splitGroups.Length == 0)
                continue;

            sourceTracks++;
            pickedParts += splitGroups.Length;

            var autoEditTrackName = $"{track.DisplayName} (Auto Edited Max {maxSimultaneousNotes})";
            var autoEditChunk = CreateTrackFromNotes(
                sourceChunk,
                autoEditTrackName,
                splitGroups.SelectMany(group => group.Notes));

            if (options.AdaptOutOfRangeNotes)
                changedNotes += AdaptChunkNoteNumbers(autoEditChunk, 0);

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(autoEditChunk, trackIndex + 1));
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(autoEditChunk, trackIndex);
                replacedTracks++;
            }
        }

        if (createdTracks > 0 || replacedTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeAutoEditResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            pickedParts,
            changedNotes);
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

        if (createdTracks > 0 && options.MoveSourceTracksToEnd)
            MoveTracksToEnd(file, sourceTrackRefs);

        if (createdTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeSplitDrumkitResult(
            sourceTracks,
            createdTracks,
            restTracks,
            autoEditedTracks,
            transposedNotes);
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

    public static MidiForgeSplitOverlappedNotesResult SplitTracksOverlappedNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var overlapGroups = 0;
        var overlappedNotes = 0;
        var nonOverlappedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var groups = notes
                .GroupBy(note => ((int)(byte)note.NoteNumber, note.Time))
                .OrderBy(group => group.Key.Time)
                .ThenBy(group => group.Key.Item1)
                .ToArray();
            var duplicateGroups = groups
                .Where(group => group.Count() >= 2)
                .ToArray();
            if (duplicateGroups.Length == 0)
                continue;

            var trackGroups = new Dictionary<string, List<Note>>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var groupNotes = group.ToArray();
                var isOverlapped = groupNotes.Length >= 2;

                for (int i = 0; i < groupNotes.Length; i++)
                {
                    var trackName = isOverlapped
                        ? $"{track.DisplayName} overlap ({i + 1})"
                        : $"{track.DisplayName} no overlap";

                    if (!trackGroups.TryGetValue(trackName, out var splitNotes))
                    {
                        splitNotes = new List<Note>();
                        trackGroups.Add(trackName, splitNotes);
                    }

                    splitNotes.Add(CloneNoteWithLength(groupNotes[i], groupNotes[i].Length));
                }
            }

            foreach (var (trackName, splitNotes) in trackGroups
                .OrderBy(pair => pair.Key.Contains(" no overlap", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (splitNotes.Count == 0)
                    continue;

                file.Tracks.Add(new EditableTrack(
                    CreateTrackFromNotes(sourceChunk, trackName, splitNotes),
                    file.Tracks.Count));
                createdTracks++;
            }

            sourceTracks++;
            overlapGroups += duplicateGroups.Length;
            overlappedNotes += duplicateGroups.Sum(group => group.Count());
            nonOverlappedNotes += groups.Where(group => group.Count() == 1).Sum(group => group.Count());
        }

        if (createdTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeSplitOverlappedNotesResult(
            sourceTracks,
            createdTracks,
            overlapGroups,
            overlappedNotes,
            nonOverlappedNotes);
    }

    public static MidiForgeTrimOverlappedNotesResult TrimOverlappedSustainedNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var changedNotesInTrack = 0;
            var trimmedNotes = notes
                .Select(note =>
                {
                    var overlapStart = notes
                        .Where(other => other.Time != note.Time)
                        .Where(other => NotesOverlap(note, other))
                        .Where(other => other.Time > note.Time)
                        .Select(other => other.Time)
                        .OrderBy(time => time)
                        .Cast<long?>()
                        .FirstOrDefault();

                    if (overlapStart == null)
                        return CloneNoteWithLength(note, note.Length);

                    var newLength = Math.Max(1, overlapStart.Value - note.Time);
                    if (newLength == note.Length)
                        return CloneNoteWithLength(note, note.Length);

                    changedNotesInTrack++;
                    return CloneNoteWithLength(note, newLength);
                })
                .ToArray();

            if (changedNotesInTrack == 0)
                continue;

            file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                CreateTrackFromNotes(sourceChunk, $"{track.DisplayName} (Trimmed)", trimmedNotes),
                trackIndex + 1));
            sourceTracks++;
            createdTracks++;
            changedNotes += changedNotesInTrack;
        }

        if (createdTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeTrimOverlappedNotesResult(sourceTracks, createdTracks, changedNotes);
    }

    public static MidiForgeExtendNotesDurationResult ExtendNotesDuration(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeExtendNotesDurationOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var maximumDurationTicks = Math.Max(0, options.MaximumDurationTicks);
        var barDurationTicks = GetBarDurationTicks(file);
        var sourceTracks = 0;
        var createdTracks = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes()
                .OrderBy(note => note.Time)
                .ThenBy(note => (byte)note.NoteNumber)
                .ToArray();
            if (notes.Length == 0)
                continue;

            var changedNotesInTrack = 0;
            var extendedNotes = notes
                .Select(note =>
                {
                    var noteEndTime = note.Time + note.Length;
                    var nextNote = notes.FirstOrDefault(other => other.Time >= noteEndTime);
                    if (nextNote == null)
                        return CloneNoteWithLength(note, note.Length);

                    var newLength = nextNote.Time - note.Time;
                    if (options.RespectEmptyMeasures)
                        newLength = LimitDurationToCurrentMeasureWhenNextMeasureIsEmpty(
                            note,
                            notes,
                            newLength,
                            barDurationTicks);

                    if (maximumDurationTicks > 0 && newLength > maximumDurationTicks)
                        newLength = maximumDurationTicks;

                    newLength = Math.Max(1, newLength);
                    if (newLength == note.Length)
                        return CloneNoteWithLength(note, note.Length);

                    changedNotesInTrack++;
                    return CloneNoteWithLength(note, newLength);
                })
                .ToArray();

            if (changedNotesInTrack == 0)
                continue;

            file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                CreateTrackFromNotes(sourceChunk, $"{track.DisplayName} (Extended)", extendedNotes),
                trackIndex + 1));
            sourceTracks++;
            createdTracks++;
            changedNotes += changedNotesInTrack;
        }

        if (createdTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeExtendNotesDurationResult(sourceTracks, createdTracks, changedNotes);
    }

    public static MidiForgeSplitEqualNotesResult SplitTracksEqualNotes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        int targetTrackIndex)
    {
        var validTrackIndices = GetValidComparisonTrackIndices(file, trackIndices);
        if (validTrackIndices.Length < 2 || !validTrackIndices.Contains(targetTrackIndex))
            return new MidiForgeSplitEqualNotesResult(validTrackIndices.Length, 0, 0, 0);

        var targetTrack = file.Tracks[targetTrackIndex];
        var sourceChunk = targetTrack.CloneCurrentChunk();
        var targetNotes = sourceChunk.GetNotes().ToArray();
        var comparisonNotes = validTrackIndices
            .Where(index => index != targetTrackIndex)
            .SelectMany(index => file.Tracks[index].CloneCurrentChunk().GetNotes())
            .ToArray();

        if (targetNotes.Length == 0 || comparisonNotes.Length == 0)
            return new MidiForgeSplitEqualNotesResult(validTrackIndices.Length, 0, 0, 0);

        var equalNotes = targetNotes
            .Where(note => comparisonNotes.Any(comparison => IsEqualNoteAtStart(note, comparison)))
            .Select(note => CloneNoteWithLength(note, note.Length))
            .ToArray();
        var nonEqualNotes = targetNotes
            .Where(note => !comparisonNotes.Any(comparison => IsEqualNoteAtStart(note, comparison)))
            .Select(note => CloneNoteWithLength(note, note.Length))
            .ToArray();

        var createdTracks = 0;
        createdTracks += InsertDerivedTrackAfterTarget(
            file,
            targetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Equal Notes)",
            equalNotes);
        createdTracks += InsertDerivedTrackAfterTarget(
            file,
            targetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Non Equal Notes)",
            nonEqualNotes);

        if (createdTracks > 0)
            RefreshTrackIndexesAndDirty(file);

        return new MidiForgeSplitEqualNotesResult(
            validTrackIndices.Length,
            createdTracks,
            equalNotes.Length,
            nonEqualNotes.Length);
    }

    public static MidiForgeDifferenceTracksResult DifferenceTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        int targetTrackIndex)
    {
        var validTrackIndices = GetValidComparisonTrackIndices(file, trackIndices);
        if (validTrackIndices.Length < 2 || !validTrackIndices.Contains(targetTrackIndex))
            return new MidiForgeDifferenceTracksResult(validTrackIndices.Length, 0, 0, 0);

        var targetTrack = file.Tracks[targetTrackIndex];
        var sourceChunk = targetTrack.CloneCurrentChunk();
        var targetNotes = sourceChunk.GetNotes().ToArray();
        var comparisonNotes = validTrackIndices
            .Where(index => index != targetTrackIndex)
            .SelectMany(index => file.Tracks[index].CloneCurrentChunk().GetNotes())
            .ToArray();

        if (targetNotes.Length == 0 || comparisonNotes.Length == 0)
            return new MidiForgeDifferenceTracksResult(validTrackIndices.Length, 0, 0, 0);

        var diffNotes = targetNotes
            .Where(note => !comparisonNotes.Any(comparison => NotesOverlap(note, comparison)))
            .Select(note => CloneNoteWithLength(note, note.Length))
            .ToArray();
        var restNotes = targetNotes
            .Where(note => comparisonNotes.Any(comparison => NotesOverlap(note, comparison)))
            .Select(note => CloneNoteWithLength(note, note.Length))
            .ToArray();

        var createdTracks = 0;
        createdTracks += InsertDerivedTrackAfterTarget(
            file,
            targetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Diff Rest)",
            restNotes);
        createdTracks += InsertDerivedTrackAfterTarget(
            file,
            targetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Diff)",
            diffNotes);

        if (createdTracks > 0)
            RefreshTrackIndexesAndDirty(file);

        return new MidiForgeDifferenceTracksResult(
            validTrackIndices.Length,
            createdTracks,
            diffNotes.Length,
            restNotes.Length);
    }

    public static MidiForgeSplitNotesIntoTracksResult SplitNotesIntoTracks(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSplitNotesIntoTracksOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();
        var numberOfTracks = Math.Clamp(options.NumberOfTracks, 1, 64);
        var everyNotesAmount = Math.Max(1, options.EveryNotesAmount);
        var sourceTracks = 0;
        var createdTracks = 0;
        var distributedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes()
                .OrderBy(note => note.Time)
                .ThenBy(note => (byte)note.NoteNumber)
                .ToArray();
            if (notes.Length == 0)
                continue;

            var splitNotes = Enumerable.Range(0, numberOfTracks)
                .Select(_ => new List<Note>())
                .ToArray();
            var destinationTrackIndex = 0;
            var noteCountInDestination = 0;

            foreach (var note in notes)
            {
                if (destinationTrackIndex >= numberOfTracks)
                    destinationTrackIndex = 0;

                splitNotes[destinationTrackIndex].Add(CloneNoteWithLength(note, note.Length));
                noteCountInDestination++;

                if (noteCountInDestination == everyNotesAmount)
                    noteCountInDestination = 0;

                if (noteCountInDestination == 0)
                    destinationTrackIndex++;
            }

            var newTracks = splitNotes
                .Select((notesGroup, index) => (notesGroup, index))
                .Where(group => group.notesGroup.Count > 0)
                .Select(group => new EditableTrack(
                    CreateTrackFromNotes(sourceChunk, $"{track.DisplayName} (Group {group.index + 1})", group.notesGroup),
                    0))
                .ToList();

            if (newTracks.Count == 0)
                continue;

            file.Tracks.InsertRange(trackIndex + 1, newTracks);
            sourceTracks++;
            createdTracks += newTracks.Count;
            distributedNotes += notes.Length;
        }

        if (createdTracks > 0)
            RefreshTrackIndexesAndDirty(file);

        return new MidiForgeSplitNotesIntoTracksResult(
            sourceTracks,
            createdTracks,
            distributedNotes);
    }

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
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var minimumLengthTicks = Math.Max(0, options.MinimumLengthTicks);
        var maximumLengthTicks = Math.Max(0, options.MaximumLengthTicks);
        if (minimumLengthTicks > maximumLengthTicks)
            (minimumLengthTicks, maximumLengthTicks) = (maximumLengthTicks, minimumLengthTicks);
        var newLengthTicks = Math.Max(1, options.NewLengthTicks);

        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var changedNotesInTrack = 0;
            var modifiedNotes = notes
                .Select(note =>
                {
                    if (note.Length < minimumLengthTicks || note.Length > maximumLengthTicks)
                        return CloneNoteWithLength(note, note.Length);

                    changedNotesInTrack++;
                    return CloneNoteWithLength(note, newLengthTicks);
                })
                .ToArray();

            if (changedNotesInTrack == 0)
                continue;

            sourceTracks++;
            changedNotes += changedNotesInTrack;

            var changedChunk = CreateTrackFromNotes(
                sourceChunk,
                $"{track.DisplayName} (Changed {changedNotesInTrack} notes)",
                modifiedNotes);

            if (options.DeleteOriginalTracks)
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(changedChunk, trackIndex);
                replacedTracks++;
            }
            else
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(changedChunk, trackIndex + 1));
                createdTracks++;
            }
        }

        if (createdTracks > 0 || replacedTracks > 0)
        {
            for (int i = 0; i < file.Tracks.Count; i++)
                file.Tracks[i].Index = i;
            file.MarkChanged();
        }

        return new MidiForgeChangeNoteLengthResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            changedNotes);
    }

    public static MidiForgeApplyTrackNameTransposeResult ApplyTrackNameTransposes(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeApplyTrackNameTransposeOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var cleanedTrackNames = 0;
        var changedNotes = 0;
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var transposeSemitones = TrackInfo.GetTransposeByName(track.Name);
            if (transposeSemitones == 0)
            {
                skippedTracks++;
                continue;
            }

            var migratedChunk = new TrackChunk(track.CloneCurrentChunk().Events.Select(e => e.Clone()));
            var cleanedTrackName = TrackInfo.RemoveTransposeFromTrackName(track.Name);
            changedNotes += TransposeChunkNotes(migratedChunk, transposeSemitones);
            SetTrackName(migratedChunk, cleanedTrackName);

            if (!string.Equals(track.Name, cleanedTrackName, StringComparison.Ordinal))
                cleanedTrackNames++;

            sourceTracks++;

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(migratedChunk, trackIndex + 1));
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(migratedChunk, trackIndex);
                replacedTracks++;
            }
        }

        if (createdTracks > 0 || replacedTracks > 0)
            RefreshTrackIndexesAndDirty(file);

        return new MidiForgeApplyTrackNameTransposeResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            cleanedTrackNames,
            changedNotes,
            skippedTracks);
    }

    public static MidiForgeTrackNameResult FillEmptyTrackNames(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeTrackNameFillMode fillMode)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var renamedTracks = 0;

        foreach (var (trackIndex, fallbackIndex) in validTrackIndices.Select((index, order) => (index, order + 1)))
        {
            var track = file.Tracks[trackIndex];
            if (!string.IsNullOrWhiteSpace(track.Name))
                continue;

            var defaultName = GetDefaultTrackName(track, fillMode, fallbackIndex);
            if (SetEditableTrackName(track, defaultName))
                renamedTracks++;
        }

        if (renamedTracks > 0)
            file.MarkChanged();

        return new MidiForgeTrackNameResult(validTrackIndices.Length, renamedTracks);
    }

    public static MidiForgeTrackNameResult ClearTrackNames(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        bool preserveDrumInstrumentNames = true)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var renamedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            if (string.IsNullOrWhiteSpace(track.Name))
                continue;

            if (preserveDrumInstrumentNames && PreservedDrumTrackNames.Contains(track.Name))
                continue;

            if (SetEditableTrackName(track, string.Empty))
                renamedTracks++;
        }

        if (renamedTracks > 0)
            file.MarkChanged();

        return new MidiForgeTrackNameResult(validTrackIndices.Length, renamedTracks);
    }

    public static MidiForgeSetTrackProgramResult SetTrackPrograms(
        EditableMidiFile file,
        IEnumerable<int> trackIndices,
        MidiForgeSetTrackProgramOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var programNumber = (SevenBitNumber)(byte)Math.Clamp(options.ProgramNumber, 0, 127);
        var changedTracks = 0;
        var addedProgramChanges = 0;
        var updatedProgramChanges = 0;
        var renamedTracks = 0;

        foreach (var (trackIndex, fallbackIndex) in validTrackIndices.Select((index, order) => (index, order + 1)))
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var trackChanged = false;

            using (var manager = sourceChunk.ManageTimedEvents())
            {
                var timedProgramChanges = manager.Objects
                    .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
                    .OrderBy(timedEvent => timedEvent.Time)
                    .ToArray();

                if (timedProgramChanges.Length == 0)
                {
                    manager.Objects.Add(new TimedEvent(
                        new ProgramChangeEvent(programNumber)
                        {
                            Channel = (FourBitNumber)(byte)Math.Clamp(track.Channel, 0, 15),
                        },
                        0));
                    addedProgramChanges++;
                    trackChanged = true;
                }
                else
                {
                    var changesToUpdate = options.ReplaceAllProgramChanges
                        ? timedProgramChanges
                        : timedProgramChanges.Take(1);

                    foreach (var timedProgramChange in changesToUpdate)
                    {
                        var programChange = (ProgramChangeEvent)timedProgramChange.Event;
                        if (programChange.ProgramNumber == programNumber)
                            continue;

                        programChange.ProgramNumber = programNumber;
                        updatedProgramChanges++;
                        trackChanged = true;
                    }
                }
            }

            var replacementTrack = trackChanged
                ? new EditableTrack(sourceChunk, trackIndex)
                : track;

            if (options.RenameTracks)
            {
                var trackName = MidiForgeTrackNaming.GetTrackNameForProgram(
                    programNumber,
                    options.RenameMode,
                    fallbackIndex);
                if (SetEditableTrackName(replacementTrack, trackName))
                {
                    renamedTracks++;
                    trackChanged = true;
                }
            }

            if (!trackChanged)
                continue;

            if (!ReferenceEquals(replacementTrack, track))
            {
                track.Dispose();
                file.Tracks[trackIndex] = replacementTrack;
            }

            changedTracks++;
        }

        if (changedTracks > 0)
            file.MarkChanged();

        return new MidiForgeSetTrackProgramResult(
            validTrackIndices.Length,
            changedTracks,
            addedProgramChanges,
            updatedProgramChanges,
            renamedTracks);
    }

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

    private static int[] GetValidComparisonTrackIndices(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
        => trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

    private static int InsertDerivedTrackAfterTarget(
        EditableMidiFile file,
        int targetTrackIndex,
        TrackChunk sourceChunk,
        string trackName,
        IReadOnlyCollection<Note> notes)
    {
        if (notes.Count == 0)
            return 0;

        file.Tracks.Insert(targetTrackIndex + 1, new EditableTrack(
            CreateTrackFromNotes(sourceChunk, trackName, notes),
            targetTrackIndex + 1));
        return 1;
    }

    private static void RefreshTrackIndexesAndDirty(EditableMidiFile file)
    {
        for (int i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
        file.MarkChanged();
    }

    private static bool IsEqualNoteAtStart(Note note, Note other)
        => note.Time == other.Time && (byte)note.NoteNumber == (byte)other.NoteNumber;

    private static bool NotesOverlap(Note note, Note other)
    {
        var noteStart = note.Time;
        var noteEnd = note.Time + note.Length;
        var otherStart = other.Time;
        var otherEnd = other.Time + other.Length;

        return otherEnd > noteStart && otherStart < noteEnd;
    }

    private static long LimitDurationToCurrentMeasureWhenNextMeasureIsEmpty(
        Note note,
        IReadOnlyCollection<Note> trackNotes,
        long newLength,
        long barDurationTicks)
    {
        if (barDurationTicks <= 0)
            return newLength;

        var noteMeasureIndex = note.Time / barDurationTicks;
        var currentMeasureEnd = (noteMeasureIndex + 1) * barDurationTicks;
        var nextMeasureEnd = currentMeasureEnd + barDurationTicks;
        if (note.Time + newLength <= currentMeasureEnd)
            return newLength;

        var nextMeasureHasNotes = trackNotes.Any(other =>
            other.Time >= currentMeasureEnd && other.Time < nextMeasureEnd);
        if (nextMeasureHasNotes)
            return newLength;

        return Math.Max(1, currentMeasureEnd - note.Time);
    }

    private static long GetBarDurationTicks(EditableMidiFile file)
    {
        var ticksPerQuarter = file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision timeDivision
            ? timeDivision.TicksPerQuarterNote
            : 480;

        return ticksPerQuarter * 4L;
    }

    private static int AdaptChunkNoteNumbers(TrackChunk chunk, int octaveShift)
    {
        var changedNotes = chunk.GetNotes()
            .Count(note => AdaptMidiNoteToPlayableRange((byte)note.NoteNumber + octaveShift) != (byte)note.NoteNumber);

        foreach (var midiEvent in chunk.Events)
        {
            switch (midiEvent)
            {
                case NoteOnEvent noteOn:
                    noteOn.NoteNumber = (SevenBitNumber)(byte)AdaptMidiNoteToPlayableRange((byte)noteOn.NoteNumber + octaveShift);
                    break;
                case NoteOffEvent noteOff:
                    noteOff.NoteNumber = (SevenBitNumber)(byte)AdaptMidiNoteToPlayableRange((byte)noteOff.NoteNumber + octaveShift);
                    break;
            }
        }

        return changedNotes;
    }

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

    private static IEnumerable<SplitChordGroup> SplitChordNotes(
        IEnumerable<Note> notes,
        string trackName,
        MidiForgeChordSplitStrategy strategy,
        MidiForgeChordGroupMode groupMode,
        int minimumSimultaneousNotes)
    {
        var splitGroups = new Dictionary<string, SplitChordGroup>();

        foreach (var group in notes
            .GroupBy(note => strategy == MidiForgeChordSplitStrategy.SameStartTickAndLength
                ? (note.Time, note.Length)
                : (note.Time, Length: 0))
            .OrderBy(group => group.Key.Time))
        {
            var groupNotes = group
                .OrderByDescending(note => (byte)note.NoteNumber)
                .ToArray();
            var groupSize = groupNotes.Length;
            var isChord = groupSize >= minimumSimultaneousNotes;

            for (int i = 0; i < groupNotes.Length; i++)
            {
                var partOrder = i + 1;
                var trackGroupName = GetSplitChordGroupTrackName(trackName, groupSize, partOrder, isChord, groupMode);
                if (!splitGroups.TryGetValue(trackGroupName, out var splitGroup))
                {
                    splitGroup = new SplitChordGroup(
                        trackGroupName,
                        isChord ? groupSize : 0,
                        isChord ? partOrder : 0,
                        isChord,
                        new List<Note>());
                    splitGroups.Add(trackGroupName, splitGroup);
                }

                splitGroup.Notes.Add(groupNotes[i]);
            }
        }

        return splitGroups.Values
            .OrderBy(group => group.GroupSize)
            .ThenBy(group => group.Order)
            .ThenBy(group => group.TrackName, StringComparer.Ordinal);
    }

    private static string GetSplitChordGroupTrackName(
        string trackName,
        int groupSize,
        int partOrder,
        bool isChord,
        MidiForgeChordGroupMode groupMode)
    {
        if (!isChord)
            return $"{trackName} no chords";

        return groupMode switch
        {
            MidiForgeChordGroupMode.Group => $"{trackName} chords of {groupSize}",
            MidiForgeChordGroupMode.Individual => $"{trackName} chords of {groupSize} ({partOrder})",
            _ => $"{trackName} chords parts ({partOrder})",
        };
    }

    private static bool ShouldPickAutoEditGroup(
        SplitChordGroup group,
        int maxSimultaneousNotes,
        MidiForgeChordPickStrategy pickStrategy)
    {
        if (!group.IsChord || group.Order == 1)
            return true;

        if (maxSimultaneousNotes <= 1)
            return false;

        if (maxSimultaneousNotes == 2)
        {
            if (pickStrategy == MidiForgeChordPickStrategy.OddChords && group.GroupSize >= 3)
                return group.Order == 3;

            return group.Order == 2;
        }

        return group.Order is 2 or 3;
    }

    private static Note[] AutoEditDrumNotes(
        Note[] notes,
        string trackName,
        ref int autoEditedTracks,
        ref int transposedNotes)
    {
        var pickedNotes = SplitChordNotes(
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
        => new(
            note.NoteNumber,
            Math.Max(0, length),
            note.Time)
        {
            Channel = note.Channel,
            Velocity = note.Velocity,
            OffVelocity = note.OffVelocity,
        };

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

    private static string GetDefaultTrackName(EditableTrack track, MidiForgeTrackNameFillMode fillMode, int fallbackIndex)
        => MidiForgeTrackNaming.GetDefaultTrackName(track.Chunk, fallbackIndex, fillMode);

    private static bool SetEditableTrackName(EditableTrack track, string name)
    {
        if (string.Equals(track.Name, name, StringComparison.Ordinal))
            return false;

        track.Name = name;
        track.MarkNameDirty();
        return true;
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
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();

        manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(trackName), 0));

        foreach (var timedEvent in sourceChunk.GetTimedEvents()
            .Where(te => te.Event is not NoteOnEvent and not NoteOffEvent and not SequenceTrackNameEvent))
        {
            if (!includePitchBendEvents && timedEvent.Event is PitchBendEvent)
                continue;

            manager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), timedEvent.Time));
        }

        foreach (var note in notes.OrderBy(note => note.Time).ThenBy(note => (byte)note.NoteNumber))
        {
            manager.Objects.Add(new TimedEvent(
                new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                note.Time));
            manager.Objects.Add(new TimedEvent(
                new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                note.EndTime));
        }

        return chunk;
    }

    private sealed record SplitChordGroup(
        string TrackName,
        int GroupSize,
        int Order,
        bool IsChord,
        List<Note> Notes);
}
