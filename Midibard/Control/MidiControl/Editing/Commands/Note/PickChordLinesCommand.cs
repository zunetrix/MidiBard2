using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.pick-chord-lines",
    "Pick Chord Lines",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Chords",
    RequiresSelectedTracks = true)]
public sealed class PickChordLinesCommand
    : EditorOperationBase, IEditorCommand<PickChordLinesCommandOptions, MidiForgePickChordLinesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, PickChordLinesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgePickChordLinesResult> Execute(
        EditorCommandContext context,
        PickChordLinesCommandOptions commandOptions)
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
        var pickedParts = 0;
        var maxSimultaneousNotes = Math.Clamp(options.MaxSimultaneousNotes, 1, 3);
        var outputTrackRefs = new List<EditableTrack>();

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
                MidiForgeChordSplitStrategy.SameStartTick,
                MidiForgeChordGroupMode.GroupMerged,
                2)
                .Where(group => ShouldPickChordLine(group, maxSimultaneousNotes, options.PickStrategy))
                .ToArray();
            if (splitGroups.Length == 0)
                continue;

            sourceTracks++;
            pickedParts += splitGroups.Length;

            var pickedNotes = splitGroups
                .SelectMany(group => group.Notes)
                .ToArray();
            var outputTrackName = options.RenameTracks
                ? $"{track.DisplayName} (Auto Edited Max {maxSimultaneousNotes})"
                : track.DisplayName;
            var outputChunk = MidiForgeNotePrimitives.CreateTrackFromNotes(
                sourceChunk,
                outputTrackName,
                pickedNotes);
            var outputTrack = new EditableTrack(outputChunk, options.CreateNewTracks ? trackIndex + 1 : trackIndex);

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, outputTrack);
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = outputTrack;
                replacedTracks++;
            }

            outputTrackRefs.Add(outputTrack);
        }

        if (createdTracks > 0 || replacedTracks > 0)
            MidiForgeNotePrimitives.RefreshTrackIndexes(file);

        var outputTrackIndices = outputTrackRefs
            .Select(track => file.Tracks.IndexOf(track))
            .Where(index => index >= 0)
            .OrderBy(index => index)
            .ToArray();
        var result = new MidiForgePickChordLinesResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            pickedParts,
            outputTrackIndices);

        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgePickChordLinesResult>.UnchangedResult(result);

        return EditorCommandResult<MidiForgePickChordLinesResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: !options.CreateNewTracks,
                ReloadEventList: !options.CreateNewTracks,
                ClearTrackSelection: true,
                ClearEventSelection: !options.CreateNewTracks,
                RebuildPreview: true));
    }

    private static bool ShouldPickChordLine(
        MidiForgeChordSplitGroup group,
        int maxSimultaneousNotes,
        MidiForgeChordPickStrategy pickStrategy)
    {
        if (!group.IsChord || group.Order == 1)
            return true;

        if (maxSimultaneousNotes <= 1)
            return false;

        if (maxSimultaneousNotes == 2)
        {
            if (pickStrategy == MidiForgeChordPickStrategy.OddChords && group.GroupSize >= 3)
                return group.Order == 3;

            return group.Order == 2;
        }

        return group.Order is 2 or 3;
    }
}

public sealed record PickChordLinesCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgePickChordLinesOptions Options);
