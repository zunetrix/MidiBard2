using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.extend-duration",
    "Extend Notes Duration",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Length",
    RequiresSelectedTracks = true)]
public sealed class ExtendNotesDurationCommand
    : EditorOperationBase, IEditorCommand<ExtendNotesDurationCommandOptions, MidiForgeExtendNotesDurationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ExtendNotesDurationCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeExtendNotesDurationResult> Execute(
        EditorCommandContext context,
        ExtendNotesDurationCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var maximumDurationTicks = Math.Max(0, options.MaximumDurationTicks);
        var barDurationTicks = MidiForgeNotePrimitives.GetBarDurationTicks(file);
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
                        return MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length);

                    var newLength = nextNote.Time - note.Time;
                    if (options.RespectEmptyMeasures)
                    {
                        newLength = MidiForgeNotePrimitives.LimitDurationToCurrentMeasureWhenNextMeasureIsEmpty(
                            note,
                            notes,
                            newLength,
                            barDurationTicks);
                    }

                    if (maximumDurationTicks > 0 && newLength > maximumDurationTicks)
                        newLength = maximumDurationTicks;

                    newLength = Math.Max(1, newLength);
                    if (newLength == note.Length)
                        return MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length);

                    changedNotesInTrack++;
                    return MidiForgeNotePrimitives.CloneNoteWithLength(note, newLength);
                })
                .ToArray();

            if (changedNotesInTrack == 0)
                continue;

            file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                MidiForgeNotePrimitives.CreateTrackFromNotes(
                    sourceChunk,
                    $"{track.DisplayName} (Extended)",
                    extendedNotes),
                trackIndex + 1));
            sourceTracks++;
            createdTracks++;
            changedNotes += changedNotesInTrack;
        }

        var result = new MidiForgeExtendNotesDurationResult(sourceTracks, createdTracks, changedNotes);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeExtendNotesDurationResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeExtendNotesDurationResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record ExtendNotesDurationCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeExtendNotesDurationOptions Options);
