using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

using MidiBard.Control.MidiControl.Editing.Commands.Event;

using static MidiBard.Control.MidiControl.Editing.Commands.Event.EventCommandHelpers;
using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

public readonly record struct NoteSelectionKey(EventSelectionKey Event)
{
    public static NoteSelectionKey FromEvent(int index, EditableEvent editableEvent)
    {
        ArgumentNullException.ThrowIfNull(editableEvent);

        if (editableEvent.NoteOffSource is null || editableEvent.Source.Event is not NoteOnEvent)
            throw new ArgumentException("Selection key must refer to a note row.", nameof(editableEvent));

        return new NoteSelectionKey(EventSelectionKey.FromEvent(index, editableEvent));
    }

    public bool Matches(EditableEvent editableEvent)
        => editableEvent?.NoteOffSource is not null
           && editableEvent.Source.Event is NoteOnEvent
           && Event.Matches(editableEvent);
}

public sealed record InsertNoteOptions(
    int TrackIndex,
    long Tick,
    int NoteNumber,
    int Velocity,
    long DurationTicks,
    bool PreventOverlap,
    bool TrimToFit);

public sealed record DeleteNoteOptions(
    int TrackIndex,
    NoteSelectionKey Note);

public sealed record DeleteSelectedNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> Notes);

public sealed record NoteEditValues(
    long Tick,
    int NoteNumber,
    int Velocity,
    long DurationTicks);

public sealed record NoteEditOperation(
    NoteSelectionKey Note,
    NoteEditValues Values);

public sealed record MoveSelectedNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteEditOperation> Notes);

public sealed record ResizeSelectedNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteEditOperation> Notes);

public sealed record NudgeSelectedNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> Notes,
    long DeltaTicks);

public sealed record ResizeSelectedNotesFromStartOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> Notes,
    long DeltaTicks);

public sealed record CopiedNote(
    long RelativeTick,
    int NoteNumber,
    int Velocity,
    long DurationTicks);

public sealed record CopySelectedNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> Notes);

public sealed record CopySelectedNotesResult(
    int CopiedNotes,
    IReadOnlyList<CopiedNote> Notes);

public sealed record PasteCopiedNotesOptions(
    int TrackIndex,
    long AnchorTick,
    IReadOnlyList<CopiedNote> Notes);

public sealed record TransposeSelectedNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> Notes,
    int Semitones);

[EditorOperation(
    "note.insert",
    "Insert Note",
    Scope = EditorOperationScope.Note)]
public sealed class InsertNoteCommand
    : EditorOperationBase, IEditorCommand<InsertNoteOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, InsertNoteOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.NoteNumber < 0 || options.NoteNumber > 127)
            return EditorCommandValidation.Failure("Choose a note from 0 to 127.");

        if (options.Velocity < 1 || options.Velocity > 127)
            return EditorCommandValidation.Failure("Choose a velocity from 1 to 127.");

        if (options.DurationTicks <= 0)
            return EditorCommandValidation.Failure("Choose a positive note duration.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        InsertNoteOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        var tick = Math.Max(0, options.Tick);
        var noteNumber = Math.Clamp(options.NoteNumber, 0, 127);
        var duration = Math.Max(1, options.DurationTicks);

        if (options.PreventOverlap)
        {
            var overlapEndTick = FindOverlapEndTick(track.Events, noteNumber, tick);
            if (overlapEndTick != tick)
                return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

            var nextStart = FindNextNoteStartAfter(track.Events, null, noteNumber, tick);
            if (nextStart < tick + duration)
            {
                if (!options.TrimToFit)
                    return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

                duration = nextStart - tick;
                if (duration <= 0)
                    return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));
            }
        }

        var editableEvent = track.InsertNote(tick, noteNumber, options.Velocity, duration);
        if (editableEvent is null || track.Events is null)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var insertedIndex = track.Events.IndexOf(editableEvent);
        var affectedIndices = insertedIndex >= 0 ? new[] { insertedIndex } : Array.Empty<int>();

        return EditorCommandResult<NoteMutationResult>.ChangedResult(
            new NoteMutationResult(1, insertedIndex, affectedIndices),
            refreshHints: NoteEditCommandHelpers.NoteChangedHints);
    }

    internal static long FindOverlapEndTick(List<EditableEvent>? events, int noteNumber, long tick)
    {
        if (events is null)
            return tick;

        var end = tick;
        foreach (var editableEvent in events)
        {
            if (editableEvent.Source.Event is not NoteOnEvent noteOn)
                continue;

            if ((byte)noteOn.Velocity == 0)
                continue;

            if ((byte)noteOn.NoteNumber != noteNumber)
                continue;

            var eventStart = editableEvent.Tick;
            var eventEnd = editableEvent.Tick + editableEvent.DurationTicks;
            if (eventStart <= tick && tick < eventEnd)
                end = Math.Max(end, eventEnd);
        }

        return end;
    }

    internal static long FindNextNoteStartAfter(
        List<EditableEvent>? events,
        EditableEvent? exclude,
        int noteNumber,
        long afterTick)
    {
        if (events is null)
            return long.MaxValue;

        var min = long.MaxValue;
        foreach (var editableEvent in events)
        {
            if (ReferenceEquals(editableEvent, exclude))
                continue;

            if (editableEvent.Source.Event is not NoteOnEvent noteOn)
                continue;

            if ((byte)noteOn.Velocity == 0)
                continue;

            if ((byte)noteOn.NoteNumber != noteNumber)
                continue;

            if (editableEvent.Tick > afterTick)
                min = Math.Min(min, editableEvent.Tick);
        }

        return min;
    }
}

