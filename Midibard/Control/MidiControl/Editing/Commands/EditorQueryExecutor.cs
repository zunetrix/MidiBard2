using System;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed class EditorQueryExecutor
{
    public EditorQueryExecutionResult<TResult> Execute<TOptions, TResult>(
        IEditorQuery<TOptions, TResult> query,
        EditorQueryContext context,
        TOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        var validation = query.Validate(context, options);
        if (!validation.IsValid)
            return EditorQueryExecutionResult<TResult>.Rejected(validation.Message);

        return EditorQueryExecutionResult<TResult>.Completed(query.Execute(context, options));
    }
}
