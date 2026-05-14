using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.UI.Windows.MidiEditor.Commands;

namespace MidiBard.Tests.UI.Windows.MidiEditor.Commands;

public class EditorOperationPresenterRegistryTests
{
    [Fact]
    public void FromTypes_DiscoversPresentersForKnownOperations()
    {
        var commandRegistry = EditorCommandRegistry.FromTypes(typeof(PresentedCommand));

        var presenterRegistry = EditorOperationPresenterRegistry.FromTypes(
            commandRegistry,
            typeof(PresentedCommandPresenter));

        presenterRegistry.OperationIds.ShouldBe(new[] { "test.presented-command" });

        var presenter = presenterRegistry.GetPresenter("test.presented-command");

        presenter.ShouldBeOfType<PresentedCommandPresenter>();
        presenter.OperationId.ShouldBe("test.presented-command");
    }

    [Fact]
    public void FromTypes_RejectsDuplicatePresentersForSameOperation()
    {
        var commandRegistry = EditorCommandRegistry.FromTypes(typeof(PresentedCommand));

        Should.Throw<InvalidOperationException>(() => EditorOperationPresenterRegistry.FromTypes(
                commandRegistry,
                typeof(PresentedCommandPresenter),
                typeof(DuplicatePresentedCommandPresenter)))
            .Message.ShouldContain("Duplicate presenter");
    }

    [Fact]
    public void FromTypes_RejectsPresenterForUnknownOperation()
    {
        var commandRegistry = EditorCommandRegistry.FromTypes(typeof(PresentedCommand));

        Should.Throw<InvalidOperationException>(() => EditorOperationPresenterRegistry.FromTypes(
                commandRegistry,
                typeof(UnknownOperationPresenter)))
            .Message.ShouldContain("targets unknown editor operation");
    }

    [Fact]
    public void FromTypes_RejectsAttributeTypeThatIsNotPresenter()
    {
        var commandRegistry = EditorCommandRegistry.FromTypes(typeof(PresentedCommand));

        Should.Throw<InvalidOperationException>(() => EditorOperationPresenterRegistry.FromTypes(
                commandRegistry,
                typeof(NotAPresenter)))
            .Message.ShouldContain("must implement");
    }

    [Fact]
    public void TryGetPresenter_ReturnsFalseForMissingPresenter()
    {
        var commandRegistry = EditorCommandRegistry.FromTypes(typeof(PresentedCommand));
        var presenterRegistry = EditorOperationPresenterRegistry.FromTypes(commandRegistry);

        presenterRegistry.TryGetPresenter("test.presented-command", out var presenter).ShouldBeFalse();
        presenter.ShouldBeNull();
    }

    [EditorOperation("test.presented-command", "Presented Command")]
    private sealed class PresentedCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperationPresenter("test.presented-command")]
    private sealed class PresentedCommandPresenter : EditorOperationPresenterBase
    {
        public override void DrawMenuItem(EditorPresenterContext context)
        {
        }

        public override void Open(EditorPresenterContext context)
        {
        }

        public override void DrawPopup(EditorPresenterContext context)
        {
        }
    }

    [EditorOperationPresenter("test.presented-command")]
    private sealed class DuplicatePresentedCommandPresenter : EditorOperationPresenterBase
    {
        public override void DrawMenuItem(EditorPresenterContext context)
        {
        }

        public override void Open(EditorPresenterContext context)
        {
        }

        public override void DrawPopup(EditorPresenterContext context)
        {
        }
    }

    [EditorOperationPresenter("test.unknown-operation")]
    private sealed class UnknownOperationPresenter : EditorOperationPresenterBase
    {
        public override void DrawMenuItem(EditorPresenterContext context)
        {
        }

        public override void Open(EditorPresenterContext context)
        {
        }

        public override void DrawPopup(EditorPresenterContext context)
        {
        }
    }

    [EditorOperationPresenter("test.presented-command")]
    private sealed class NotAPresenter
    {
    }
}