[EditorOperation(
    "note.delete",
    "Delete Note",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class DeleteNoteCommand
    : EditorOperationBase, IEditorCommand<DeleteNoteOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DeleteNoteOptions options)
        => IsPerformanceTrackIndex(context.File, options.TrackIndex)
            ? EditorCommandValidation.Success
            : EditorCommandValidation.Failure("Choose a performance track.");

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        DeleteNoteOptions options)
    {
        var result = context.Invoker.Execute(
            new DeleteSelectedNotesCommand(),
            new DeleteSelectedNotesOptions(options.TrackIndex, new[] { options.Note }));

        if (!result.Succeeded)
            return EditorCommandResult<NoteMutationResult>.NoChange(result.Message);

        return result.Result!;
    }
}

[EditorOperation(
    "note.delete-selected",
    "Delete Selected Notes",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class DeleteSelectedNotesCommand
    : EditorOperationBase, IEditorCommand<DeleteSelectedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DeleteSelectedNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Notes is null || options.Notes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        DeleteSelectedNotesOptions options)
    {
        var eventKeys = options.Notes
            .Select(note => note.Event)
            .ToArray();

        var result = context.Invoker.Execute(
            new DeleteEventsCommand(),
            new DeleteEventsOptions(options.TrackIndex, eventKeys, ClearSelection: false));

        if (!result.Succeeded)
            return EditorCommandResult<NoteMutationResult>.NoChange(result.Message);

        var changedEvents = result.Result!.Value.ChangedEvents;
        var noteResult = new NoteMutationResult(changedEvents);
        return result.Changed
            ? EditorCommandResult<NoteMutationResult>.ChangedResult(
                noteResult,
                refreshHints: result.Result.RefreshHints)
            : EditorCommandResult<NoteMutationResult>.UnchangedResult(noteResult);
    }
}

[EditorOperation(
    "note.move-selected",
    "Move Selected Notes",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class MoveSelectedNotesCommand
    : EditorOperationBase, IEditorCommand<MoveSelectedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, MoveSelectedNotesOptions options)
        => NoteEditCommandHelpers.ValidateNoteEditSelection(context, options.TrackIndex, options.Notes);

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        MoveSelectedNotesOptions options)
        => NoteEditCommandHelpers.ApplyNoteEdits(context, options.TrackIndex, options.Notes);
}

[EditorOperation(
    "note.resize-selected",
    "Resize Selected Notes",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class ResizeSelectedNotesCommand
    : EditorOperationBase, IEditorCommand<ResizeSelectedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ResizeSelectedNotesOptions options)
        => NoteEditCommandHelpers.ValidateNoteEditSelection(context, options.TrackIndex, options.Notes);

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        ResizeSelectedNotesOptions options)
        => NoteEditCommandHelpers.ApplyNoteEdits(context, options.TrackIndex, options.Notes);
}

[EditorOperation(
    "note.nudge-selected",
    "Move Selected Notes",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class NudgeSelectedNotesCommand
    : EditorOperationBase, IEditorCommand<NudgeSelectedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, NudgeSelectedNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Notes is null || options.Notes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        NudgeSelectedNotesOptions options)
    {
        if (options.DeltaTicks == 0)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var track = context.File.Tracks[options.TrackIndex];
        var excludedEvents = new List<EditableEvent>();
        var editOperations = new List<NoteEditOperation>();

        foreach (var note in options.Notes)
        {
            var editableEvent = NoteEditCommandHelpers.ResolveNote(track, note, excludedEvents);
            if (editableEvent is null || editableEvent.Source.Event is not NoteOnEvent noteOn)
                continue;

            excludedEvents.Add(editableEvent);
            var newTick = Math.Max(0, editableEvent.Tick + options.DeltaTicks);
            if (newTick == editableEvent.Tick)
                continue;

            var eventIndex = track.Events!.IndexOf(editableEvent);
            editOperations.Add(new NoteEditOperation(
                NoteSelectionKey.FromEvent(eventIndex, editableEvent),
                new NoteEditValues(
                    newTick,
                    (byte)noteOn.NoteNumber,
                    (byte)noteOn.Velocity,
                    editableEvent.DurationTicks)));
        }

        if (editOperations.Count == 0)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var result = context.Invoker.Execute(
            new MoveSelectedNotesCommand(),
            new MoveSelectedNotesOptions(options.TrackIndex, editOperations));

        if (!result.Succeeded)
            return EditorCommandResult<NoteMutationResult>.NoChange(result.Message);

        return result.Result!;
    }
}

[EditorOperation(
    "note.resize-selected-from-start",
    "Resize Selected Notes From Start",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class ResizeSelectedNotesFromStartCommand
    : EditorOperationBase, IEditorCommand<ResizeSelectedNotesFromStartOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ResizeSelectedNotesFromStartOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Notes is null || options.Notes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        ResizeSelectedNotesFromStartOptions options)
    {
        if (options.DeltaTicks == 0)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var track = context.File.Tracks[options.TrackIndex];
        var excludedEvents = new List<EditableEvent>();
        var editOperations = new List<NoteEditOperation>();

        foreach (var note in options.Notes)
        {
            var editableEvent = NoteEditCommandHelpers.ResolveNote(track, note, excludedEvents);
            if (editableEvent is null || editableEvent.Source.Event is not NoteOnEvent noteOn)
                continue;

            excludedEvents.Add(editableEvent);
            var endTick = editableEvent.Tick + editableEvent.DurationTicks;
            var newTick = Math.Clamp(editableEvent.Tick + options.DeltaTicks, 0, endTick - 1);
            var newDuration = endTick - newTick;
            if (newTick == editableEvent.Tick && newDuration == editableEvent.DurationTicks)
                continue;

            var eventIndex = track.Events!.IndexOf(editableEvent);
            editOperations.Add(new NoteEditOperation(
                NoteSelectionKey.FromEvent(eventIndex, editableEvent),
                new NoteEditValues(
                    newTick,
                    (byte)noteOn.NoteNumber,
                    (byte)noteOn.Velocity,
                    newDuration)));
        }

        if (editOperations.Count == 0)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var result = context.Invoker.Execute(
            new ResizeSelectedNotesCommand(),
            new ResizeSelectedNotesOptions(options.TrackIndex, editOperations));

        if (!result.Succeeded)
            return EditorCommandResult<NoteMutationResult>.NoChange(result.Message);

        return result.Result!;
    }
}

