using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.adapt-to-playable-range",
    "Adapt Tracks to C3-C6",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Range",
    RequiresSelectedTracks = true)]
public sealed class AdaptTracksToPlayableRangeCommand
    : EditorOperationBase, IEditorCommand<AdaptTracksToPlayableRangeCommandOptions, MidiForgeAdaptToRangeResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, AdaptTracksToPlayableRangeCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeAdaptToRangeResult> Execute(
        EditorCommandContext context,
        AdaptTracksToPlayableRangeCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var octaveShiftedTracks = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            sourceTracks++;

            var octaveShift = MidiForgeNotePrimitives.GetRangeFitOctaveShift(
                notes.Select(note => (int)(byte)note.NoteNumber),
                options.RangeStrategy);
            if (octaveShift != 0)
                octaveShiftedTracks++;

            var adaptedChunk = new TrackChunk(sourceChunk.Events.Select(midiEvent => midiEvent.Clone()));
            var changedNotesInTrack = MidiForgeNotePrimitives.AdaptChunkNoteNumbers(adaptedChunk, octaveShift);
            changedNotes += changedNotesInTrack;

            if (options.RenameTracks)
                SetTrackName(adaptedChunk, $"{track.DisplayName} (Adapted {changedNotesInTrack} notes)");

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(adaptedChunk, trackIndex + 1));
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(adaptedChunk, trackIndex);
                replacedTracks++;
            }
        }

        var result = new MidiForgeAdaptToRangeResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            octaveShiftedTracks,
            changedNotes);
        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeAdaptToRangeResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeAdaptToRangeResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: !options.CreateNewTracks,
                ReloadEventList: !options.CreateNewTracks,
                ClearTrackSelection: true,
                ClearEventSelection: !options.CreateNewTracks,
                RebuildPreview: true));
    }

    private static void SetTrackName(TrackChunk chunk, string name)
    {
        chunk.Events.RemoveAll(midiEvent => midiEvent is SequenceTrackNameEvent);
        chunk.Events.Insert(0, new SequenceTrackNameEvent(name));
    }
}

public sealed record AdaptTracksToPlayableRangeCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeAdaptToRangeOptions Options);
