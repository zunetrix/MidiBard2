using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class EditorCommandExecutorTests
{
    [Fact]
    public void Execute_ChangedCommandMarksDirtyCapturesHistoryAndAppliesRefreshHints()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);
        var executor = new EditorCommandExecutor();

        var result = executor.Execute(
            new RenameTrackCommand(),
            context,
            new RenameTrackOptions("Lead"));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Lead");
        file.IsDirty.ShouldBeTrue();
        session.IsDirty.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBeEmpty();
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Execute_NoChangeDoesNotDirtyOrCaptureHistory()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new NoChangeCommand(),
            context,
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
        session.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Execute_RejectedCommandDoesNotRunOrCaptureHistory()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);
        var command = new RejectingCommand();

        var result = new EditorCommandExecutor().Execute(
            command,
            context,
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Rejected for test.");
        command.ExecuteCount.ShouldBe(0);
        file.IsDirty.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_HistoryPolicyNoneMarksDirtyWithoutUndoSnapshot()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);

        var result = new EditorCommandExecutor().Execute(
            new NonUndoableRenameTrackCommand(),
            context,
            new RenameTrackOptions("Temporary"));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Temporary");
        file.IsDirty.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_WithoutHistoryOptionMarksDirtyWithoutUndoSnapshot()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);

        var result = new EditorCommandExecutor().Execute(
            new RenameTrackCommand(),
            context,
            new RenameTrackOptions("Compatibility"),
            EditorCommandExecutionOptions.WithoutHistory);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Compatibility");
        file.IsDirty.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_NestedCommandUsesOneRootUndoSnapshotAndMergesRefreshHints()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);

        var result = new EditorCommandExecutor().Execute(
            new RenameThenClearSelectionCommand(),
            context,
            new RenameTrackOptions("Nested"));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Nested");
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBeEmpty();
    }

    [Fact]
    public void Execute_NestedCommandCanResolveChildThroughRegistry()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var registry = EditorCommandRegistry.FromTypes(typeof(RenameTrackCommand));
        var context = EditorCommandContext.Create(
            session,
            new EditorCommandServices { CommandRegistry = registry });

        var result = new EditorCommandExecutor().Execute(
            new RegistryRenameCommand(),
            context,
            new RenameTrackOptions("From Registry"));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("From Registry");
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Execute_RejectedNestedCommandDoesNotCaptureHistory()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);

        var result = new EditorCommandExecutor().Execute(
            new ParentWithRejectedChildCommand(),
            context,
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.UserMessage.ShouldBe("Rejected for test.");
        file.IsDirty.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(0);
    }

    private static EditableMidiFile CreateEditableFile(params Note[] notes)
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            foreach (var note in notes)
            {
                manager.Objects.Add(new TimedEvent(
                    new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                    note.Time));
                manager.Objects.Add(new TimedEvent(
                    new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                    note.EndTime));
            }
        }

        return new EditableMidiFile(new MidiFile(chunk));
    }

    private static Note Note(int noteNumber, long time, long length, int channel = 0)
        => new(
            (SevenBitNumber)(byte)noteNumber,
            length,
            time)
        {
            Channel = (FourBitNumber)(byte)channel,
            Velocity = (SevenBitNumber)100,
            OffVelocity = (SevenBitNumber)0,
        };

    private sealed record RenameTrackOptions(string Name);

    private sealed record RenameTrackResult(string Name);

    [EditorOperation(
        "test.rename-track",
        "Rename Track",
        Scope = EditorOperationScope.Track,
        HistoryPolicy = HistoryPolicy.CaptureIfChanged)]
    private sealed class RenameTrackCommand
        : EditorOperationBase, IEditorCommand<RenameTrackOptions, RenameTrackResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, RenameTrackOptions options)
            => string.IsNullOrWhiteSpace(options.Name)
                ? EditorCommandValidation.Failure("Track name is required.")
                : EditorCommandValidation.Success;

        public EditorCommandResult<RenameTrackResult> Execute(
            EditorCommandContext context,
            RenameTrackOptions options)
        {
            context.File.Tracks[0].Name = options.Name;
            context.File.Tracks[0].MarkNameDirty();

            return EditorCommandResult<RenameTrackResult>.ChangedResult(
                new RenameTrackResult(options.Name),
                refreshHints: new EditorRefreshHints(
                    ReloadTrackList: true,
                    ReloadSelectedTrack: true));
        }
    }

    [EditorOperation(
        "test.non-undoable-rename-track",
        "Non-Undoable Rename Track",
        Scope = EditorOperationScope.Track,
        HistoryPolicy = HistoryPolicy.None)]
    private sealed class NonUndoableRenameTrackCommand
        : EditorOperationBase, IEditorCommand<RenameTrackOptions, RenameTrackResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, RenameTrackOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<RenameTrackResult> Execute(
            EditorCommandContext context,
            RenameTrackOptions options)
        {
            context.File.Tracks[0].Name = options.Name;
            context.File.Tracks[0].MarkNameDirty();
            return EditorCommandResult<RenameTrackResult>.ChangedResult(new RenameTrackResult(options.Name));
        }
    }

    [EditorOperation("test.no-change", "No Change")]
    private sealed class NoChangeCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation("test.rejecting", "Rejecting")]
    private sealed class RejectingCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public int ExecuteCount { get; private set; }

        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Failure("Rejected for test.");

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
        {
            ExecuteCount++;
            return EditorCommandResult<EditorOperationEmptyResult>.ChangedResult();
        }
    }

    [EditorOperation("test.rename-then-clear-selection", "Rename Then Clear Selection")]
    private sealed class RenameThenClearSelectionCommand
        : EditorOperationBase, IEditorCommand<RenameTrackOptions, RenameTrackResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, RenameTrackOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<RenameTrackResult> Execute(
            EditorCommandContext context,
            RenameTrackOptions options)
        {
            var childResult = context.Invoker.Execute(
                new RenameTrackCommand(),
                options);

            if (!childResult.Succeeded)
                return EditorCommandResult<RenameTrackResult>.NoChange(childResult.Message);

            return EditorCommandResult<RenameTrackResult>.ChangedResult(
                childResult.Result!.Value,
                refreshHints: new EditorRefreshHints(ClearTrackSelection: true));
        }
    }

    [EditorOperation("test.registry-rename", "Registry Rename")]
    private sealed class RegistryRenameCommand
        : EditorOperationBase, IEditorCommand<RenameTrackOptions, RenameTrackResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, RenameTrackOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<RenameTrackResult> Execute(
            EditorCommandContext context,
            RenameTrackOptions options)
        {
            var childResult = context.Invoker.Execute<RenameTrackOptions, RenameTrackResult>(
                "test.rename-track",
                options);

            return childResult.Succeeded
                ? EditorCommandResult<RenameTrackResult>.ChangedResult(childResult.Result!.Value)
                : EditorCommandResult<RenameTrackResult>.NoChange(childResult.Message);
        }
    }

    [EditorOperation("test.parent-with-rejected-child", "Parent With Rejected Child")]
    private sealed class ParentWithRejectedChildCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
        {
            var childResult = context.Invoker.Execute(
                new RejectingCommand(),
                options);

            return childResult.Succeeded
                ? EditorCommandResult<EditorOperationEmptyResult>.ChangedResult()
                : EditorCommandResult<EditorOperationEmptyResult>.NoChange(childResult.Message);
        }
    }
}
