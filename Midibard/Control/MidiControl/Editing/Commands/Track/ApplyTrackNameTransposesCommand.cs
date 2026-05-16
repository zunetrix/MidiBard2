using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

[EditorOperation(
    "track.apply-name-transposes",
    "Apply Track-Name Transposes",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Names",
    RequiresSelectedTracks = true)]
public sealed class ApplyTrackNameTransposesCommand
    : EditorOperationBase, IEditorCommand<ApplyTrackNameTransposesCommandOptions, MidiForgeApplyTrackNameTransposeResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ApplyTrackNameTransposesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeApplyTrackNameTransposeResult> Execute(
        EditorCommandContext context,
        ApplyTrackNameTransposesCommandOptions commandOptions)
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
        var cleanedTrackNames = 0;
        var changedNotes = 0;
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var transposeSemitones = TrackInfo.GetTransposeByName(track.Name);
            if (transposeSemitones == 0)
            {
                skippedTracks++;
                continue;
            }

            var migratedChunk = new TrackChunk(track.CloneCurrentChunk().Events.Select(midiEvent => midiEvent.Clone()));
            var cleanedTrackName = TrackInfo.RemoveTransposeFromTrackName(track.Name);
            changedNotes += TransposeChunkNotes(migratedChunk, transposeSemitones);
            SetTrackName(migratedChunk, cleanedTrackName);

            if (!string.Equals(track.Name, cleanedTrackName, StringComparison.Ordinal))
                cleanedTrackNames++;

            sourceTracks++;

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(migratedChunk, trackIndex + 1));
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(migratedChunk, trackIndex);
                replacedTracks++;
            }
        }

        var result = new MidiForgeApplyTrackNameTransposeResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            cleanedTrackNames,
            changedNotes,
            skippedTracks);

        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeApplyTrackNameTransposeResult>.UnchangedResult(result);

        RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeApplyTrackNameTransposeResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: true,
                ReloadEventList: !options.CreateNewTracks,
                ClearTrackSelection: true,
                ClearEventSelection: !options.CreateNewTracks,
                RebuildPreview: true));
    }

    private static int TransposeChunkNotes(TrackChunk chunk, int semitones)
    {
        var changedNotes = chunk.GetNotes()
            .Count(note => Math.Clamp((byte)note.NoteNumber + semitones, 0, 127) != (byte)note.NoteNumber);

        foreach (var midiEvent in chunk.Events)
        {
            if (midiEvent is NoteEvent noteEvent)
                noteEvent.NoteNumber = (SevenBitNumber)(byte)Math.Clamp((byte)noteEvent.NoteNumber + semitones, 0, 127);
        }

        return changedNotes;
    }

    private static void SetTrackName(TrackChunk chunk, string name)
    {
        chunk.Events.RemoveAll(midiEvent => midiEvent is SequenceTrackNameEvent);
        chunk.Events.Insert(0, new SequenceTrackNameEvent(name));
    }

    private static void RefreshTrackIndexes(EditableMidiFile file)
    {
        for (int i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
    }
}

public sealed record ApplyTrackNameTransposesCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeApplyTrackNameTransposeOptions Options);
