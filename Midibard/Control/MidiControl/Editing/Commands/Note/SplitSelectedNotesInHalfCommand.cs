using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-selected-in-half",
    "Split Selected Notes in Half",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Length")]
public sealed class SplitSelectedNotesInHalfCommand
    : EditorOperationBase, IEditorCommand<SplitSelectedNotesInHalfOptions, SplitSelectedNotesInHalfResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitSelectedNotesInHalfOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.SelectedNotes is null || options.SelectedNotes.Count == 0)
            return EditorCommandValidation.Failure("Select at least one note.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<SplitSelectedNotesInHalfResult> Execute(
        EditorCommandContext context,
        SplitSelectedNotesInHalfOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        if (track.Events is null)
            return EditorCommandResult<SplitSelectedNotesInHalfResult>.NoChange("Load the track before editing notes.");

        var resolvedNotes = ResolveSelectedNotes(track, options.SelectedNotes).ToArray();
        if (resolvedNotes.Length == 0)
            return EditorCommandResult<SplitSelectedNotesInHalfResult>.UnchangedResult(
                new SplitSelectedNotesInHalfResult(0));

        var splitCount = 0;
        foreach (var ev in resolvedNotes)
        {
            if (ev.Source.Event is not NoteOnEvent noteOn)
                continue;

            var duration = ev.DurationTicks;
            if (duration < 2)
                continue;

            var firstHalf = duration / 2;
            var secondHalf = duration - firstHalf;
            var splitTick = ev.Tick + firstHalf;

            ev.EditDuration = (int)firstHalf;
            ev.ApplyEditValues();

            track.InsertNote(splitTick, (byte)noteOn.NoteNumber, (byte)noteOn.Velocity, secondHalf);
            splitCount++;
        }

        if (splitCount == 0)
            return EditorCommandResult<SplitSelectedNotesInHalfResult>.UnchangedResult(
                new SplitSelectedNotesInHalfResult(0));

        var result = new SplitSelectedNotesInHalfResult(splitCount);
        return EditorCommandResult<SplitSelectedNotesInHalfResult>.ChangedResult(
            result,
            refreshHints: NoteEditCommandHelpers.NoteChangedHints);
    }

    private static IEnumerable<EditableEvent> ResolveSelectedNotes(
        EditableTrack track,
        IReadOnlyList<NoteSelectionKey> selectedNotes)
    {
        var excludedEvents = new List<EditableEvent>();
        foreach (var noteKey in selectedNotes)
        {
            var editableEvent = NoteEditCommandHelpers.ResolveNote(track, noteKey, excludedEvents);
            if (editableEvent?.Source.Event is not NoteOnEvent)
                continue;

            excludedEvents.Add(editableEvent);
            yield return editableEvent;
        }
    }
}

public sealed record SplitSelectedNotesInHalfOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> SelectedNotes);
