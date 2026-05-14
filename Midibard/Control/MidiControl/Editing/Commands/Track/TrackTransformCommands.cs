using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;
using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackTransformCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

public sealed record TrackTransformResult(
    int SourceTracks,
    int ChangedTracks,
    int ChangedNotes,
    IReadOnlyList<int> CreatedTrackIndices,
    IReadOnlyList<int> RemovedTrackIndices);

[EditorOperation(
    "track.transpose",
    "Transpose Tracks",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Transform",
    RequiresSelectedTracks = true)]
public sealed class TransposeTracksCommand
    : EditorOperationBase, IEditorCommand<TransposeTracksOptions, TrackTransformResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, TransposeTracksOptions options)
        => ValidatePerformanceTrackSelection(context, options.TrackIndices);

    public EditorCommandResult<TrackTransformResult> Execute(
        EditorCommandContext context,
        TransposeTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        if (options.Semitones == 0)
        {
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(
                EmptyResult(validTrackIndices.Length));
        }

        var beforeCount = file.Tracks.Count;
        var changedNotes = file.TransposeTracks(
            validTrackIndices,
            options.Semitones,
            options.MinimumNoteNumber,
            options.MaximumNoteNumber,
            options.CreateNewTracks);

        var createdTrackIndices = options.CreateNewTracks
            ? EstimateInsertedTrackIndices(validTrackIndices, beforeCount, file.Tracks.Count)
            : Array.Empty<int>();
        var changedTracks = options.CreateNewTracks
            ? createdTrackIndices.Length
            : changedNotes > 0 ? validTrackIndices.Length : 0;
        var result = new TrackTransformResult(
            validTrackIndices.Length,
            changedTracks,
            changedNotes,
            createdTrackIndices,
            Array.Empty<int>());

        if (changedTracks == 0 && changedNotes == 0)
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(result);

        return EditorCommandResult<TrackTransformResult>.ChangedResult(
            result,
            refreshHints: TrackTransformHints(options.CreateNewTracks));
    }

    private static TrackTransformResult EmptyResult(int sourceTracks)
        => new(sourceTracks, 0, 0, Array.Empty<int>(), Array.Empty<int>());
}

public sealed record TransposeTracksOptions(
    IReadOnlyList<int> TrackIndices,
    int Semitones,
    int MinimumNoteNumber = 0,
    int MaximumNoteNumber = 127,
    bool CreateNewTracks = false);

[EditorOperation(
    "track.merge",
    "Merge Tracks",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Transform",
    RequiresSelectedTracks = true)]
public sealed class MergeTracksCommand
    : EditorOperationBase, IEditorCommand<MergeTracksOptions, TrackTransformResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, MergeTracksOptions options)
    {
        var selectionValidation = ValidatePerformanceTrackSelection(context, options.TrackIndices);
        if (!selectionValidation.IsValid)
            return selectionValidation;

        var validTrackIndices = GetPerformanceTrackIndices(context.File, options.TrackIndices);
        if (validTrackIndices.Length < 2)
            return EditorCommandValidation.Failure("Choose at least two performance tracks to merge.");

        if (!validTrackIndices.Contains(options.TargetTrackIndex))
            return EditorCommandValidation.Failure("Choose one selected performance track as the merge target.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackTransformResult> Execute(
        EditorCommandContext context,
        MergeTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        var newTrackIndex = file.MergeTracks(
            options.TargetTrackIndex,
            validTrackIndices,
            includeProgramChange: options.IncludeProgramChanges,
            includePitchBend: options.IncludePitchBends,
            includeControlChange: options.IncludeControlChanges,
            toleranceMs: options.ToleranceMilliseconds,
            removeEqualNotes: options.RemoveEqualNotes,
            deleteOriginalTracks: options.DeleteOriginalTracks);

        var result = new TrackTransformResult(
            validTrackIndices.Length,
            newTrackIndex >= 0 ? 1 : 0,
            0,
            newTrackIndex >= 0 ? new[] { newTrackIndex } : Array.Empty<int>(),
            options.DeleteOriginalTracks && newTrackIndex >= 0
                ? validTrackIndices
                : Array.Empty<int>());

        if (newTrackIndex < 0)
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(result);

        return EditorCommandResult<TrackTransformResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}

public sealed record MergeTracksOptions(
    int TargetTrackIndex,
    IReadOnlyList<int> TrackIndices,
    bool IncludeProgramChanges,
    bool IncludePitchBends,
    bool IncludeControlChanges,
    int ToleranceMilliseconds,
    bool RemoveEqualNotes,
    bool DeleteOriginalTracks);

[EditorOperation(
    "track.quantize",
    "Quantize Tracks",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Transform",
    RequiresSelectedTracks = true)]
public sealed class QuantizeTracksCommand
    : EditorOperationBase, IEditorCommand<QuantizeTracksOptions, TrackTransformResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, QuantizeTracksOptions options)
    {
        var selectionValidation = ValidatePerformanceTrackSelection(context, options.TrackIndices);
        if (!selectionValidation.IsValid)
            return selectionValidation;

        return options.Grid is null || options.Settings is null
            ? EditorCommandValidation.Failure("Choose quantize settings.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackTransformResult> Execute(
        EditorCommandContext context,
        QuantizeTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        var beforeCount = file.Tracks.Count;
        var changedTracks = file.QuantizeTracks(
            validTrackIndices,
            options.Grid,
            options.Settings,
            options.CreateNewTracks);
        var createdTrackIndices = options.CreateNewTracks
            ? EstimateInsertedTrackIndices(validTrackIndices, beforeCount, file.Tracks.Count)
            : Array.Empty<int>();
        var result = new TrackTransformResult(
            validTrackIndices.Length,
            changedTracks,
            0,
            createdTrackIndices,
            Array.Empty<int>());

        if (changedTracks == 0)
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(result);

        return EditorCommandResult<TrackTransformResult>.ChangedResult(
            result,
            refreshHints: TrackTransformHints(options.CreateNewTracks));
    }
}

public sealed record QuantizeTracksOptions(
    IReadOnlyList<int> TrackIndices,
    IGrid Grid,
    QuantizingSettings Settings,
    bool CreateNewTracks);

internal static class TrackTransformCommandHelpers
{
    public static int[] EstimateInsertedTrackIndices(
        IReadOnlyList<int> sourceTrackIndices,
        int beforeTrackCount,
        int afterTrackCount)
    {
        var createdCount = Math.Max(0, afterTrackCount - beforeTrackCount);
        if (createdCount == 0)
            return Array.Empty<int>();

        var createdTrackIndices = new List<int>(createdCount);
        foreach (var sourceTrackIndex in sourceTrackIndices
                     .Distinct()
                     .OrderByDescending(index => index)
                     .Take(createdCount))
        {
            var insertedIndex = sourceTrackIndex + 1;
            for (var i = 0; i < createdTrackIndices.Count; i++)
            {
                if (createdTrackIndices[i] >= insertedIndex)
                    createdTrackIndices[i]++;
            }

            createdTrackIndices.Add(insertedIndex);
        }

        return createdTrackIndices.OrderBy(index => index).ToArray();
    }

    public static EditorRefreshHints TrackTransformHints(bool createdNewTracks)
        => new(
            ReloadTrackList: true,
            ReloadSelectedTrack: !createdNewTracks,
            ReloadEventList: !createdNewTracks,
            ClearTrackSelection: true,
            ClearEventSelection: !createdNewTracks,
            RebuildPreview: true,
            RecalculateMetrics: true);
}
