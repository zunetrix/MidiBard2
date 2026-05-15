using System.Collections.Generic;
using System.Linq;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

[EditorOperation(
    "track.fill-empty-names",
    "Fill Empty Track Names",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Names",
    RequiresSelectedTracks = true)]
public sealed class FillEmptyTrackNamesCommand
    : EditorOperationBase, IEditorCommand<FillEmptyTrackNamesOptions, MidiForgeTrackNameResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, FillEmptyTrackNamesOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeTrackNameResult> Execute(
        EditorCommandContext context,
        FillEmptyTrackNamesOptions options)
    {
        var validTrackIndices = MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(
            context.File,
            options.TrackIndices);
        var renamedTracks = 0;

        foreach (var (trackIndex, fallbackIndex) in validTrackIndices.Select((index, order) => (index, order + 1)))
        {
            var track = context.File.Tracks[trackIndex];
            if (!string.IsNullOrWhiteSpace(track.Name))
                continue;

            var defaultName = MidiForgeTrackNaming.GetDefaultTrackName(
                track.Chunk,
                fallbackIndex,
                options.FillMode,
                context.Services.MidiMapProvider);
            if (MidiForgeTrackNamePrimitives.SetEditableTrackName(track, defaultName))
                renamedTracks++;
        }

        var result = new MidiForgeTrackNameResult(validTrackIndices.Length, renamedTracks);
        if (renamedTracks == 0)
            return EditorCommandResult<MidiForgeTrackNameResult>.UnchangedResult(result);

        return EditorCommandResult<MidiForgeTrackNameResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record FillEmptyTrackNamesOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeTrackNameFillMode FillMode);
