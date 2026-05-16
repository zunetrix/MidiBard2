using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.change-lengths",
    "Change Track Note Lengths",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Length",
    RequiresSelectedTracks = true)]
public sealed class ChangeTrackNoteLengthsCommand
    : EditorOperationBase, IEditorCommand<ChangeTrackNoteLengthsCommandOptions, MidiForgeChangeNoteLengthResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ChangeTrackNoteLengthsCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeChangeNoteLengthResult> Execute(
        EditorCommandContext context,
        ChangeTrackNoteLengthsCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var options = commandOptions.Options;
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
                        return MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length);

                    changedNotesInTrack++;
                    return MidiForgeNotePrimitives.CloneNoteWithLength(note, newLengthTicks);
                })
                .ToArray();

            if (changedNotesInTrack == 0)
                continue;

            sourceTracks++;
            changedNotes += changedNotesInTrack;

            var changedChunk = MidiForgeNotePrimitives.CreateTrackFromNotes(
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

        var result = new MidiForgeChangeNoteLengthResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            changedNotes);
        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeChangeNoteLengthResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeChangeNoteLengthResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: options.DeleteOriginalTracks,
                ReloadEventList: options.DeleteOriginalTracks,
                ClearTrackSelection: true,
                ClearEventSelection: options.DeleteOriginalTracks,
                RebuildPreview: true));
    }
}

public sealed record ChangeTrackNoteLengthsCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeChangeNoteLengthOptions Options);
