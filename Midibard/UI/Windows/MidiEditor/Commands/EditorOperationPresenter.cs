using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.UI.Windows.MidiEditor.Commands;

public interface IEditorOperationPresenter
{
    string OperationId { get; }
    void DrawMenuItem(EditorPresenterContext context);
    void Open(EditorPresenterContext context);
    void DrawPopup(EditorPresenterContext context);
}

public interface IEditorOperationPresenter<TOptions, TResult> : IEditorOperationPresenter
{
    TOptions GetDefaultOptions(EditorPresenterContext context);
    EditorCommandValidation ValidateOptions(EditorPresenterContext context, TOptions options);
}

public abstract class EditorOperationPresenterBase : IEditorOperationPresenter
{
    private string operationId;

    public string OperationId
        => operationId ??= ResolveOperationId(GetType());

    public abstract void DrawMenuItem(EditorPresenterContext context);
    public abstract void Open(EditorPresenterContext context);
    public abstract void DrawPopup(EditorPresenterContext context);

    private static string ResolveOperationId(System.Type type)
    {
        var attribute = System.Attribute.GetCustomAttribute(
            type,
            typeof(EditorOperationPresenterAttribute)) as EditorOperationPresenterAttribute;

        if (attribute is null)
            throw new System.InvalidOperationException(
                $"{type.FullName} is missing {nameof(EditorOperationPresenterAttribute)}.");

        return attribute.OperationId;
    }
}

public sealed record EditorPresenterContext(
    MidiEditorSessionState Session,
    EditorCommandRegistry CommandRegistry,
    EditorCommandExecutor CommandExecutor,
    EditorQueryExecutor QueryExecutor,
    EditorPresenterServices Services);

public sealed class EditorPresenterServices
{
    public static EditorPresenterServices Empty { get; } = new();
}
