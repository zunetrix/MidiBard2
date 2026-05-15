using Melanchall.DryWetMidi.Tools;

namespace MidiBard.Control.MidiControl.Editing.Commands.File;

[EditorOperation(
    "file.sanitize",
    "Sanitize File",
    Scope = EditorOperationScope.File,
    MenuPath = "File/Cleanup")]
public sealed class SanitizeFileCommand
    : EditorOperationBase, IEditorCommand<SanitizeFileOptions, FileMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SanitizeFileOptions options)
        => options.Settings is null
            ? EditorCommandValidation.Failure("Choose sanitize settings.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<FileMutationResult> Execute(
        EditorCommandContext context,
        SanitizeFileOptions options)
    {
        var changed = FileDocumentCommandHelpers.ApplySanitize(context.File, options.Settings);
        var result = new FileMutationResult(context.File.Tracks.Count);

        if (!changed)
            return EditorCommandResult<FileMutationResult>.UnchangedResult(result);

        return EditorCommandResult<FileMutationResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: true,
                ReloadEventList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}

public sealed record SanitizeFileOptions(SanitizingSettings Settings);

public sealed record FileMutationResult(int TrackCount);
