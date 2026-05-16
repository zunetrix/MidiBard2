using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using DryWetMidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.strum",
    "Strum Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Chords")]
public sealed class StrumNotesCommand
    : EditorOperationBase, IEditorCommand<StrumNotesCommandOptions, MidiForgeStrumNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, StrumNotesCommandOptions options)
    {
        if (options.SelectedNotes is { Count: > 0 })
            return options.SelectedTrackIndex >= 0 &&
                   options.SelectedTrackIndex < context.File.Tracks.Count &&
                   !context.File.Tracks[options.SelectedTrackIndex].IsConductorTrack
                ? EditorCommandValidation.Success
                : EditorCommandValidation.Failure("Choose a performance track with selected notes.");

        if (options.TrackIndices is null || options.TrackIndices.Count == 0)
            return EditorCommandValidation.Failure("Choose selected notes or at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeStrumNotesResult> Execute(
        EditorCommandContext context,
        StrumNotesCommandOptions commandOptions)
    {
        var options = commandOptions.Options ?? new MidiForgeStrumNotesOptions();
        if (commandOptions.SelectedNotes is { Count: > 0 })
            return StrumSelectedNotes(context, commandOptions, options);

        return StrumSelectedTracks(context, commandOptions, options);
    }

    private static EditorCommandResult<MidiForgeStrumNotesResult> StrumSelectedNotes(
        EditorCommandContext context,
        StrumNotesCommandOptions commandOptions,
        MidiForgeStrumNotesOptions options)
    {
        var track = context.File.Tracks[commandOptions.SelectedTrackIndex];
        if (track.Events is null)
            return EditorCommandResult<MidiForgeStrumNotesResult>.NoChange("Load the track before editing notes.");

        var selectedNotes = ResolveSelectedNotes(track, commandOptions.SelectedNotes)
            .ToArray();
        if (selectedNotes.Length == 0)
            return EditorCommandResult<MidiForgeStrumNotesResult>.UnchangedResult(new MidiForgeStrumNotesResult(0, 0, 0, 0, 0));

        var timingToleranceTicks = MidiForgeNotePrimitives.ResolveChordTimingToleranceTicks(
            context.File,
            options.ChordTimingTolerance);
        var operations = BuildSelectedNoteStrumOperations(
            selectedNotes,
            options,
            timingToleranceTicks,
            out var strummedGroups);
        if (operations.Count == 0)
            return EditorCommandResult<MidiForgeStrumNotesResult>.UnchangedResult(new MidiForgeStrumNotesResult(1, 0, 0, 0, 0));

        var moveResult = context.Invoker.Execute(
            new MoveSelectedNotesCommand(),
            new MoveSelectedNotesOptions(commandOptions.SelectedTrackIndex, operations));
        if (!moveResult.Succeeded)
            return EditorCommandResult<MidiForgeStrumNotesResult>.NoChange(moveResult.Message);

        var result = new MidiForgeStrumNotesResult(
            SourceTracks: 1,
            CreatedTracks: 0,
            ReplacedTracks: 0,
            StrummedChordGroups: strummedGroups,
            ChangedNotes: moveResult.Result!.Value.ChangedEvents);

        return moveResult.Changed
            ? EditorCommandResult<MidiForgeStrumNotesResult>.ChangedResult(result, refreshHints: moveResult.Result.RefreshHints)
            : EditorCommandResult<MidiForgeStrumNotesResult>.UnchangedResult(result);
    }

    private static EditorCommandResult<MidiForgeStrumNotesResult> StrumSelectedTracks(
        EditorCommandContext context,
        StrumNotesCommandOptions commandOptions,
        MidiForgeStrumNotesOptions options)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();
        var timingToleranceTicks = MidiForgeNotePrimitives.ResolveChordTimingToleranceTicks(
            file,
            options.ChordTimingTolerance);
        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var strummedGroups = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var strummedNotes = BuildTrackStrummedNotes(
                notes,
                options,
                timingToleranceTicks,
                out var trackStrummedGroups,
                out var trackChangedNotes);
            if (trackChangedNotes == 0)
                continue;

            sourceTracks++;
            strummedGroups += trackStrummedGroups;
            changedNotes += trackChangedNotes;

            var outputChunk = MidiForgeNotePrimitives.CreateTrackFromNotes(
                sourceChunk,
                $"{track.DisplayName} (Strummed)",
                strummedNotes);
            var outputTrack = new EditableTrack(outputChunk, options.CreateNewTracks ? trackIndex + 1 : trackIndex);
            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, outputTrack);
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = outputTrack;
                replacedTracks++;
            }
        }

        var result = new MidiForgeStrumNotesResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            strummedGroups,
            changedNotes);
        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeStrumNotesResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeStrumNotesResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: !options.CreateNewTracks,
                ReloadEventList: !options.CreateNewTracks,
                ClearTrackSelection: true,
                ClearEventSelection: !options.CreateNewTracks,
                RebuildPreview: true));
    }

    private static IEnumerable<SelectedStrumNote> ResolveSelectedNotes(
        EditableTrack track,
        IReadOnlyList<NoteSelectionKey> selectedNotes)
    {
        var excludedEvents = new List<EditableEvent>();
        foreach (var selectedNote in selectedNotes)
        {
            var editableEvent = NoteEditCommandHelpers.ResolveNote(track, selectedNote, excludedEvents);
            if (editableEvent?.Source.Event is not NoteOnEvent noteOn)
                continue;

            excludedEvents.Add(editableEvent);
            yield return new SelectedStrumNote(
                new DryWetMidiNote(
                    noteOn.NoteNumber,
                    editableEvent.DurationTicks,
                    editableEvent.Tick)
                {
                    Channel = noteOn.Channel,
                    Velocity = noteOn.Velocity,
                    OffVelocity = editableEvent.NoteOffSource?.Event is NoteOffEvent noteOff
                        ? noteOff.Velocity
                        : (SevenBitNumber)0,
                },
                selectedNote,
                (byte)noteOn.Velocity);
        }
    }

    private static List<NoteEditOperation> BuildSelectedNoteStrumOperations(
        IReadOnlyList<SelectedStrumNote> selectedNotes,
        MidiForgeStrumNotesOptions options,
        long timingToleranceTicks,
        out int strummedGroups)
    {
        var operations = new List<NoteEditOperation>();
        strummedGroups = 0;
        var groupIndex = 0;
        foreach (var group in MidiForgeNotePrimitives.BuildChordNoteGroups(
            selectedNotes.Select(note => note.Note),
            MidiForgeChordSplitStrategy.SameStartTick,
            timingToleranceTicks))
        {
            if (group.Count < 2)
                continue;

            var orderedGroup = OrderStrumGroup(group, options.Direction, groupIndex).ToArray();
            var groupOperations = BuildStrumOperations(
                orderedGroup,
                options.StepTicks,
                options.PreserveNoteEnds,
                note => selectedNotes.First(selectedNote => ReferenceEquals(selectedNote.Note, note)));
            if (groupOperations.Count == 0)
                continue;

            strummedGroups++;
            operations.AddRange(groupOperations);
            groupIndex++;
        }

        return operations;
    }

    private static DryWetMidiNote[] BuildTrackStrummedNotes(
        IReadOnlyList<DryWetMidiNote> notes,
        MidiForgeStrumNotesOptions options,
        long timingToleranceTicks,
        out int strummedGroups,
        out int changedNotes)
    {
        var changedBySourceNote = new Dictionary<DryWetMidiNote, DryWetMidiNote>();
        strummedGroups = 0;
        changedNotes = 0;
        var groupIndex = 0;
        var candidateNotes = notes
            .Where(note => IsWithinRange(note, options.StartTick, options.EndTick))
            .ToArray();

        foreach (var group in MidiForgeNotePrimitives.BuildChordNoteGroups(
            candidateNotes,
            MidiForgeChordSplitStrategy.SameStartTick,
            timingToleranceTicks))
        {
            if (group.Count < 2)
                continue;

            var orderedGroup = OrderStrumGroup(group, options.Direction, groupIndex).ToArray();
            var changedInGroup = 0;
            for (int i = 0; i < orderedGroup.Length; i++)
            {
                var note = orderedGroup[i];
                var shiftedNote = CloneStrummedNote(note, i, options.StepTicks, options.PreserveNoteEnds);
                if (shiftedNote.Time == note.Time && shiftedNote.Length == note.Length)
                    continue;

                changedBySourceNote[note] = shiftedNote;
                changedInGroup++;
            }

            if (changedInGroup > 0)
            {
                strummedGroups++;
                changedNotes += changedInGroup;
            }

            groupIndex++;
        }

        return notes
            .Select(note => changedBySourceNote.TryGetValue(note, out var changedNote) ? changedNote : note)
            .ToArray();
    }

    private static List<NoteEditOperation> BuildStrumOperations(
        IReadOnlyList<DryWetMidiNote> orderedGroup,
        long stepTicks,
        bool preserveNoteEnds,
        Func<DryWetMidiNote, SelectedStrumNote> getSelectedNote)
    {
        var operations = new List<NoteEditOperation>();
        for (int i = 0; i < orderedGroup.Count; i++)
        {
            var note = orderedGroup[i];
            var selectedNote = getSelectedNote(note);
            var shiftedNote = CloneStrummedNote(note, i, stepTicks, preserveNoteEnds);
            if (shiftedNote.Time == note.Time && shiftedNote.Length == note.Length)
                continue;

            operations.Add(new NoteEditOperation(
                selectedNote.SelectionKey,
                new NoteEditValues(
                    shiftedNote.Time,
                    (byte)shiftedNote.NoteNumber,
                    selectedNote.Velocity,
                    shiftedNote.Length)));
        }

        return operations;
    }

    private static IEnumerable<DryWetMidiNote> OrderStrumGroup(
        IReadOnlyList<DryWetMidiNote> group,
        MidiForgeStrumDirection direction,
        int groupIndex)
    {
        var lowToHigh = direction == MidiForgeStrumDirection.LowToHigh ||
                        (direction == MidiForgeStrumDirection.Alternate && groupIndex % 2 == 0);
        return lowToHigh
            ? group.OrderBy(note => (byte)note.NoteNumber).ThenBy(note => note.Time)
            : group.OrderByDescending(note => (byte)note.NoteNumber).ThenBy(note => note.Time);
    }

    private static DryWetMidiNote CloneStrummedNote(
        DryWetMidiNote note,
        int order,
        long stepTicks,
        bool preserveNoteEnds)
    {
        var delay = Math.Max(0, stepTicks) * order;
        var newTime = note.Time + delay;
        var newLength = preserveNoteEnds
            ? Math.Max(1, note.EndTime - newTime)
            : note.Length;

        return new DryWetMidiNote(
            note.NoteNumber,
            newLength,
            newTime)
        {
            Channel = note.Channel,
            Velocity = note.Velocity,
            OffVelocity = note.OffVelocity,
        };
    }

    private static bool IsWithinRange(
        DryWetMidiNote note,
        long? startTick,
        long? endTick)
        => (!startTick.HasValue || note.Time >= startTick.Value) &&
           (!endTick.HasValue || note.Time <= endTick.Value);

    private sealed record SelectedStrumNote(
        DryWetMidiNote Note,
        NoteSelectionKey SelectionKey,
        int Velocity);
}

public sealed record StrumNotesCommandOptions(
    IReadOnlyList<int> TrackIndices,
    int SelectedTrackIndex,
    IReadOnlyList<NoteSelectionKey> SelectedNotes,
    MidiForgeStrumNotesOptions Options);
