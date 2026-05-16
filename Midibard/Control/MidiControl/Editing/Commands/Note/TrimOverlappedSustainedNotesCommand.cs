using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.trim-overlapped-sustained-notes",
    "Trim Overlapped Sustained Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Overlaps",
    RequiresSelectedTracks = true)]
public sealed class TrimOverlappedSustainedNotesCommand
    : EditorOperationBase, IEditorCommand<TrimOverlappedSustainedNotesCommandOptions, MidiForgeTrimOverlappedNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, TrimOverlappedSustainedNotesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeTrimOverlappedNotesResult> Execute(
        EditorCommandContext context,
        TrimOverlappedSustainedNotesCommandOptions options)
    {
        var file = context.File;
        var validTrackIndices = options.TrackIndices
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
                        .Where(other => MidiForgeNotePrimitives.NotesOverlap(note, other))
                        .Where(other => other.Time > note.Time)
                        .Select(other => other.Time)
                        .OrderBy(time => time)
                        .Cast<long?>()
                        .FirstOrDefault();

                    if (overlapStart == null)
                        return MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length);

                    var newLength = Math.Max(1, overlapStart.Value - note.Time);
                    if (newLength == note.Length)
                        return MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length);

                    changedNotesInTrack++;
                    return MidiForgeNotePrimitives.CloneNoteWithLength(note, newLength);
                })
                .ToArray();

            if (changedNotesInTrack == 0)
                continue;

            file.Tracks.Insert(trackIndex + 1, new EditableTrack(
                MidiForgeNotePrimitives.CreateTrackFromNotes(sourceChunk, $"{track.DisplayName} (Trimmed)", trimmedNotes),
                trackIndex + 1));
            sourceTracks++;
            createdTracks++;
            changedNotes += changedNotesInTrack;
        }

        var result = new MidiForgeTrimOverlappedNotesResult(sourceTracks, createdTracks, changedNotes);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeTrimOverlappedNotesResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeTrimOverlappedNotesResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record TrimOverlappedSustainedNotesCommandOptions(IReadOnlyList<int> TrackIndices);
