namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed record EditorCommandValidation(
    bool IsValid,
    string Message = null)
{
    public static EditorCommandValidation Success { get; } = new(true);

    public static EditorCommandValidation Failure(string message)
        => new(false, message);
}

public sealed record EditorRefreshHints(
    bool ReloadTrackList = false,
    bool ReloadSelectedTrack = false,
    bool ReloadEventList = false,
    bool ClearTrackSelection = false,
    bool ClearEventSelection = false,
    bool ClearSelectedTrack = false,
    bool RebuildPreview = false,
    bool RecalculateMetrics = false)
{
    public static EditorRefreshHints None { get; } = new();

    public EditorRefreshHints Merge(EditorRefreshHints other)
    {
        if (other is null)
            return this;

        return new EditorRefreshHints(
            ReloadTrackList || other.ReloadTrackList,
            ReloadSelectedTrack || other.ReloadSelectedTrack,
            ReloadEventList || other.ReloadEventList,
            ClearTrackSelection || other.ClearTrackSelection,
            ClearEventSelection || other.ClearEventSelection,
            ClearSelectedTrack || other.ClearSelectedTrack,
            RebuildPreview || other.RebuildPreview,
            RecalculateMetrics || other.RecalculateMetrics);
    }
}

public sealed record EditorCommandResult<TResult>(
    bool Changed,
    TResult Value = default,
    string UserMessage = null,
    EditorRefreshHints RefreshHints = null)
{
    public static EditorCommandResult<TResult> NoChange(string message = null)
        => new(false, default, message);

    public static EditorCommandResult<TResult> UnchangedResult(
        TResult value = default,
        string message = null,
        EditorRefreshHints refreshHints = null)
        => new(false, value, message, refreshHints);

    public static EditorCommandResult<TResult> ChangedResult(
        TResult value = default,
        string message = null,
        EditorRefreshHints refreshHints = null)
        => new(true, value, message, refreshHints);
}

public sealed record EditorQueryResult<TResult>(
    TResult Value,
    string UserMessage = null);

public sealed record PreviewCommandResult<TResult>(
    bool Changed,
    TResult Value = default,
    string UserMessage = null);

public sealed record PreviewQueryResult<TResult>(
    TResult Value,
    string UserMessage = null);

public enum EditorOperationExecutionStatus
{
    Completed,
    Rejected
}

public sealed record EditorCommandExecutionResult<TResult>(
    EditorOperationExecutionStatus Status,
    EditorCommandResult<TResult> Result = null,
    string Message = null)
{
    public bool Succeeded => Status == EditorOperationExecutionStatus.Completed;
    public bool Changed => Result?.Changed == true;

    public static EditorCommandExecutionResult<TResult> Completed(EditorCommandResult<TResult> result)
        => new(EditorOperationExecutionStatus.Completed, result);

    public static EditorCommandExecutionResult<TResult> Rejected(string message)
        => new(EditorOperationExecutionStatus.Rejected, null, message);
}

public sealed record EditorQueryExecutionResult<TResult>(
    EditorOperationExecutionStatus Status,
    EditorQueryResult<TResult> Result = null,
    string Message = null)
{
    public bool Succeeded => Status == EditorOperationExecutionStatus.Completed;

    public static EditorQueryExecutionResult<TResult> Completed(EditorQueryResult<TResult> result)
        => new(EditorOperationExecutionStatus.Completed, result);

    public static EditorQueryExecutionResult<TResult> Rejected(string message)
        => new(EditorOperationExecutionStatus.Rejected, null, message);
}

public sealed record PreviewCommandExecutionResult<TResult>(
    EditorOperationExecutionStatus Status,
    PreviewCommandResult<TResult> Result = null,
    string Message = null)
{
    public bool Succeeded => Status == EditorOperationExecutionStatus.Completed;
    public bool Changed => Result?.Changed == true;

    public static PreviewCommandExecutionResult<TResult> Completed(PreviewCommandResult<TResult> result)
        => new(EditorOperationExecutionStatus.Completed, result);

    public static PreviewCommandExecutionResult<TResult> Rejected(string message)
        => new(EditorOperationExecutionStatus.Rejected, null, message);
}

public sealed record PreviewQueryExecutionResult<TResult>(
    EditorOperationExecutionStatus Status,
    PreviewQueryResult<TResult> Result = null,
    string Message = null)
{
    public bool Succeeded => Status == EditorOperationExecutionStatus.Completed;

    public static PreviewQueryExecutionResult<TResult> Completed(PreviewQueryResult<TResult> result)
        => new(EditorOperationExecutionStatus.Completed, result);

    public static PreviewQueryExecutionResult<TResult> Rejected(string message)
        => new(EditorOperationExecutionStatus.Rejected, null, message);
}
