using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-chords",
    "Split Chords",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Chords",
    RequiresSelectedTracks = true)]
public sealed class SplitTracksChordsCommand
    : EditorOperationBase, IEditorCommand<SplitTracksChordsCommandOptions, MidiForgeSplitChordsResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitTracksChordsCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSplitChordsResult> Execute(
        EditorCommandContext context,
        SplitTracksChordsCommandOptions commandOptions)
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
        var chordGroups = 0;
        var minimumSimultaneousNotes = Math.Clamp(options.MinimumSimultaneousNotes, 2, 10);
        var timingToleranceTicks = MidiForgeNotePrimitives.ResolveChordTimingToleranceTicks(
            file,
            options.ChordTimingTolerance);

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var splitGroups = MidiForgeNotePrimitives.SplitChordNotes(
                notes,
                track.DisplayName,
                options.Strategy,
                options.GroupMode,
                minimumSimultaneousNotes,
                timingToleranceTicks)
                .ToArray();
            if (splitGroups.Length == 0)
                continue;

            sourceTracks++;
            chordGroups += splitGroups.Count(group => group.IsChord);

            var splitTracks = splitGroups
                .Select(group => MidiForgeNotePrimitives.CreateTrackFromNotes(sourceChunk, group.TrackName, group.Notes))
                .Select(chunk => new EditableTrack(chunk, 0))
                .ToArray();

            if (options.InsertPartsAtEnd)
            {
                foreach (var splitTrack in splitTracks)
                    file.Tracks.Insert(file.Tracks.Count, splitTrack);
            }
            else
            {
                foreach (var splitTrack in splitTracks.Reverse())
                    file.Tracks.Insert(trackIndex + 1, splitTrack);
            }

            createdTracks += splitTracks.Length;
        }

        var result = new MidiForgeSplitChordsResult(sourceTracks, createdTracks, chordGroups);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeSplitChordsResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeSplitChordsResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record SplitTracksChordsCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeSplitChordsOptions Options);
