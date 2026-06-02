using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.glue-same-pitch",
    "Glue Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Overlaps")]
public sealed class GlueNotesCommand
    : EditorOperationBase, IEditorCommand<GlueNotesOptions, GlueNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, GlueNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.SelectedNotes is null || options.SelectedNotes.Count < 2)
            return EditorCommandValidation.Failure("Select at least two notes to glue.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<GlueNotesResult> Execute(
        EditorCommandContext context,
        GlueNotesOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        if (track.Events is null)
            return EditorCommandResult<GlueNotesResult>.NoChange("Load the track before editing notes.");

        var resolvedNotes = ResolveSelectedNotes(track, options.SelectedNotes).ToArray();
        if (resolvedNotes.Length < 2)
            return EditorCommandResult<GlueNotesResult>.UnchangedResult(
                new GlueNotesResult(resolvedNotes.Length, resolvedNotes.Length, 0));

        var groups = resolvedNotes
            .Where(n => n.Source.Event is NoteOnEvent)
            .GroupBy(n => (byte)((NoteOnEvent)n.Source.Event).NoteNumber)
            .Where(g => g.Count() >= 2)
            .ToArray();

        if (groups.Length == 0)
            return EditorCommandResult<GlueNotesResult>.UnchangedResult(
                new GlueNotesResult(resolvedNotes.Length, resolvedNotes.Length, 0));

        var eventsToRemove = new List<EditableEvent>();
        var gluedGroups = 0;

        foreach (var group in groups)
        {
            var sorted = group.OrderBy(n => n.Tick).ToArray();
            var first = sorted[0];
            var last = sorted[^1];
            var newEndTick = last.Tick + last.DurationTicks;
            var newDuration = newEndTick - first.Tick;

            if (newDuration <= 0)
                continue;

            var noteOn = (NoteOnEvent)first.Source.Event;
            first.EditTick = (int)first.Tick;
            first.EditValue1 = (byte)noteOn.NoteNumber;
            first.EditValue2 = (byte)noteOn.Velocity;
            first.EditDuration = (int)newDuration;
            first.ApplyEditValues();

            for (int i = 1; i < sorted.Length; i++)
                eventsToRemove.Add(sorted[i]);

            gluedGroups++;
        }

        if (eventsToRemove.Count == 0)
            return EditorCommandResult<GlueNotesResult>.UnchangedResult(
                new GlueNotesResult(resolvedNotes.Length, resolvedNotes.Length, 0));

        foreach (var ev in eventsToRemove)
            track.RemoveEvent(ev);

        var outputNotes = resolvedNotes.Length - eventsToRemove.Count;
        var result = new GlueNotesResult(resolvedNotes.Length, outputNotes, gluedGroups);

        return EditorCommandResult<GlueNotesResult>.ChangedResult(
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

public sealed record GlueNotesOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> SelectedNotes);