[EditorOperation(
    "note.copy-selected",
    "Copy Selected Notes",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class CopySelectedNotesCommand
    : EditorOperationBase, IEditorCommand<CopySelectedNotesOptions, CopySelectedNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, CopySelectedNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Notes is null || options.Notes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before copying notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<CopySelectedNotesResult> Execute(
        EditorCommandContext context,
        CopySelectedNotesOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        var copiedNotes = NoteEditCommandHelpers.GetCopiedNotes(track, options.Notes);
        if (copiedNotes.Count == 0)
        {
            return EditorCommandResult<CopySelectedNotesResult>.UnchangedResult(
                new CopySelectedNotesResult(0, copiedNotes));
        }

        context.Session.NoteClipboard.Set(copiedNotes);
        return EditorCommandResult<CopySelectedNotesResult>.UnchangedResult(
            new CopySelectedNotesResult(copiedNotes.Count, copiedNotes));
    }
}

[EditorOperation(
    "note.paste-copied",
    "Paste Notes",
    Scope = EditorOperationScope.Note)]
public sealed class PasteCopiedNotesCommand
    : EditorOperationBase, IEditorCommand<PasteCopiedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, PasteCopiedNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Notes is null || options.Notes.Count == 0)
            return EditorCommandValidation.Failure("Copy at least one note first.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before pasting notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        PasteCopiedNotesOptions options)
    {
        var changedEvents = 0;
        var affectedIndices = new List<int>();

        foreach (var note in options.Notes)
        {
            var result = context.Invoker.Execute(
                new InsertNoteCommand(),
                new InsertNoteOptions(
                    options.TrackIndex,
                    Math.Max(0, options.AnchorTick + note.RelativeTick),
                    note.NoteNumber,
                    note.Velocity,
                    note.DurationTicks,
                    PreventOverlap: false,
                    TrimToFit: false));

            if (!result.Succeeded)
                return EditorCommandResult<NoteMutationResult>.NoChange(result.Message);

            if (!result.Changed)
                continue;

            changedEvents += result.Result!.Value.ChangedEvents;
            if (result.Result.Value.InsertedEventIndex >= 0)
                affectedIndices.Add(result.Result.Value.InsertedEventIndex);
        }

        var mutationResult = new NoteMutationResult(
            changedEvents,
            AffectedEventIndices: affectedIndices.ToArray());

        return changedEvents == 0
            ? EditorCommandResult<NoteMutationResult>.UnchangedResult(mutationResult)
            : EditorCommandResult<NoteMutationResult>.ChangedResult(
                mutationResult,
                refreshHints: NoteEditCommandHelpers.NoteChangedHints);
    }
}

[EditorOperation(
    "note.transpose-selected",
    "Transpose Selected Notes",
    Scope = EditorOperationScope.Note,
    RequiresSelectedEvents = true)]
public sealed class TransposeSelectedNotesCommand
    : EditorOperationBase, IEditorCommand<TransposeSelectedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, TransposeSelectedNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Notes is null || options.Notes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        TransposeSelectedNotesOptions options)
    {
        if (options.Semitones == 0)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var track = context.File.Tracks[options.TrackIndex];
        if (track.Events is null)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var excludedEvents = new List<EditableEvent>();
        var editOperations = new List<NoteEditOperation>();
        foreach (var note in options.Notes)
        {
            var editableEvent = NoteEditCommandHelpers.ResolveNote(track, note, excludedEvents);
            if (editableEvent is null || editableEvent.Source.Event is not NoteOnEvent noteOn)
                continue;

            excludedEvents.Add(editableEvent);
            var newNoteNumber = Math.Clamp((byte)noteOn.NoteNumber + options.Semitones, 0, 127);
            if (newNoteNumber == (byte)noteOn.NoteNumber)
                continue;

            var eventIndex = track.Events.IndexOf(editableEvent);
            editOperations.Add(new NoteEditOperation(
                NoteSelectionKey.FromEvent(eventIndex, editableEvent),
                new NoteEditValues(
                    editableEvent.Tick,
                    newNoteNumber,
                    (byte)noteOn.Velocity,
                    editableEvent.DurationTicks)));
        }

        if (editOperations.Count == 0)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(new NoteMutationResult(0));

        var result = context.Invoker.Execute(
            new MoveSelectedNotesCommand(),
            new MoveSelectedNotesOptions(options.TrackIndex, editOperations));

        if (!result.Succeeded)
            return EditorCommandResult<NoteMutationResult>.NoChange(result.Message);

        return result.Result!;
    }
}

