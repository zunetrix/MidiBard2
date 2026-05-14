using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class EditorCommandRegistryTests
{
    [Fact]
    public void FromTypes_DiscoversCommandsAndQueries()
    {
        var registry = EditorCommandRegistry.FromTypes(
            typeof(RegistryCommand),
            typeof(RegistryQuery));

        registry.Operations
            .Select(operation => operation.Id)
            .OrderBy(id => id)
            .ShouldBe(new[]
            {
                "test.registry-command",
                "test.registry-query",
            });

        registry.GetCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>(
                "test.registry-command")
            .ShouldBeOfType<RegistryCommand>();

        registry.GetQuery<EditorOperationEmptyOptions, RegistryQueryResult>(
                "test.registry-query")
            .ShouldBeOfType<RegistryQuery>();
    }

    [Fact]
    public void FromTypes_DiscoversPreviewCommandsAndQueries()
    {
        var registry = EditorCommandRegistry.FromTypes(
            typeof(RegistryPreviewCommand),
            typeof(RegistryPreviewQuery));

        registry.Operations
            .Select(operation => operation.Id)
            .OrderBy(id => id)
            .ShouldBe(new[]
            {
                "test.registry-preview-command",
                "test.registry-preview-query",
            });

        registry.GetPreviewCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>(
                "test.registry-preview-command")
            .ShouldBeOfType<RegistryPreviewCommand>();

        registry.GetPreviewQuery<EditorOperationEmptyOptions, RegistryQueryResult>(
                "test.registry-preview-query")
            .ShouldBeOfType<RegistryPreviewQuery>();
    }

    [Fact]
    public void FromTypes_RejectsDuplicateOperationIds()
    {
        Should.Throw<InvalidOperationException>(() => EditorCommandRegistry.FromTypes(
                typeof(DuplicateCommandA),
                typeof(DuplicateCommandB)))
            .Message.ShouldContain("Duplicate editor operation id");
    }

    [Fact]
    public void FromTypes_RejectsInvalidOperationIdConvention()
    {
        Should.Throw<InvalidOperationException>(() => EditorCommandRegistry.FromTypes(
                typeof(InvalidOperationIdCommand)))
            .Message.ShouldContain("lowercase dotted namespaces with kebab-case segments");
    }

    [Fact]
    public void FromTypes_RejectsInvalidMenuPathConvention()
    {
        Should.Throw<InvalidOperationException>(() => EditorCommandRegistry.FromTypes(
                typeof(InvalidMenuPathCommand)))
            .Message.ShouldContain("title-case segments");
    }

    [Fact]
    public void FromTypes_RejectsKindMismatch()
    {
        Should.Throw<InvalidOperationException>(() => EditorCommandRegistry.FromTypes(
                typeof(KindMismatchCommand)))
            .Message.ShouldContain("declares Query but implements Command");
    }

    [Fact]
    public void GetQuery_RejectsCommandOperation()
    {
        var registry = EditorCommandRegistry.FromTypes(typeof(RegistryCommand));

        Should.Throw<InvalidOperationException>(() => registry.GetQuery<EditorOperationEmptyOptions, EditorOperationEmptyResult>(
                "test.registry-command"))
            .Message.ShouldContain("not a Query");
    }

    [Fact]
    public void GetMenuOperations_ReturnsSortedDescriptorsForMenuPath()
    {
        var registry = EditorCommandRegistry.FromTypes(
            typeof(SortedCommandB),
            typeof(SortedCommandA),
            typeof(RegistryCommand));

        registry.GetMenuOperations("Forge/Test")
            .Select(operation => operation.Id)
            .ShouldBe(new[]
            {
                "test.sorted-a",
                "test.sorted-b",
            });
    }

    [Fact]
    public void FromTypes_AllowsEventScopeOperations()
    {
        var registry = EditorCommandRegistry.FromTypes(typeof(EventScopeCommand));

        registry.Operations.Single().Scope.ShouldBe(EditorOperationScope.Event);
    }

    [EditorOperation(
        "test.registry-command",
        "Registry Command",
        Scope = EditorOperationScope.Track)]
    private sealed class RegistryCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation(
        "test.event-scope",
        "Event Scope",
        Scope = EditorOperationScope.Event)]
    private sealed class EventScopeCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    private sealed record RegistryQueryResult(int Value);

    [EditorOperation(
        "test.registry-query",
        "Registry Query",
        Kind = EditorOperationKind.Query,
        HistoryPolicy = HistoryPolicy.None)]
    private sealed class RegistryQuery
        : EditorOperationBase, IEditorQuery<EditorOperationEmptyOptions, RegistryQueryResult>
    {
        public EditorCommandValidation Validate(EditorQueryContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorQueryResult<RegistryQueryResult> Execute(
            EditorQueryContext context,
            EditorOperationEmptyOptions options)
            => new(new RegistryQueryResult(1));
    }

    [EditorOperation(
        "test.registry-preview-command",
        "Registry Preview Command",
        Kind = EditorOperationKind.PreviewCommand,
        Scope = EditorOperationScope.Preview,
        HistoryPolicy = HistoryPolicy.None)]
    private sealed class RegistryPreviewCommand
        : EditorOperationBase, IPreviewCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(PreviewCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public PreviewCommandResult<EditorOperationEmptyResult> Execute(
            PreviewCommandContext context,
            EditorOperationEmptyOptions options)
            => new(false);
    }

    [EditorOperation(
        "test.registry-preview-query",
        "Registry Preview Query",
        Kind = EditorOperationKind.PreviewQuery,
        Scope = EditorOperationScope.Preview,
        HistoryPolicy = HistoryPolicy.None)]
    private sealed class RegistryPreviewQuery
        : EditorOperationBase, IPreviewQuery<EditorOperationEmptyOptions, RegistryQueryResult>
    {
        public EditorCommandValidation Validate(PreviewQueryContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public PreviewQueryResult<RegistryQueryResult> Execute(
            PreviewQueryContext context,
            EditorOperationEmptyOptions options)
            => new(new RegistryQueryResult(1));
    }

    [EditorOperation("test.duplicate", "Duplicate A")]
    private sealed class DuplicateCommandA
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation("test.duplicate", "Duplicate B")]
    private sealed class DuplicateCommandB
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation("Test.InvalidOperationId", "Invalid Operation Id")]
    private sealed class InvalidOperationIdCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation(
        "test.invalid-menu-path",
        "Invalid Menu Path",
        MenuPath = "forge/test")]
    private sealed class InvalidMenuPathCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation(
        "test.kind-mismatch",
        "Kind Mismatch",
        Kind = EditorOperationKind.Query)]
    private sealed class KindMismatchCommand
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation(
        "test.sorted-b",
        "Sorted B",
        MenuPath = "Forge/Test",
        SortOrder = 20)]
    private sealed class SortedCommandB
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }

    [EditorOperation(
        "test.sorted-a",
        "Sorted A",
        MenuPath = "Forge/Test",
        SortOrder = 10)]
    private sealed class SortedCommandA
        : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, EditorOperationEmptyResult>
    {
        public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorCommandResult<EditorOperationEmptyResult> Execute(
            EditorCommandContext context,
            EditorOperationEmptyOptions options)
            => EditorCommandResult<EditorOperationEmptyResult>.NoChange();
    }
}
