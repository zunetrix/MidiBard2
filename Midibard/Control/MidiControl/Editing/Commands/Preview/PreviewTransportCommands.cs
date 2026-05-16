using System;

namespace MidiBard.Control.MidiControl.Editing.Commands.Preview;

public sealed record SeekPreviewOptions(
    double PositionSeconds);

public readonly record struct PreviewTransportResult(
    bool HasEvents,
    bool IsPlaying,
    bool IsPaused,
    double PositionSeconds,
    double DurationSeconds);

[EditorOperation(
    "preview.start",
    "Start Preview",
    Kind = EditorOperationKind.PreviewCommand,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class StartPreviewCommand
    : EditorOperationBase, IPreviewCommand<EditorOperationEmptyOptions, PreviewTransportResult>
{
    public EditorCommandValidation Validate(PreviewCommandContext context, EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.ValidateTransport(context);

    public PreviewCommandResult<PreviewTransportResult> Execute(
        PreviewCommandContext context,
        EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.Start(context);
}

[EditorOperation(
    "preview.resume",
    "Resume Preview",
    Kind = EditorOperationKind.PreviewCommand,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class ResumePreviewCommand
    : EditorOperationBase, IPreviewCommand<EditorOperationEmptyOptions, PreviewTransportResult>
{
    public EditorCommandValidation Validate(PreviewCommandContext context, EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.ValidateTransport(context);

    public PreviewCommandResult<PreviewTransportResult> Execute(
        PreviewCommandContext context,
        EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.Start(context);
}

[EditorOperation(
    "preview.restart",
    "Restart Preview",
    Kind = EditorOperationKind.PreviewCommand,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class RestartPreviewCommand
    : EditorOperationBase, IPreviewCommand<EditorOperationEmptyOptions, PreviewTransportResult>
{
    public EditorCommandValidation Validate(PreviewCommandContext context, EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.ValidateTransport(context);

    public PreviewCommandResult<PreviewTransportResult> Execute(
        PreviewCommandContext context,
        EditorOperationEmptyOptions options)
    {
        var before = PreviewTransportPrimitives.Capture(context);
        if (!context.Transport.HasEvents)
        {
            var unavailable = PreviewTransportPrimitives.SyncSession(context, isPaused: false);
            return new PreviewCommandResult<PreviewTransportResult>(
                PreviewTransportPrimitives.HasChanged(before, unavailable),
                unavailable,
                "Preview has no playable events.");
        }

        context.Transport.Seek(0);
        context.Transport.Play();
        var after = PreviewTransportPrimitives.SyncSession(context, isPaused: false);
        return new PreviewCommandResult<PreviewTransportResult>(
            PreviewTransportPrimitives.HasChanged(before, after),
            after);
    }
}

[EditorOperation(
    "preview.pause",
    "Pause Preview",
    Kind = EditorOperationKind.PreviewCommand,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class PausePreviewCommand
    : EditorOperationBase, IPreviewCommand<EditorOperationEmptyOptions, PreviewTransportResult>
{
    public EditorCommandValidation Validate(PreviewCommandContext context, EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.ValidateTransport(context);

    public PreviewCommandResult<PreviewTransportResult> Execute(
        PreviewCommandContext context,
        EditorOperationEmptyOptions options)
    {
        var before = PreviewTransportPrimitives.Capture(context);
        if (!context.Transport.IsPlaying)
        {
            var unchanged = PreviewTransportPrimitives.SyncSession(context, context.Preview.IsPaused);
            return new PreviewCommandResult<PreviewTransportResult>(
                PreviewTransportPrimitives.HasChanged(before, unchanged),
                unchanged);
        }

        context.Transport.Pause();
        var after = PreviewTransportPrimitives.SyncSession(context, isPaused: true);
        return new PreviewCommandResult<PreviewTransportResult>(
            PreviewTransportPrimitives.HasChanged(before, after),
            after);
    }
}

[EditorOperation(
    "preview.stop",
    "Stop Preview",
    Kind = EditorOperationKind.PreviewCommand,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class StopPreviewCommand
    : EditorOperationBase, IPreviewCommand<EditorOperationEmptyOptions, PreviewTransportResult>
{
    public EditorCommandValidation Validate(PreviewCommandContext context, EditorOperationEmptyOptions options)
        => PreviewTransportPrimitives.ValidateTransport(context);

    public PreviewCommandResult<PreviewTransportResult> Execute(
        PreviewCommandContext context,
        EditorOperationEmptyOptions options)
    {
        var before = PreviewTransportPrimitives.Capture(context);
        context.Transport.Stop();
        var after = PreviewTransportPrimitives.SyncSession(context, isPaused: false);
        return new PreviewCommandResult<PreviewTransportResult>(
            PreviewTransportPrimitives.HasChanged(before, after),
            after);
    }
}

[EditorOperation(
    "preview.seek",
    "Seek Preview",
    Kind = EditorOperationKind.PreviewCommand,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class SeekPreviewCommand
    : EditorOperationBase, IPreviewCommand<SeekPreviewOptions, PreviewTransportResult>
{
    public EditorCommandValidation Validate(PreviewCommandContext context, SeekPreviewOptions options)
    {
        var transportValidation = PreviewTransportPrimitives.ValidateTransport(context);
        if (!transportValidation.IsValid)
            return transportValidation;

        return double.IsFinite(options.PositionSeconds)
            ? EditorCommandValidation.Success
            : EditorCommandValidation.Failure("Preview seek position must be a finite number.");
    }

    public PreviewCommandResult<PreviewTransportResult> Execute(
        PreviewCommandContext context,
        SeekPreviewOptions options)
    {
        var before = PreviewTransportPrimitives.Capture(context);
        context.Transport.Seek(options.PositionSeconds);
        var after = PreviewTransportPrimitives.SyncSession(
            context,
            isPaused: context.Preview.IsPaused && !context.Transport.IsPlaying);
        return new PreviewCommandResult<PreviewTransportResult>(
            PreviewTransportPrimitives.HasChanged(before, after),
            after);
    }
}

public static class PreviewTransportPrimitives
{
    public static EditorCommandValidation ValidateTransport(PreviewCommandContext context)
        => context.Transport is null or UnavailableEditorPreviewTransport
            ? EditorCommandValidation.Failure("Preview transport is unavailable.")
            : EditorCommandValidation.Success;

    public static PreviewCommandResult<PreviewTransportResult> Start(PreviewCommandContext context)
    {
        var before = Capture(context);
        if (!context.Transport.HasEvents)
        {
            var unavailable = SyncSession(context, isPaused: false);
            return new PreviewCommandResult<PreviewTransportResult>(
                HasChanged(before, unavailable),
                unavailable,
                "Preview has no playable events.");
        }

        if (context.Transport.DurationSeconds > 0 &&
            context.Transport.PositionSeconds >= context.Transport.DurationSeconds)
        {
            context.Transport.Seek(0);
        }

        context.Transport.Play();
        var after = SyncSession(context, isPaused: false);
        return new PreviewCommandResult<PreviewTransportResult>(HasChanged(before, after), after);
    }

    public static PreviewTransportResult Capture(PreviewCommandContext context)
        => new(
            context.Transport?.HasEvents == true,
            context.Transport?.IsPlaying == true,
            context.Preview.IsPaused,
            Math.Max(0.0, context.Transport?.PositionSeconds ?? 0),
            Math.Max(0.0, context.Transport?.DurationSeconds ?? 0));

    public static PreviewTransportResult SyncSession(
        PreviewCommandContext context,
        bool isPaused)
    {
        var result = Capture(context) with
        {
            IsPaused = !context.Transport.IsPlaying && isPaused,
        };

        context.Preview.HasEvents = result.HasEvents;
        context.Preview.IsPlaying = result.IsPlaying;
        context.Preview.IsPaused = result.IsPaused;
        context.Preview.PlaybackPositionSeconds = result.PositionSeconds;
        context.Preview.DurationSeconds = result.DurationSeconds;
        return result;
    }

    public static bool HasChanged(PreviewTransportResult before, PreviewTransportResult after)
        => before != after;
}
