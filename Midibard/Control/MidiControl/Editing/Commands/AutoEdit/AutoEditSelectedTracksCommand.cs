using System.Collections.Generic;

using MidiBard.Control.MidiControl.Editing.Commands.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;

[EditorOperation(
    "auto-edit.selected-tracks",
    "Auto Edit Selected Tracks",
    Scope = EditorOperationScope.AutoEdit,
    MenuPath = "Forge/Auto Edit",
    RequiresSelectedTracks = true)]
public sealed class AutoEditSelectedTracksCommand
    : EditorOperationBase, IEditorCommand<AutoEditSelectedTracksCommandOptions, MidiForgeAutoEditResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, AutoEditSelectedTracksCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeAutoEditResult> Execute(
        EditorCommandContext context,
        AutoEditSelectedTracksCommandOptions commandOptions)
    {
        var options = commandOptions.Options ?? new MidiForgeAutoEditOptions();
        var pickExecution = context.Invoker.Execute(
            new PickChordLinesCommand(),
            new PickChordLinesCommandOptions(
                commandOptions.TrackIndices,
                new MidiForgePickChordLinesOptions(
                    MaxSimultaneousNotes: options.MaxSimultaneousNotes,
                    PickStrategy: options.PickStrategy,
                    CreateNewTracks: options.CreateNewTracks,
                    RenameTracks: options.RenameTracks,
                    ChordTimingTolerance: options.ChordTimingTolerance)));

        if (!pickExecution.Succeeded)
            return EditorCommandResult<MidiForgeAutoEditResult>.NoChange(pickExecution.Message);

        var pickResult = pickExecution.Result!.Value;
        var changedNotes = 0;
        var changed = pickExecution.Changed;

        if (options.AdaptOutOfRangeNotes && pickResult.OutputTrackIndices.Count > 0)
        {
            var adaptExecution = context.Invoker.Execute(
                new AdaptTracksToPlayableRangeCommand(),
                new AdaptTracksToPlayableRangeCommandOptions(
                    pickResult.OutputTrackIndices,
                    new MidiForgeAdaptToRangeOptions(
                        CreateNewTracks: false,
                        RangeStrategy: options.RangeStrategy,
                        RenameTracks: false)));

            if (!adaptExecution.Succeeded)
            {
                var partialResult = CreateResult(pickResult, changedNotes);
                return changed
                    ? EditorCommandResult<MidiForgeAutoEditResult>.ChangedResult(partialResult, adaptExecution.Message)
                    : EditorCommandResult<MidiForgeAutoEditResult>.NoChange(adaptExecution.Message);
            }

            changedNotes = adaptExecution.Result!.Value.ChangedNotes;
            changed |= adaptExecution.Changed;
        }

        var result = CreateResult(pickResult, changedNotes);
        return changed
            ? EditorCommandResult<MidiForgeAutoEditResult>.ChangedResult(result)
            : EditorCommandResult<MidiForgeAutoEditResult>.UnchangedResult(result);
    }

    private static MidiForgeAutoEditResult CreateResult(
        MidiForgePickChordLinesResult pickResult,
        int changedNotes)
        => new(
            pickResult.SourceTracks,
            pickResult.CreatedTracks,
            pickResult.ReplacedTracks,
            pickResult.PickedParts,
            changedNotes);
}

public sealed record AutoEditSelectedTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeAutoEditOptions Options);
