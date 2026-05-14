namespace MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;

[EditorOperation(
    "auto-edit.prepare-for-playback.conservative",
    "Quick Prepare for Playback",
    Scope = EditorOperationScope.AutoEdit,
    MenuPath = "Forge/Auto Edit",
    SortOrder = 90)]
public sealed class PrepareForPlaybackConservativeCommand
    : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, MidiForgePrepareForPlaybackResult>
{
    public static MidiForgePrepareForPlaybackOptions ConservativeOptions { get; } = new(
        FillEmptyTrackNames: true,
        ApplyTrackNameTransposes: true,
        SplitDrumkits: true,
        MaxSimultaneousNotes: 1,
        PickStrategy: MidiForgeChordPickStrategy.HighestChords,
        RangeStrategy: MidiForgeRangeFitStrategy.LowerHighNotesFirst,
        DrumTransposePreset: MidiForgeDrumTransposePreset.Default);

    public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
        => EditorCommandValidation.Success;

    public EditorCommandResult<MidiForgePrepareForPlaybackResult> Execute(
        EditorCommandContext context,
        EditorOperationEmptyOptions options)
    {
        var execution = context.Invoker.Execute(
            new PrepareForPlaybackCommand(),
            new PrepareForPlaybackCommandOptions(ConservativeOptions));

        if (!execution.Succeeded)
            return EditorCommandResult<MidiForgePrepareForPlaybackResult>.NoChange(execution.Message);

        var result = execution.Result!.Value;
        return execution.Changed
            ? EditorCommandResult<MidiForgePrepareForPlaybackResult>.ChangedResult(
                result,
                execution.Result.UserMessage)
            : EditorCommandResult<MidiForgePrepareForPlaybackResult>.UnchangedResult(
                result,
                execution.Result.UserMessage);
    }
}
