using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

[EditorOperation(
    "track.clear-names",
    "Clear Track Names",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Names",
    RequiresSelectedTracks = true)]
public sealed class ClearTrackNamesCommand
    : EditorOperationBase, IEditorCommand<ClearTrackNamesOptions, MidiForgeTrackNameResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ClearTrackNamesOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeTrackNameResult> Execute(
        EditorCommandContext context,
        ClearTrackNamesOptions options)
    {
        var validTrackIndices = MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(
            context.File,
            options.TrackIndices);
        var renamedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = context.File.Tracks[trackIndex];
            if (string.IsNullOrWhiteSpace(track.Name))
                continue;

            if (options.PreserveDrumInstrumentNames
                && MidiForgeTrackNamePrimitives.IsPreservedDrumTrackName(track.Name))
            {
                continue;
            }

            if (MidiForgeTrackNamePrimitives.SetEditableTrackName(track, string.Empty))
                renamedTracks++;
        }

        var result = new MidiForgeTrackNameResult(validTrackIndices.Length, renamedTracks);

        if (result.RenamedTracks == 0)
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

public sealed record ClearTrackNamesOptions(
    IReadOnlyList<int> TrackIndices,
    bool PreserveDrumInstrumentNames = true);
