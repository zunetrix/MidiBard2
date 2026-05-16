using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiNote = Melanchall.DryWetMidi.Interaction.Note;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Drum;

[EditorOperation(
    "drum.split-drumkit-tracks",
    "Split Drumkit Tracks",
    Scope = EditorOperationScope.Drum,
    MenuPath = "Drum/Drumkit",
    RequiresSelectedTracks = true)]
public sealed class SplitDrumkitTracksCommand
    : EditorOperationBase, IEditorCommand<SplitDrumkitTracksCommandOptions, MidiForgeSplitDrumkitResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitDrumkitTracksCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one drumkit track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSplitDrumkitResult> Execute(
        EditorCommandContext context,
        SplitDrumkitTracksCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var sourceTracks = 0;
        var createdTracks = 0;
        var restTracks = 0;
        var autoEditedTracks = 0;
        var transposedNotes = 0;
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
            sourceTracks++;

            foreach (var mapping in MidiForgeDrumMaps.DefaultDrumkitMappings.Where(mapping => mapping.SourceNotes.Count > 0))
            {
                var mappedNotes = drumNotes
                    .Where(note => mapping.SourceNotes.Contains((byte)note.NoteNumber))
                    .Select(note =>
                    {
                        var sourceNoteNumber = (byte)note.NoteNumber;
                        var outputNoteNumber = MidiForgeDrumMaps.TransposeToOutputNote(
                            sourceNoteNumber,
                            options.TransposePreset);
                        if (outputNoteNumber != sourceNoteNumber)
                            transposedNotes++;

                        return MidiForgeNotePrimitives.CloneNoteWithNumber(note, outputNoteNumber);
                    })
                    .ToArray();

                if (mappedNotes.Length == 0)
                    continue;

                if (options.AutoEditAfterSplit)
                    mappedNotes = AutoEditDrumNotes(mappedNotes, mapping.TrackName, ref autoEditedTracks, ref transposedNotes);

                file.Tracks.Add(new EditableTrack(
                    MidiForgeNotePrimitives.CreateTrackFromNotes(sourceChunk, mapping.TrackName, mappedNotes),
                    file.Tracks.Count));
                createdTracks++;
                createdFromSource++;
            }

            if (options.CreateRestTrack)
            {
                var restNotes = drumNotes
                    .Where(note => !MidiForgeDrumMaps.IsMappedSourceNote((byte)note.NoteNumber))
                    .Select(note => MidiForgeNotePrimitives.CloneNoteWithNumber(note, (byte)note.NoteNumber))
                    .ToArray();

                if (restNotes.Length > 0)
                {
                    file.Tracks.Add(new EditableTrack(
                        MidiForgeNotePrimitives.CreateTrackFromNotes(sourceChunk, MidiForgeDrumMaps.RestTrackName, restNotes),
                        file.Tracks.Count));
                    createdTracks++;
                    createdFromSource++;
                    restTracks++;
                }
            }

            if (createdFromSource > 0)
                sourceTrackRefs.Add(track);
        }

        var deletedSourceTracks = 0;
        if (createdTracks > 0 && options.DeleteOriginalTracks)
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
        else if (createdTracks > 0 && options.MoveSourceTracksToEnd)
        {
            MoveTracksToEnd(file, sourceTrackRefs);
        }

        var result = new MidiForgeSplitDrumkitResult(
            sourceTracks,
            createdTracks,
            restTracks,
            autoEditedTracks,
            transposedNotes,
            deletedSourceTracks);
        if (createdTracks == 0 && deletedSourceTracks == 0)
            return EditorCommandResult<MidiForgeSplitDrumkitResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeSplitDrumkitResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true));
    }

    private static MidiNote[] AutoEditDrumNotes(
        MidiNote[] notes,
        string trackName,
        ref int autoEditedTracks,
        ref int transposedNotes)
    {
        var pickedNotes = MidiForgeNotePrimitives.SplitChordNotes(
            notes,
            trackName,
            MidiForgeChordSplitStrategy.SameStartTick,
            MidiForgeChordGroupMode.GroupMerged,
            2)
            .Where(group => !group.IsChord || group.Order == 1)
            .SelectMany(group => group.Notes)
            .ToArray();

        var changed = pickedNotes.Length != notes.Length;

        if (pickedNotes.Count(note => (byte)note.NoteNumber == MidiForgeAnalysis.PlayableLowestMidiNote) > pickedNotes.Length / 2)
        {
            pickedNotes = pickedNotes
                .Select(note => MidiForgeNotePrimitives.CloneNoteWithNumber(
                    note,
                    Math.Clamp((byte)note.NoteNumber + 4, 0, 127)))
                .ToArray();
            transposedNotes += pickedNotes.Length;
            changed = true;
        }

        if (changed)
            autoEditedTracks++;

        return pickedNotes;
    }

    private static void MoveTracksToEnd(EditableMidiFile file, IEnumerable<EditableTrack> tracks)
    {
        foreach (var track in tracks)
        {
            var index = file.Tracks.IndexOf(track);
            if (index < 0)
                continue;

            file.Tracks.RemoveAt(index);
            file.Tracks.Add(track);
        }
    }
}

public sealed record SplitDrumkitTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeSplitDrumkitOptions Options);
