using System;
using System.Threading;

using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed record EditorCommandContext(
    MidiEditorSessionState Session,
    EditableMidiFile File,
    EditorSelectionState Selection,
    EditorCommandServices Services,
    CancellationToken CancellationToken,
    IEditorCommandInvoker Invoker)
{
    public static EditorCommandContext Create(
        MidiEditorSessionState session,
        EditorCommandServices services = null,
        CancellationToken cancellationToken = default,
        IEditorCommandInvoker invoker = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.File is null)
            throw new InvalidOperationException("Editor command context requires an open MIDI file.");

        return new EditorCommandContext(
            session,
            session.File,
            session.Selection,
            services ?? EditorCommandServices.Empty,
            cancellationToken,
            invoker ?? UnavailableEditorCommandInvoker.Instance);
    }
}

public interface IEditorCommandInvoker
{
    EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        IEditorCommand<TOptions, TResult> command,
        TOptions options);

    EditorCommandExecutionResult<TResult> Execute<TOptions, TResult>(
        string operationId,
        TOptions options);
}

public sealed record EditorQueryContext(
    MidiEditorSessionState Session,
    EditableMidiFile File,
    EditorSelectionSnapshot Selection,
    EditorQueryServices Services,
    CancellationToken CancellationToken)
{
    public static EditorQueryContext Create(
        MidiEditorSessionState session,
        EditorQueryServices services = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.File is null)
            throw new InvalidOperationException("Editor query context requires an open MIDI file.");

        return new EditorQueryContext(
            session,
            session.File,
            session.Selection.CreateSnapshot(),
            services ?? EditorQueryServices.Empty,
            cancellationToken);
    }
}

public sealed record PreviewCommandContext(
    PreviewSessionState Preview,
    EditableMidiFile File,
    EditorSelectionSnapshot Selection,
    IEditorPreviewSettings Settings,
    IEditorPreviewInstrumentCatalog InstrumentCatalog,
    IEditorPreviewSoundPlayer SoundPlayer,
    IEditorPreviewScheduler Scheduler,
    CancellationToken CancellationToken);

public sealed record PreviewQueryContext(
    PreviewSessionState Preview,
    EditableMidiFile File,
    EditorSelectionSnapshot Selection,
    IEditorPreviewSettings Settings,
    IEditorPreviewInstrumentCatalog InstrumentCatalog,
    CancellationToken CancellationToken);

public sealed class EditorCommandServices
{
    public static EditorCommandServices Empty { get; } = new();

    public EditorCommandRegistry CommandRegistry { get; init; }
}

public sealed class EditorQueryServices
{
    public static EditorQueryServices Empty { get; } = new();
}

public interface IEditorPreviewSettings
{
}

public interface IEditorPreviewInstrumentCatalog
{
}

public interface IEditorPreviewSoundPlayer
{
}

public interface IEditorPreviewScheduler
{
}
