using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

using MidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-notes-into-tracks",
    "Split Notes Into Tracks",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Arrangement",
    RequiresSelectedTracks = true)]
public sealed class SplitNotesIntoTracksCommand
    : EditorOperationBase, IEditorCommand<SplitNotesIntoTracksCommandOptions, MidiForgeSplitNotesIntoTracksResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitNotesIntoTracksCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSplitNotesIntoTracksResult> Execute(
        EditorCommandContext context,
        SplitNotesIntoTracksCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();
        var numberOfTracks = Math.Clamp(commandOptions.Options.NumberOfTracks, 1, 64);
        var everyNotesAmount = Math.Max(1, commandOptions.Options.EveryNotesAmount);
        var sourceTracks = 0;
        var createdTracks = 0;
        var distributedNotes = 0;

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

            var splitNotes = Enumerable.Range(0, numberOfTracks)
                .Select(_ => new List<MidiNote>())
                .ToArray();
            var destinationTrackIndex = 0;
            var noteCountInDestination = 0;

            foreach (var note in notes)
            {
                if (destinationTrackIndex >= numberOfTracks)
                    destinationTrackIndex = 0;

                splitNotes[destinationTrackIndex].Add(MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length));
                noteCountInDestination++;

                if (noteCountInDestination == everyNotesAmount)
                    noteCountInDestination = 0;

                if (noteCountInDestination == 0)
                    destinationTrackIndex++;
            }

            var newTracks = splitNotes
                .Select((notesGroup, index) => (notesGroup, index))
                .Where(group => group.notesGroup.Count > 0)
                .Select(group => new EditableTrack(
                    MidiForgeNotePrimitives.CreateTrackFromNotes(
                        sourceChunk,
                        $"{track.DisplayName} (Group {group.index + 1})",
                        group.notesGroup),
                    0))
                .ToList();

            if (newTracks.Count == 0)
                continue;

            file.Tracks.InsertRange(trackIndex + 1, newTracks);
            sourceTracks++;
            createdTracks += newTracks.Count;
            distributedNotes += notes.Length;
        }

        var result = new MidiForgeSplitNotesIntoTracksResult(
            sourceTracks,
            createdTracks,
            distributedNotes);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeSplitNotesIntoTracksResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeSplitNotesIntoTracksResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record SplitNotesIntoTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeSplitNotesIntoTracksOptions Options);
