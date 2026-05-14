using System;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed class EditorCommandExecutor
{
    private EditorCommandExecutionScope activeScope;
    private int executionDepth;

    public EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        IEditorCommand<TOptions, TResult> command,
        EditorCommandContext context,
        TOptions options)
        => Execute(command, context, options, EditorCommandExecutionOptions.Default);

    public EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        IEditorCommand<TOptions, TResult> command,
        EditorCommandContext context,
        TOptions options,
        EditorCommandExecutionOptions executionOptions)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        executionOptions ??= EditorCommandExecutionOptions.Default;
        var isRootExecution = executionDepth == 0;
        if (isRootExecution)
            activeScope = CreateExecutionScope(command.Descriptor, context, executionOptions);

        executionDepth++;
        var scopedContext = context with
        {
            Invoker = new EditorCommandInvoker(this, context, executionOptions),
        };

        var beforeVersion = scopedContext.File.Version;
        var validation = command.Validate(scopedContext, options);
        if (!validation.IsValid)
        {
            executionDepth--;
            if (isRootExecution)
                activeScope = null;

            return EditorCommandExecutionResult<TResult>.Rejected(validation.Message);
        }

        EditorCommandResult<TResult> result;
        try
        {
            result = command.Execute(scopedContext, options);
        }
        finally
        {
            executionDepth--;
        }

        if (result.Changed)
        {
            MarkChanged(scopedContext, beforeVersion);
            activeScope.Changed = true;
        }

        scopedContext.Session.AddRefreshHints(result.RefreshHints);

        if (isRootExecution)
        {
            CompleteRootExecution(scopedContext);
            activeScope = null;
        }

        return EditorCommandExecutionResult<TResult>.Completed(result);
    }

    private static EditorCommandExecutionScope CreateExecutionScope(
        EditorOperationDescriptor descriptor,
        EditorCommandContext context,
        EditorCommandExecutionOptions executionOptions)
    {
        var suppressHistory = executionOptions.SuppressHistory
                              || descriptor.HistoryPolicy == HistoryPolicy.None;
        return new EditorCommandExecutionScope(
            suppressHistory
                ? null
                : context.Session.History.BeginPendingCapture(context.File));
    }

    private static void MarkChanged(EditorCommandContext context, int beforeVersion)
    {
        if (context.File.Version == beforeVersion)
            context.File.MarkChanged();

        context.Session.IsDirty = true;
    }

    private void CompleteRootExecution(EditorCommandContext context)
    {
        if (!activeScope.Changed || activeScope.PendingHistory is null)
            return;

        context.Session.History.CommitPendingCapture(context.File, activeScope.PendingHistory);
    }

    private sealed class EditorCommandExecutionScope
    {
        public EditorCommandExecutionScope(MidiForgePendingHistoryCapture pendingHistory)
        {
            PendingHistory = pendingHistory;
        }

        public MidiForgePendingHistoryCapture PendingHistory { get; }
        public bool Changed { get; set; }
    }
}

public sealed record EditorCommandExecutionOptions(bool SuppressHistory = false)
{
    public static EditorCommandExecutionOptions Default { get; } = new();
    public static EditorCommandExecutionOptions WithoutHistory { get; } = new(SuppressHistory: true);
}

internal sealed class EditorCommandInvoker : IEditorCommandInvoker
{
    private readonly EditorCommandExecutor executor;
    private readonly EditorCommandContext context;
    private readonly EditorCommandExecutionOptions executionOptions;

    public EditorCommandInvoker(
        EditorCommandExecutor executor,
        EditorCommandContext context,
        EditorCommandExecutionOptions executionOptions)
    {
        this.executor = executor;
        this.context = context;
        this.executionOptions = executionOptions;
    }

    public EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        IEditorCommand<TOptions, TResult> command,
        TOptions options)
        => executor.Execute(command, context, options, executionOptions);

    public EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        string operationId,
        TOptions options)
    {
        if (context.Services.CommandRegistry is null)
        {
            return EditorCommandExecutionResult<TResult>.Rejected(
                "No command registry is available for command composition.");
        }

        var command = context.Services.CommandRegistry.GetCommand<TOptions, TResult>(operationId);
        return Execute(command, options);
    }
}

internal sealed class UnavailableEditorCommandInvoker : IEditorCommandInvoker
{
    public static UnavailableEditorCommandInvoker Instance { get; } = new();

    private UnavailableEditorCommandInvoker()
    {
    }

    public EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        IEditorCommand<TOptions, TResult> command,
        TOptions options)
        => EditorCommandExecutionResult<TResult>.Rejected(
            "Command invoker is only available while a command is executing.");

    public EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        string operationId,
        TOptions options)
        => EditorCommandExecutionResult<TResult>.Rejected(
            "Command invoker is only available while a command is executing.");
}
