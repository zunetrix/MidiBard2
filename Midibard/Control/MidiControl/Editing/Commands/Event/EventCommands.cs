using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;
using static MidiBard.Control.MidiControl.Editing.Commands.Event.EventCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Event;

public readonly record struct EventSelectionKey(
    int Index,
    long Tick,
    long DurationTicks,
    string EventType,
    int Value1,
    int Value2,
    string TextValue)
{
    public static EventSelectionKey FromEvent(int index, EditableEvent editableEvent)
    {
        ArgumentNullException.ThrowIfNull(editableEvent);

        var (value1, value2, textValue) = GetSignature(editableEvent);
        return new EventSelectionKey(
            index,
            editableEvent.Tick,
            editableEvent.DurationTicks,
            editableEvent.Source.Event.GetType().FullName ?? editableEvent.Source.Event.GetType().Name,
            value1,
            value2,
            textValue);
    }

    public bool Matches(EditableEvent editableEvent)
    {
        if (editableEvent is null)
            return false;

        var (value1, value2, textValue) = GetSignature(editableEvent);
        return Tick == editableEvent.Tick
               && DurationTicks == editableEvent.DurationTicks
               && string.Equals(
                   EventType,
                   editableEvent.Source.Event.GetType().FullName ?? editableEvent.Source.Event.GetType().Name,
                   StringComparison.Ordinal)
               && Value1 == value1
               && Value2 == value2
               && string.Equals(TextValue, textValue, StringComparison.Ordinal);
    }

    private static (int value1, int value2, string textValue) GetSignature(EditableEvent editableEvent)
        => editableEvent.Source.Event switch
        {
            NoteOnEvent noteOn => ((byte)noteOn.NoteNumber, (byte)noteOn.Velocity, null),
            NoteOffEvent noteOff => ((byte)noteOff.NoteNumber, 0, null),
            ProgramChangeEvent programChange => ((byte)programChange.ProgramNumber, 0, null),
            ControlChangeEvent controlChange => ((byte)controlChange.ControlNumber, (byte)controlChange.ControlValue, null),
            PitchBendEvent pitchBend => (pitchBend.PitchValue, 0, null),
            SetTempoEvent tempo => ((int)Math.Clamp(tempo.MicrosecondsPerQuarterNote, int.MinValue, int.MaxValue), 0, null),
            TimeSignatureEvent timeSignature => (timeSignature.Numerator, timeSignature.Denominator, null),
            BaseTextEvent text => (0, 0, text.Text ?? string.Empty),
            _ => (0, 0, editableEvent.GetValueDisplay()),
        };
}

public sealed record EventEditValues(
    int Tick,
    int Value1,
    int Value2,
    int DurationTicks);

public sealed record EventMutationResult(int ChangedEvents);

[EditorOperation(
    "event.delete",
    "Delete Event",
    Scope = EditorOperationScope.Event,
    RequiresSelectedEvents = true)]
public sealed class DeleteEventCommand
    : EditorOperationBase, IEditorCommand<DeleteEventOptions, EventMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DeleteEventOptions options)
        => IsPerformanceTrackIndex(context.File, options.TrackIndex)
            ? EditorCommandValidation.Success
            : EditorCommandValidation.Failure("Choose a performance track.");

    public EditorCommandResult<EventMutationResult> Execute(
        EditorCommandContext context,
        DeleteEventOptions options)
    {
        var result = context.Invoker.Execute(
            new DeleteEventsCommand(),
            new DeleteEventsOptions(options.TrackIndex, new[] { options.Event }));

        if (!result.Succeeded)
            return EditorCommandResult<EventMutationResult>.NoChange(result.Message);

        return result.Result!;
    }
}

public sealed record DeleteEventOptions(
    int TrackIndex,
    EventSelectionKey Event);

[EditorOperation(
    "event.delete-selected",
    "Delete Selected Events",
    Scope = EditorOperationScope.Event,
    RequiresSelectedEvents = true)]
public sealed class DeleteEventsCommand
    : EditorOperationBase, IEditorCommand<DeleteEventsOptions, EventMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DeleteEventsOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.Events is null || options.Events.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one event.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<EventMutationResult> Execute(
        EditorCommandContext context,
        DeleteEventsOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        var resolvedEvents = ResolveEvents(track, options.Events);
        foreach (var editableEvent in resolvedEvents)
            track.RemoveEvent(editableEvent);

        var result = new EventMutationResult(resolvedEvents.Count);
        if (result.ChangedEvents == 0)
            return EditorCommandResult<EventMutationResult>.UnchangedResult(result);

        return EditorCommandResult<EventMutationResult>.ChangedResult(
            result,
            refreshHints: options.ClearSelection ? EventChangedHints : EventChangedHintsWithoutSelectionClear);
    }
}

public sealed record DeleteEventsOptions(
    int TrackIndex,
    IReadOnlyList<EventSelectionKey> Events,
    bool ClearSelection = true);

[EditorOperation(
    "event.edit",
    "Edit Event",
    Scope = EditorOperationScope.Event,
    RequiresSelectedEvents = true)]
public sealed class EditEventCommand
    : EditorOperationBase, IEditorCommand<EditEventOptions, EventMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, EditEventOptions options)
        => IsPerformanceTrackIndex(context.File, options.TrackIndex)
            ? EditorCommandValidation.Success
            : EditorCommandValidation.Failure("Choose a performance track.");

    public EditorCommandResult<EventMutationResult> Execute(
        EditorCommandContext context,
        EditEventOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        var editableEvent = ResolveEvent(track, options.Event, []);
        if (editableEvent is null)
            return EditorCommandResult<EventMutationResult>.UnchangedResult(new EventMutationResult(0));

        var currentValues = EventEditValuesFromEvent(editableEvent);
        if (currentValues == options.Values)
            return EditorCommandResult<EventMutationResult>.UnchangedResult(new EventMutationResult(0));

        editableEvent.EditTick = options.Values.Tick;
        editableEvent.EditValue1 = options.Values.Value1;
        editableEvent.EditValue2 = options.Values.Value2;
        editableEvent.EditDuration = options.Values.DurationTicks;
        editableEvent.ApplyEditValues();

        return EditorCommandResult<EventMutationResult>.ChangedResult(
            new EventMutationResult(1),
            refreshHints: EventChangedHints);
    }

    private static EventEditValues EventEditValuesFromEvent(EditableEvent editableEvent)
    {
        editableEvent.RefreshEditValues();
        return new EventEditValues(
            editableEvent.EditTick,
            editableEvent.EditValue1,
            editableEvent.EditValue2,
            editableEvent.EditDuration);
    }
}

public sealed record EditEventOptions(
    int TrackIndex,
    EventSelectionKey Event,
    EventEditValues Values);

internal static class EventCommandHelpers
{
    public static IReadOnlyList<EditableEvent> ResolveEvents(
        EditableTrack track,
        IReadOnlyList<EventSelectionKey> eventKeys)
    {
        if (track.Events is null || eventKeys.Count == 0)
            return Array.Empty<EditableEvent>();

        var resolvedEvents = new List<EditableEvent>();
        foreach (var eventKey in eventKeys)
        {
            var editableEvent = ResolveEvent(track, eventKey, resolvedEvents);
            if (editableEvent is not null)
                resolvedEvents.Add(editableEvent);
        }

        return resolvedEvents
            .OrderByDescending(editableEvent => track.Events.IndexOf(editableEvent))
            .ToArray();
    }

    public static EditableEvent ResolveEvent(
        EditableTrack track,
        EventSelectionKey eventKey,
        IReadOnlyCollection<EditableEvent> excludedEvents)
    {
        if (track.Events is null)
            return null;

        if ((uint)eventKey.Index < (uint)track.Events.Count)
        {
            var indexedEvent = track.Events[eventKey.Index];
            if (!excludedEvents.Contains(indexedEvent) && eventKey.Matches(indexedEvent))
                return indexedEvent;
        }

        return track.Events.FirstOrDefault(editableEvent =>
            !excludedEvents.Contains(editableEvent) && eventKey.Matches(editableEvent));
    }

    public static EditorRefreshHints EventChangedHints
        => new(
            ReloadSelectedTrack: true,
            ReloadEventList: true,
            ClearEventSelection: true,
            RebuildPreview: true,
            RecalculateMetrics: true);

    public static EditorRefreshHints EventChangedHintsWithoutSelectionClear
        => new(
            ReloadEventList: true,
            RebuildPreview: true,
            RecalculateMetrics: true);
}
