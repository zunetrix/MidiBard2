using System;
using System.Collections.Generic;
using System.Threading;

using Melanchall.DryWetMidi.Common;

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
        IEditorCommandInvoker invoker = null,
        bool requireFile = true)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (requireFile && session.File is null)
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
    IEditorPreviewTransport Transport,
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
    uint DefaultInstrumentId { get; }
    bool ForceDefaultInstrument { get; }
    GuitarToneMode GuitarToneMode { get; }
    AntiStackType AntiStackType { get; }
    int TransposeGlobal { get; }
    bool AdaptNotesOOR { get; }
    IReadOnlyList<TrackStatus> TrackStatus { get; }
}

public sealed class EmptyEditorPreviewSettings : IEditorPreviewSettings
{
    public static EmptyEditorPreviewSettings Instance { get; } = new();

    public uint DefaultInstrumentId => 0;
    public bool ForceDefaultInstrument => false;
    public GuitarToneMode GuitarToneMode => GuitarToneMode.Off;
    public AntiStackType AntiStackType => AntiStackType.Off;
    public int TransposeGlobal => 0;
    public bool AdaptNotesOOR => true;
    public IReadOnlyList<TrackStatus> TrackStatus { get; } = Array.Empty<TrackStatus>();

    private EmptyEditorPreviewSettings()
    {
    }
}

public interface IEditorPreviewInstrumentCatalog
{
    uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument);
    bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId);
    bool IsGuitar(uint instrumentId);
}

public sealed class EmptyEditorPreviewInstrumentCatalog : IEditorPreviewInstrumentCatalog
{
    public static EmptyEditorPreviewInstrumentCatalog Instance { get; } = new();

    public uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument)
        => forceDefaultInstrument && defaultInstrumentId > 0
            ? defaultInstrumentId
            : null;

    public bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId)
    {
        instrumentId = 0;
        return false;
    }

    public bool IsGuitar(uint instrumentId)
        => false;

    private EmptyEditorPreviewInstrumentCatalog()
    {
    }
}

public readonly record struct PreviewSoundRequest(
    int TrackIndex,
    int Channel,
    int MidiNote,
    int GameNote,
    uint InstrumentId);

public interface IEditorPreviewSoundPlayer
{
    nint Play(PreviewSoundRequest request, out string statusMessage);
    void Stop(nint sound, uint fadeOutDuration);
}

public sealed class EmptyEditorPreviewSoundPlayer : IEditorPreviewSoundPlayer
{
    public static EmptyEditorPreviewSoundPlayer Instance { get; } = new();

    public nint Play(PreviewSoundRequest request, out string statusMessage)
    {
        statusMessage = null;
        return 0;
    }

    public void Stop(nint sound, uint fadeOutDuration)
    {
    }

    private EmptyEditorPreviewSoundPlayer()
    {
    }
}

public interface IEditorPreviewScheduler
{
}

public sealed class EmptyEditorPreviewScheduler : IEditorPreviewScheduler
{
    public static EmptyEditorPreviewScheduler Instance { get; } = new();

    private EmptyEditorPreviewScheduler()
    {
    }
}

public interface IEditorPreviewTransport
{
    bool HasEvents { get; }
    bool IsPlaying { get; }
    double PositionSeconds { get; }
    double DurationSeconds { get; }

    void Play();
    void Pause();
    void Stop();
    void Seek(double seconds);
}

public sealed class UnavailableEditorPreviewTransport : IEditorPreviewTransport
{
    public static UnavailableEditorPreviewTransport Instance { get; } = new();

    public bool HasEvents => false;
    public bool IsPlaying => false;
    public double PositionSeconds => 0;
    public double DurationSeconds => 0;

    public void Play()
    {
    }

    public void Pause()
    {
    }

    public void Stop()
    {
    }

    public void Seek(double seconds)
    {
    }

    private UnavailableEditorPreviewTransport()
    {
    }
}
