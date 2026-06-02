using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-at-position",
    "Split Notes at Position",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Length")]
public sealed class SplitAtPositionCommand
    : EditorOperationBase, IEditorCommand<SplitAtPositionOptions, SplitAtPositionResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitAtPositionOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.SplitTick <= 0)
            return EditorCommandValidation.Failure("Split position must be after tick 0.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<SplitAtPositionResult> Execute(
        EditorCommandContext context,
        SplitAtPositionOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        if (track.Events is null)
            return EditorCommandResult<SplitAtPositionResult>.NoChange("Load the track before editing notes.");

        var splitTick = options.SplitTick;
        var notesToSplit = new List<EditableEvent>();

        foreach (var ev in track.Events)
        {
            if (ev.NoteOffSource is null || ev.Source.Event is not NoteOnEvent)
                continue;

            if (ev.Tick < splitTick && ev.Tick + ev.DurationTicks > splitTick)
                notesToSplit.Add(ev);
        }

        if (notesToSplit.Count == 0)
            return EditorCommandResult<SplitAtPositionResult>.UnchangedResult(
                new SplitAtPositionResult(0, 0));

        var splitCount = 0;
        foreach (var ev in notesToSplit)
        {
            if (ev.Source.Event is not NoteOnEvent noteOn)
                continue;

            var originalEndTick = ev.Tick + ev.DurationTicks;
            var firstHalfDuration = splitTick - ev.Tick;
            var secondHalfDuration = originalEndTick - splitTick;

            if (firstHalfDuration <= 0 || secondHalfDuration <= 0)
                continue;

            ev.EditDuration = (int)firstHalfDuration;
            ev.ApplyEditValues();

            track.InsertNote(
                splitTick,
                (byte)noteOn.NoteNumber,
                (byte)noteOn.Velocity,
                secondHalfDuration);

            splitCount++;
        }

        var result = new SplitAtPositionResult(splitCount, 0);
        return EditorCommandResult<SplitAtPositionResult>.ChangedResult(
            result,
            refreshHints: NoteEditCommandHelpers.NoteChangedHints);
    }
}

public sealed record SplitAtPositionOptions(
    int TrackIndex,
    long SplitTick);
