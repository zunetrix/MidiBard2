using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Drum;

[EditorOperation(
    "drum.transpose-single-note-tracks",
    "Retarget Single-Note Drum Tracks",
    Scope = EditorOperationScope.Drum,
    MenuPath = "Drum/Drumkit",
    RequiresSelectedTracks = true)]
public sealed class TransposeSingleNoteTracksToDrumNoteCommand
    : EditorOperationBase, IEditorCommand<TransposeSingleNoteTracksToDrumNoteCommandOptions, MidiForgeTransposeToDrumNoteResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, TransposeSingleNoteTracksToDrumNoteCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one single-note track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeTransposeToDrumNoteResult> Execute(
        EditorCommandContext context,
        TransposeSingleNoteTracksToDrumNoteCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var targetNote = Math.Clamp(options.TargetNote, 0, 127);
        var sourceTracks = validTrackIndices.Length;
        var createdTracks = 0;
        var deletedSourceTracks = 0;
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            var uniqueNoteNumbers = notes
                .Select(note => (int)(byte)note.NoteNumber)
                .Distinct()
                .Take(2)
                .ToArray();

            if (uniqueNoteNumbers.Length != 1)
            {
                skippedTracks++;
                continue;
            }

            var transposeSemitones = targetNote - uniqueNoteNumbers[0];
            var trackName = string.IsNullOrWhiteSpace(options.TrackName)
                ? $"{track.DisplayName} (Transposed {transposeSemitones})"
                : options.TrackName.Trim();
            var transposedChunk = MidiForgeNotePrimitives.CreateTrackFromNotes(
                sourceChunk,
                trackName,
                notes.Select(note => MidiForgeNotePrimitives.CloneNoteWithNumber(note, targetNote)));

            if (options.DeleteOriginalTracks)
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(transposedChunk, trackIndex);
                deletedSourceTracks++;
            }
            else
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(transposedChunk, trackIndex + 1));
            }

            createdTracks++;
        }

        var result = new MidiForgeTransposeToDrumNoteResult(
            sourceTracks,
            createdTracks,
            deletedSourceTracks,
            skippedTracks);
        if (createdTracks == 0 && deletedSourceTracks == 0)
            return EditorCommandResult<MidiForgeTransposeToDrumNoteResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeTransposeToDrumNoteResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: options.DeleteOriginalTracks,
                ReloadEventList: options.DeleteOriginalTracks,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true));
    }
}

public sealed record TransposeSingleNoteTracksToDrumNoteCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeTransposeToDrumNoteOptions Options);
