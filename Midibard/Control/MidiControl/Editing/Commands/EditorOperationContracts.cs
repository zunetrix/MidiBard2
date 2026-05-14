using System;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public interface IEditorOperation
{
    string Id { get; }
    EditorOperationDescriptor Descriptor { get; }
}

public abstract class EditorOperationBase : IEditorOperation
{
    private EditorOperationDescriptor descriptor;

    public string Id => Descriptor.Id;

    public EditorOperationDescriptor Descriptor
        => descriptor ??= EditorOperationDescriptor.FromType(GetType());
}

public interface IEditorCommand<TOptions, TResult> : IEditorOperation
{
    EditorCommandValidation Validate(EditorCommandContext context, TOptions options);
    EditorCommandResult<TResult> Execute(EditorCommandContext context, TOptions options);
}

public interface IEditorQuery<TOptions, TResult> : IEditorOperation
{
    EditorCommandValidation Validate(EditorQueryContext context, TOptions options);
    EditorQueryResult<TResult> Execute(EditorQueryContext context, TOptions options);
}

public interface IPreviewCommand<TOptions, TResult> : IEditorOperation
{
    EditorCommandValidation Validate(PreviewCommandContext context, TOptions options);
    PreviewCommandResult<TResult> Execute(PreviewCommandContext context, TOptions options);
}

public interface IPreviewQuery<TOptions, TResult> : IEditorOperation
{
    EditorCommandValidation Validate(PreviewQueryContext context, TOptions options);
    PreviewQueryResult<TResult> Execute(PreviewQueryContext context, TOptions options);
}

public readonly record struct EditorOperationEmptyOptions;

public readonly record struct EditorOperationEmptyResult;
