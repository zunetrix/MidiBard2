using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Extensions.DryWetMidi;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

public sealed record TrackMutationResult(
    int ChangedTracks,
    IReadOnlyList<int> CreatedTrackIndices,
    IReadOnlyList<int> RemovedTrackIndices);

[EditorOperation(
    "track.create-blank",
    "Add Blank Track",
    Scope = EditorOperationScope.Track)]
public sealed class CreateBlankTrackCommand
    : EditorOperationBase, IEditorCommand<CreateBlankTrackOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, CreateBlankTrackOptions options)
        => EditorCommandValidation.Success;

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        CreateBlankTrackOptions options)
    {
        var file = context.File;
        var insertIndex = GetBlankTrackInsertIndex(file, options.InsertAfterTrackIndex);
        var newTrack = new EditableTrack(new TrackChunk(), insertIndex);

        file.Tracks.Insert(insertIndex, newTrack);
        ReindexTracks(file);

        var result = new TrackMutationResult(
            1,
            new[] { insertIndex },
            Array.Empty<int>());

        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            result,
            refreshHints: TrackListChangedHints(clearSelectedTrack: false));
    }

    private static int GetBlankTrackInsertIndex(EditableMidiFile file, int? insertAfterTrackIndex)
    {
        if (insertAfterTrackIndex is { } index && IsPerformanceTrackIndex(file, index))
            return index + 1;

        return file.Tracks.Count;
    }
}

public sealed record CreateBlankTrackOptions(int? InsertAfterTrackIndex = null);

[EditorOperation(
    "track.clone",
    "Clone Tracks",
    Scope = EditorOperationScope.Track,
    RequiresSelectedTracks = true)]
public sealed class CloneTracksCommand
    : EditorOperationBase, IEditorCommand<CloneTracksOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, CloneTracksOptions options)
        => ValidatePerformanceTrackSelection(context, options.TrackIndices);

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        CloneTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        var createdTrackIndices = new List<int>();

        foreach (var trackIndex in validTrackIndices.OrderByDescending(index => index))
        {
            if (!CloneTrack(file, trackIndex))
                continue;

            var insertedIndex = trackIndex + 1;
            for (var i = 0; i < createdTrackIndices.Count; i++)
            {
                if (createdTrackIndices[i] >= insertedIndex)
                    createdTrackIndices[i]++;
            }

            createdTrackIndices.Add(insertedIndex);
        }

        var result = new TrackMutationResult(
            createdTrackIndices.Count,
            createdTrackIndices.OrderBy(index => index).ToArray(),
            Array.Empty<int>());

        if (result.ChangedTracks == 0)
            return EditorCommandResult<TrackMutationResult>.UnchangedResult(result);

        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            result,
            refreshHints: TrackListChangedHints(clearSelectedTrack: false));
    }
}

public sealed record CloneTracksOptions(IReadOnlyList<int> TrackIndices);

[EditorOperation(
    "track.delete",
    "Delete Tracks",
    Scope = EditorOperationScope.Track,
    RequiresSelectedTracks = true)]
public sealed class DeleteTracksCommand
    : EditorOperationBase, IEditorCommand<DeleteTracksOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DeleteTracksOptions options)
        => ValidatePerformanceTrackSelection(context, options.TrackIndices);

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        DeleteTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices)
            .OrderByDescending(index => index)
            .ToArray();
        var removedTrackIndices = new List<int>();

        foreach (var trackIndex in validTrackIndices)
        {
            if (!RemoveTrack(file, trackIndex))
                continue;

            removedTrackIndices.Add(trackIndex);
        }

        var result = new TrackMutationResult(
            removedTrackIndices.Count,
            Array.Empty<int>(),
            removedTrackIndices.OrderBy(index => index).ToArray());

        if (result.ChangedTracks == 0)
            return EditorCommandResult<TrackMutationResult>.UnchangedResult(result);

        var clearSelectedTrack = removedTrackIndices.Contains(context.Selection.SelectedTrackIndex);
        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            result,
            refreshHints: TrackListChangedHints(clearSelectedTrack));
    }
}

public sealed record DeleteTracksOptions(IReadOnlyList<int> TrackIndices);

[EditorOperation(
    "track.reorder",
    "Reorder Track",
    Scope = EditorOperationScope.Track)]
public sealed class ReorderTrackCommand
    : EditorOperationBase, IEditorCommand<ReorderTrackOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ReorderTrackOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.FromIndex))
            return EditorCommandValidation.Failure("Choose a performance track to move.");

        if (!IsPerformanceTrackIndex(context.File, options.ToIndex))
            return EditorCommandValidation.Failure("Choose a performance track destination.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        ReorderTrackOptions options)
    {
        if (options.FromIndex == options.ToIndex)
        {
            return EditorCommandResult<TrackMutationResult>.UnchangedResult(
                new TrackMutationResult(0, Array.Empty<int>(), Array.Empty<int>()));
        }

        MoveTrack(context.File, options.FromIndex, options.ToIndex);

        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            new TrackMutationResult(1, Array.Empty<int>(), Array.Empty<int>()),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}

public sealed record ReorderTrackOptions(int FromIndex, int ToIndex);

[EditorOperation(
    "track.rename",
    "Rename Track",
    Scope = EditorOperationScope.Track)]
public sealed class RenameTrackCommand
    : EditorOperationBase, IEditorCommand<RenameTrackOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, RenameTrackOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track to rename.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        RenameTrackOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        if (!MidiForgeTrackNamePrimitives.SetEditableTrackName(track, options.Name ?? string.Empty))
        {
            return EditorCommandResult<TrackMutationResult>.UnchangedResult(
                new TrackMutationResult(0, Array.Empty<int>(), Array.Empty<int>()));
        }

        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            new TrackMutationResult(1, Array.Empty<int>(), Array.Empty<int>()),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: context.Selection.SelectedTrackIndex == options.TrackIndex,
                RebuildPreview: true));
    }
}

public sealed record RenameTrackOptions(int TrackIndex, string Name);

[EditorOperation(
    "track.set-channel",
    "Set Track Channel",
    Scope = EditorOperationScope.Track)]
public sealed class SetTrackChannelCommand
    : EditorOperationBase, IEditorCommand<SetTrackChannelOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SetTrackChannelOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track to change channel.");

        if (options.Channel < 0 || options.Channel > 15)
            return EditorCommandValidation.Failure("Choose a MIDI channel from 1 to 16.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        SetTrackChannelOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        if (track.Channel == options.Channel)
        {
            return EditorCommandResult<TrackMutationResult>.UnchangedResult(
                new TrackMutationResult(0, Array.Empty<int>(), Array.Empty<int>()));
        }

        track.SetChannel(options.Channel);

        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            new TrackMutationResult(1, Array.Empty<int>(), Array.Empty<int>()),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: context.Selection.SelectedTrackIndex == options.TrackIndex,
                ReloadEventList: context.Selection.SelectedTrackIndex == options.TrackIndex,
                ClearEventSelection: context.Selection.SelectedTrackIndex == options.TrackIndex,
                RebuildPreview: true));
    }
}

public sealed record SetTrackChannelOptions(int TrackIndex, int Channel);

[EditorOperation(
    "track.split-by-channel",
    "Split Track by Channel",
    Scope = EditorOperationScope.Track)]
public sealed class SplitTrackByChannelCommand
    : EditorOperationBase, IEditorCommand<SplitTrackByChannelOptions, TrackMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitTrackByChannelOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track to split.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackMutationResult> Execute(
        EditorCommandContext context,
        SplitTrackByChannelOptions options)
    {
        var file = context.File;
        var beforeCount = file.Tracks.Count;

        if (!SplitTrackByChannel(file, options.TrackIndex))
        {
            return EditorCommandResult<TrackMutationResult>.UnchangedResult(
                new TrackMutationResult(0, Array.Empty<int>(), Array.Empty<int>()));
        }

        var createdCount = Math.Max(0, file.Tracks.Count - beforeCount + 1);
        var createdTrackIndices = Enumerable
            .Range(options.TrackIndex, createdCount)
            .Where(index => index >= 0 && index < file.Tracks.Count)
            .ToArray();

        return EditorCommandResult<TrackMutationResult>.ChangedResult(
            new TrackMutationResult(1, createdTrackIndices, new[] { options.TrackIndex }),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: true,
                ReloadEventList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}

public sealed record SplitTrackByChannelOptions(int TrackIndex);

internal static class TrackCrudCommandHelpers
{
    public static EditorCommandValidation ValidatePerformanceTrackSelection(
        EditorCommandContext context,
        IReadOnlyList<int> trackIndices)
    {
        if (trackIndices is null || trackIndices.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one performance track.");

        return GetPerformanceTrackIndices(context.File, trackIndices).Length == 0
            ? EditorCommandValidation.Failure("Choose at least one performance track.")
            : EditorCommandValidation.Success;
    }

    public static int[] GetPerformanceTrackIndices(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
        => MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(file, trackIndices);

    public static bool IsPerformanceTrackIndex(EditableMidiFile file, int trackIndex)
        => trackIndex >= 0
           && trackIndex < file.Tracks.Count
           && !file.Tracks[trackIndex].IsConductorTrack;

    public static bool CloneTrack(EditableMidiFile file, int index)
    {
        if (!IsPerformanceTrackIndex(file, index))
            return false;

        var source = file.Tracks[index];
        source.FlushChanges();

        var cloneChunk = new TrackChunk(source.Chunk.Events.Select(midiEvent => midiEvent.Clone()));
        var newTrack = new EditableTrack(cloneChunk, index + 1);

        file.Tracks.Insert(index + 1, newTrack);
        ReindexTracks(file);
        return true;
    }

    public static bool RemoveTrack(EditableMidiFile file, int index)
    {
        if (index < 0 || index >= file.Tracks.Count)
            return false;

        file.Tracks[index].Dispose();
        file.Tracks.RemoveAt(index);
        ReindexTracks(file);
        return true;
    }

    public static bool MoveTrack(EditableMidiFile file, int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex
            || fromIndex < 0 || toIndex < 0
            || fromIndex >= file.Tracks.Count || toIndex >= file.Tracks.Count)
        {
            return false;
        }

        var track = file.Tracks[fromIndex];
        file.Tracks.RemoveAt(fromIndex);
        file.Tracks.Insert(toIndex, track);
        ReindexTracks(file);
        return true;
    }

    public static bool SplitTrackByChannel(EditableMidiFile file, int trackIndex)
    {
        if (!IsPerformanceTrackIndex(file, trackIndex))
            return false;

        var track = file.Tracks[trackIndex];
        track.FlushChanges();

        using var manager = track.Chunk.ManageTimedEvents();
        var channelGroups = manager.Objects
            .Where(timedEvent => timedEvent.Event is ChannelEvent)
            .GroupBy(timedEvent => (byte)((ChannelEvent)timedEvent.Event).Channel)
            .OrderBy(group => group.Key)
            .ToList();

        if (channelGroups.Count <= 1)
            return false;

        var nonChannelEvents = manager.Objects
            .Where(timedEvent => timedEvent.Event is not ChannelEvent)
            .ToList();

        var newTracks = new List<EditableTrack>();
        if (nonChannelEvents.Count > 0)
        {
            var conductorChunk = new TrackChunk();
            using (var conductorManager = conductorChunk.ManageTimedEvents())
            {
                foreach (var timedEvent in nonChannelEvents)
                    conductorManager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), timedEvent.Time));
            }

            newTracks.Add(new EditableTrack(conductorChunk, 0));
        }

        const byte drumChannel = 9;
        var programToChannel = new Dictionary<byte, byte>();
        var regularChannels = Enumerable.Range(0, 16)
            .Where(channel => channel != drumChannel)
            .Select(channel => (byte)channel)
            .ToList();
        var channelCursor = 0;

        foreach (var group in channelGroups)
        {
            var originalChannel = group.Key;
            var groupEvents = group.OrderBy(timedEvent => timedEvent.Time).ToList();
            var programNumber = (byte)0;
            var hasProgramChange = false;

            foreach (var timedEvent in groupEvents)
            {
                if (timedEvent.Event is not ProgramChangeEvent programChange)
                    continue;

                programNumber = (byte)programChange.ProgramNumber;
                hasProgramChange = true;
                break;
            }

            byte outputChannel;
            string trackName;

            if (originalChannel == drumChannel)
            {
                outputChannel = drumChannel;
                trackName = "Drumkit";
            }
            else
            {
                if (hasProgramChange && programToChannel.TryGetValue(programNumber, out var existingChannel))
                {
                    outputChannel = existingChannel;
                }
                else
                {
                    outputChannel = regularChannels[channelCursor % regularChannels.Count];
                    channelCursor++;
                    if (hasProgramChange)
                        programToChannel[programNumber] = outputChannel;
                }

                var gmName = DryWetMidiExtensions.GetGMProgramName(programNumber);
                trackName = string.IsNullOrEmpty(gmName) ? string.Empty : gmName;
            }

            var chunk = new TrackChunk();
            using (var chunkManager = chunk.ManageTimedEvents())
            {
                foreach (var timedEvent in groupEvents)
                {
                    var cloned = timedEvent.Event.Clone();
                    if (cloned is ChannelEvent channelEvent)
                        channelEvent.Channel = (FourBitNumber)outputChannel;

                    chunkManager.Objects.Add(new TimedEvent(cloned, timedEvent.Time));
                }
            }

            var newTrack = new EditableTrack(chunk, 0);
            if (!string.IsNullOrEmpty(trackName))
            {
                newTrack.Name = trackName;
                newTrack.MarkNameDirty();
            }

            newTracks.Add(newTrack);
        }

        track.Dispose();
        file.Tracks.RemoveAt(trackIndex);
        for (var i = 0; i < newTracks.Count; i++)
            file.Tracks.Insert(trackIndex + i, newTracks[i]);

        ReindexTracks(file);
        FileDocumentCommandHelpers.ConsolidateTempoToConductorTrack(file);
        return true;
    }

    public static void ReindexTracks(EditableMidiFile file)
    {
        for (var i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
    }

    public static EditorRefreshHints TrackListChangedHints(bool clearSelectedTrack)
        => new(
            ReloadTrackList: true,
            ReloadSelectedTrack: !clearSelectedTrack,
            ClearTrackSelection: true,
            ClearEventSelection: clearSelectedTrack,
            ClearSelectedTrack: clearSelectedTrack,
            RebuildPreview: true,
            RecalculateMetrics: true);
}
