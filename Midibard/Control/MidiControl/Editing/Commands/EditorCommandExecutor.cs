using System;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed class EditorCommandExecutor
{
    private EditorCommandExecutionScope activeScope;
    private EditorCommandGestureScope activeGesture;
    private int executionDepth;

    public bool IsGestureActive => activeGesture is not null;

    public void BeginGesture(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (activeGesture is not null)
            throw new InvalidOperationException("An editor command gesture is already active.");

        if (context.File is null)
            throw new InvalidOperationException("Editor command gestures require an open MIDI file.");

        activeGesture = new EditorCommandGestureScope(
            context.File,
            context.Session.History.BeginPendingCapture(context.File));
    }

    public bool CommitGesture(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var gesture = activeGesture;
        activeGesture = null;
        if (gesture is null || !gesture.Changed)
            return false;

        if (!ReferenceEquals(context.Session.File, gesture.File))
            return false;

        return context.Session.History.CommitPendingCapture(gesture.File, gesture.PendingHistory);
    }

    public void CancelGesture()
        => activeGesture = null;

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

        if (command.Descriptor.RequiresFile && context.File is null)
        {
            return EditorCommandExecutionResult<TResult>.Rejected(
                "Open a MIDI file before running this operation.");
        }

        var isRootExecution = executionDepth == 0;
        if (isRootExecution)
            activeScope = CreateExecutionScope(command.Descriptor, context, executionOptions);

        executionDepth++;
        var scopedContext = context with
        {
            Invoker = new EditorCommandInvoker(this, context, executionOptions),
        };

        var beforeFile = scopedContext.File;
        var beforeVersion = beforeFile?.Version ?? 0;
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
            MarkChanged(scopedContext, beforeFile, beforeVersion);
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

    private EditorCommandExecutionScope CreateExecutionScope(
        EditorOperationDescriptor descriptor,
        EditorCommandContext context,
        EditorCommandExecutionOptions executionOptions)
    {
        var suppressHistory = executionOptions.SuppressHistory
                              || descriptor.HistoryPolicy == HistoryPolicy.None;

        var usesGesture = activeGesture is not null
                          && !suppressHistory
                          && ReferenceEquals(context.File, activeGesture.File);

        return new EditorCommandExecutionScope(
            usesGesture,
            suppressHistory || context.File is null || usesGesture
                ? null
                : context.Session.History.BeginPendingCapture(context.File));
    }

    private static void MarkChanged(
        EditorCommandContext context,
        EditableMidiFile beforeFile,
        int beforeVersion)
    {
        var file = context.Session.File ?? context.File;
        if (file is not null && (ReferenceEquals(file, beforeFile) ? file.Version == beforeVersion : true))
            file.MarkChanged();

        context.Session.IsDirty = true;
    }

    private void CompleteRootExecution(EditorCommandContext context)
    {
        if (!activeScope.Changed || activeScope.PendingHistory is null)
        {
            if (activeScope.Changed && activeScope.UsesGesture)
                activeGesture!.Changed = true;

            return;
        }

        context.Session.History.CommitPendingCapture(context.File, activeScope.PendingHistory);
    }

    private sealed class EditorCommandExecutionScope
    {
        public EditorCommandExecutionScope(
            bool usesGesture,
            MidiForgePendingHistoryCapture pendingHistory)
        {
            UsesGesture = usesGesture;
            PendingHistory = pendingHistory;
        }

        public bool UsesGesture { get; }
        public MidiForgePendingHistoryCapture PendingHistory { get; }
        public bool Changed { get; set; }
    }

    private sealed class EditorCommandGestureScope
    {
        public EditorCommandGestureScope(
            EditableMidiFile file,
            MidiForgePendingHistoryCapture pendingHistory)
        {
            File = file;
            PendingHistory = pendingHistory;
        }

        public EditableMidiFile File { get; }
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
