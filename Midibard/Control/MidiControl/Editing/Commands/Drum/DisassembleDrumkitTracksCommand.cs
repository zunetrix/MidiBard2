using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Drum;

[EditorOperation(
    "drum.disassemble-drumkit-tracks",
    "Disassemble Drumkit Tracks",
    Scope = EditorOperationScope.Drum,
    MenuPath = "Drum/Drumkit",
    RequiresSelectedTracks = true)]
public sealed class DisassembleDrumkitTracksCommand
    : EditorOperationBase, IEditorCommand<DisassembleDrumkitTracksCommandOptions, MidiForgeDisassembleDrumkitResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DisassembleDrumkitTracksCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one drumkit track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeDisassembleDrumkitResult> Execute(
        EditorCommandContext context,
        DisassembleDrumkitTracksCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(
            file,
            commandOptions.TrackIndices);

        var sourceTracks = 0;
        var createdTracks = 0;
        var sourceTrackRefs = new List<EditableTrack>();

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var drumNotes = sourceChunk.GetNotes()
                .Where(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel)
                .ToArray();
            if (drumNotes.Length == 0)
                continue;

            var createdFromSource = 0;

            foreach (var group in drumNotes
                .GroupBy(note => (byte)note.NoteNumber)
                .OrderBy(group => group.Key))
            {
                var trackName = MidiForgeDrumMaps.GetDrumkitInstrumentName(group.Key);
                file.Tracks.Add(new EditableTrack(
                    MidiForgeNotePrimitives.CreateTrackFromNotes(
                        sourceChunk,
                        trackName,
                        group.Select(note => MidiForgeNotePrimitives.CloneNoteWithNumber(note, group.Key))),
                    file.Tracks.Count));
                createdTracks++;
                createdFromSource++;
            }

            if (createdFromSource > 0)
            {
                sourceTracks++;
                sourceTrackRefs.Add(track);
            }
        }

        var deletedSourceTracks = 0;
        if (commandOptions.Options.DeleteOriginalTracks && sourceTrackRefs.Count > 0)
        {
            foreach (var track in sourceTrackRefs)
            {
                var index = file.Tracks.IndexOf(track);
                if (index < 0)
                    continue;

                track.Dispose();
                file.Tracks.RemoveAt(index);
                deletedSourceTracks++;
            }
        }

        var result = new MidiForgeDisassembleDrumkitResult(
            sourceTracks,
            createdTracks,
            deletedSourceTracks);
        if (createdTracks == 0 && deletedSourceTracks == 0)
            return EditorCommandResult<MidiForgeDisassembleDrumkitResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeDisassembleDrumkitResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true));
    }
}

public sealed record DisassembleDrumkitTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeDisassembleDrumkitOptions Options);
