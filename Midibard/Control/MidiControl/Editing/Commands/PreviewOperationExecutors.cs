using System;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed class PreviewCommandExecutor
{
    public PreviewCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        IPreviewCommand<TOptions, TResult> command,
        PreviewCommandContext context,
        TOptions options)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var validation = command.Validate(context, options);
        if (!validation.IsValid)
            return PreviewCommandExecutionResult<TResult>.Rejected(validation.Message);

        return PreviewCommandExecutionResult<TResult>.Completed(command.Execute(context, options));
    }
}

public sealed class PreviewQueryExecutor
{
    public PreviewQueryExecutionResult<TResult> Execute<TOptions, TResult>(
        IPreviewQuery<TOptions, TResult> query,
        PreviewQueryContext context,
        TOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        var validation = query.Validate(context, options);
        if (!validation.IsValid)
            return PreviewQueryExecutionResult<TResult>.Rejected(validation.Message);

        return PreviewQueryExecutionResult<TResult>.Completed(query.Execute(context, options));
    }
}
