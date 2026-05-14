using System;
using System.Collections.Generic;
using System.Linq;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

public sealed record TrackMutationResult(
    int ChangedTracks,
    IReadOnlyList<int> CreatedTrackIndices,
    IReadOnlyList<int> RemovedTrackIndices);

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
            var beforeCount = file.Tracks.Count;
            file.CloneTrack(trackIndex);
            if (file.Tracks.Count == beforeCount)
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
            var beforeCount = file.Tracks.Count;
            file.RemoveTrack(trackIndex);
            if (file.Tracks.Count == beforeCount)
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

        context.File.MoveTrack(options.FromIndex, options.ToIndex);

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
        var beforeVersion = file.Version;
        var beforeCount = file.Tracks.Count;

        file.SplitTrackByChannel(options.TrackIndex);

        if (file.Version == beforeVersion)
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
