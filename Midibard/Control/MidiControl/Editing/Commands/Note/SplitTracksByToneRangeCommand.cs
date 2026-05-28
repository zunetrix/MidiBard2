using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-by-tone-range",
    "Split Tracks by Tone Range",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Range",
    RequiresSelectedTracks = true)]
public sealed class SplitTracksByToneRangeCommand
    : EditorOperationBase, IEditorCommand<SplitTracksByToneRangeCommandOptions, MidiForgeSplitNotesRangeResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitTracksByToneRangeCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSplitNotesRangeResult> Execute(
        EditorCommandContext context,
        SplitTracksByToneRangeCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var minimumNote = Math.Clamp(options.ResolvedMinimum, 0, 127);
        var maximumNote = Math.Clamp(options.ResolvedMaximum, 0, 127);
        if (minimumNote > maximumNote)
            (minimumNote, maximumNote) = (maximumNote, minimumNote);

        var rangeLabel = $"{MidiForgeNotePrimitives.GetMidiNoteName(minimumNote)} ({minimumNote}) - "
            + $"{MidiForgeNotePrimitives.GetMidiNoteName(maximumNote)} ({maximumNote})";
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
                .Where(note =>
                {
                    var noteNumber = (byte)note.NoteNumber;
                    return noteNumber >= minimumNote && noteNumber <= maximumNote;
                })
                .Select(note => MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length))
                .ToArray();
            var outOfRangeNotes = notes
                .Where(note =>
                {
                    var noteNumber = (byte)note.NoteNumber;
                    return noteNumber < minimumNote || noteNumber > maximumNote;
                })
                .Select(note => MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length))
                .ToArray();

            sourceTracks++;

            if (outOfRangeNotes.Length > 0)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                    MidiForgeNotePrimitives.CreateTrackFromNotes(
                        sourceChunk,
                        $"{track.DisplayName} (Out of Range {rangeLabel})",
                        outOfRangeNotes),
                    trackIndex + 1));
                createdTracks++;
                outOfRangeTracks++;
                outOfRangeNotesTotal += outOfRangeNotes.Length;
            }

            if (inRangeNotes.Length > 0)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                    MidiForgeNotePrimitives.CreateTrackFromNotes(
                        sourceChunk,
                        $"{track.DisplayName} (In Range {rangeLabel})",
                        inRangeNotes),
                    trackIndex + 1));
                createdTracks++;
                inRangeTracks++;
                inRangeNotesTotal += inRangeNotes.Length;
            }
        }

        var result = new MidiForgeSplitNotesRangeResult(
            sourceTracks,
            createdTracks,
            inRangeTracks,
            outOfRangeTracks,
            inRangeNotesTotal,
            outOfRangeNotesTotal);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeSplitNotesRangeResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeSplitNotesRangeResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record SplitTracksByToneRangeCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeSplitToneRangeOptions Options);