internal static class NoteEditCommandHelpers
{
    public static EditorCommandValidation ValidateNoteEditSelection(
        EditorCommandContext context,
        int trackIndex,
        IReadOnlyList<NoteEditOperation> notes)
    {
        if (!IsPerformanceTrackIndex(context.File, trackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (notes is null || notes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return context.File.Tracks[trackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public static EditorCommandResult<NoteMutationResult> ApplyNoteEdits(
        EditorCommandContext context,
        int trackIndex,
        IReadOnlyList<NoteEditOperation> operations)
    {
        var track = context.File.Tracks[trackIndex];
        var excludedEvents = new List<EditableEvent>();
        var affectedEventIndices = new List<int>();

        foreach (var operation in operations)
        {
            var editableEvent = ResolveNote(track, operation.Note, excludedEvents);
            if (editableEvent is null)
                continue;

            excludedEvents.Add(editableEvent);

            var currentValues = GetNoteEditValues(editableEvent);
            var targetValues = NormalizeNoteEditValues(operation.Values);
            if (currentValues == targetValues)
                continue;

            editableEvent.EditTick = (int)targetValues.Tick;
            editableEvent.EditValue1 = targetValues.NoteNumber;
            editableEvent.EditValue2 = targetValues.Velocity;
            editableEvent.EditDuration = (int)targetValues.DurationTicks;
            editableEvent.ApplyEditValues();

            affectedEventIndices.Add(track.Events!.IndexOf(editableEvent));
        }

        var result = new NoteMutationResult(
            affectedEventIndices.Count,
            AffectedEventIndices: affectedEventIndices.ToArray());

        return result.ChangedEvents == 0
            ? EditorCommandResult<NoteMutationResult>.UnchangedResult(result)
            : EditorCommandResult<NoteMutationResult>.ChangedResult(result, refreshHints: NoteChangedHints);
    }

    public static EditableEvent ResolveNote(
        EditableTrack track,
        NoteSelectionKey note,
        IReadOnlyCollection<EditableEvent> excludedEvents)
    {
        var editableEvent = ResolveEvent(track, note.Event, excludedEvents);
        return editableEvent is not null && note.Matches(editableEvent)
            ? editableEvent
            : null;
    }

    public static IReadOnlyList<CopiedNote> GetCopiedNotes(
        EditableTrack track,
        IReadOnlyList<NoteSelectionKey> notes)
    {
        if (track?.Events is null || notes is null || notes.Count == 0)
            return [];

        var excludedEvents = new List<EditableEvent>();
        var selectedNotes = new List<(EditableEvent Event, NoteOnEvent NoteOn)>();
        foreach (var note in notes)
        {
            var editableEvent = ResolveNote(track, note, excludedEvents);
            if (editableEvent?.Source.Event is not NoteOnEvent noteOn)
                continue;

            excludedEvents.Add(editableEvent);
            selectedNotes.Add((editableEvent, noteOn));
        }

        if (selectedNotes.Count == 0)
            return [];

        var minTick = selectedNotes.Min(note => note.Event.Tick);
        return selectedNotes
            .OrderBy(note => note.Event.Tick)
            .Select(note => new CopiedNote(
                note.Event.Tick - minTick,
                (byte)note.NoteOn.NoteNumber,
                (byte)note.NoteOn.Velocity,
                note.Event.DurationTicks))
            .ToArray();
    }

    private static NoteEditValues GetNoteEditValues(EditableEvent editableEvent)
    {
        editableEvent.RefreshEditValues();
        return new NoteEditValues(
            editableEvent.EditTick,
            editableEvent.EditValue1,
            editableEvent.EditValue2,
            editableEvent.EditDuration);
    }

    private static NoteEditValues NormalizeNoteEditValues(NoteEditValues values)
        => new(
            Math.Max(0, values.Tick),
            Math.Clamp(values.NoteNumber, 0, 127),
            Math.Clamp(values.Velocity, 0, 127),
            Math.Max(1, values.DurationTicks));

    public static EditorRefreshHints NoteChangedHints
        => new(
            ReloadEventList: true,
            RebuildPreview: true,
            RecalculateMetrics: true);
}
