using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Preview;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class PreviewTransportCommandTests
{
    [Fact]
    public void Resume_StartsTransportAndSyncsSession()
    {
        var session = new PreviewSessionState();
        var transport = new FakePreviewTransport
        {
            HasEvents = true,
            DurationSeconds = 12,
            PositionSeconds = 3,
        };

        var result = Execute(
            new ResumePreviewCommand(),
            new EditorOperationEmptyOptions(),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        transport.PlayCalls.ShouldBe(1);
        session.IsPlaying.ShouldBeTrue();
        session.IsPaused.ShouldBeFalse();
        session.PlaybackPositionSeconds.ShouldBe(3);
        session.DurationSeconds.ShouldBe(12);
    }

    [Fact]
    public void Resume_SeeksToStartWhenPositionIsAtEnd()
    {
        var session = new PreviewSessionState();
        var transport = new FakePreviewTransport
        {
            HasEvents = true,
            DurationSeconds = 8,
            PositionSeconds = 8,
        };

        var result = Execute(
            new ResumePreviewCommand(),
            new EditorOperationEmptyOptions(),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        transport.SeekCalls.ShouldBe(1);
        transport.LastSeekSeconds.ShouldBe(0);
        transport.PlayCalls.ShouldBe(1);
        result.Result!.Value.PositionSeconds.ShouldBe(0);
    }

    [Fact]
    public void Resume_ReturnsNoChangeWhenPreviewHasNoEvents()
    {
        var session = new PreviewSessionState();
        var transport = new FakePreviewTransport
        {
            HasEvents = false,
            DurationSeconds = 0,
        };

        var result = Execute(
            new ResumePreviewCommand(),
            new EditorOperationEmptyOptions(),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.UserMessage.ShouldBe("Preview has no playable events.");
        transport.PlayCalls.ShouldBe(0);
        session.HasEvents.ShouldBeFalse();
        session.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void Pause_StopsRunningTransportAndMarksSessionPaused()
    {
        var session = new PreviewSessionState();
        var transport = new FakePreviewTransport
        {
            HasEvents = true,
            DurationSeconds = 10,
            PositionSeconds = 4,
            IsPlaying = true,
        };

        var result = Execute(
            new PausePreviewCommand(),
            new EditorOperationEmptyOptions(),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        transport.PauseCalls.ShouldBe(1);
        session.IsPlaying.ShouldBeFalse();
        session.IsPaused.ShouldBeTrue();
        session.PlaybackPositionSeconds.ShouldBe(4);
    }

    [Fact]
    public void Stop_StopsTransportAndResetsSessionPosition()
    {
        var session = new PreviewSessionState { IsPaused = true };
        var transport = new FakePreviewTransport
        {
            HasEvents = true,
            DurationSeconds = 10,
            PositionSeconds = 4,
            IsPlaying = true,
        };

        var result = Execute(
            new StopPreviewCommand(),
            new EditorOperationEmptyOptions(),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        transport.StopCalls.ShouldBe(1);
        session.IsPlaying.ShouldBeFalse();
        session.IsPaused.ShouldBeFalse();
        session.PlaybackPositionSeconds.ShouldBe(0);
    }

    [Fact]
    public void Seek_UpdatesTransportAndPreservesPausedSessionWhenNotPlaying()
    {
        var session = new PreviewSessionState { IsPaused = true };
        var transport = new FakePreviewTransport
        {
            HasEvents = true,
            DurationSeconds = 10,
            PositionSeconds = 2,
            IsPlaying = false,
        };

        var result = Execute(
            new SeekPreviewCommand(),
            new SeekPreviewOptions(7),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        transport.SeekCalls.ShouldBe(1);
        transport.LastSeekSeconds.ShouldBe(7);
        session.IsPaused.ShouldBeTrue();
        session.PlaybackPositionSeconds.ShouldBe(7);
    }

    [Fact]
    public void Seek_RejectsNonFinitePosition()
    {
        var result = Execute(
            new SeekPreviewCommand(),
            new SeekPreviewOptions(double.NaN),
            new PreviewSessionState(),
            new FakePreviewTransport());

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Preview seek position must be a finite number.");
    }

    [Fact]
    public void Restart_SeeksToStartAndStartsTransport()
    {
        var session = new PreviewSessionState();
        var transport = new FakePreviewTransport
        {
            HasEvents = true,
            DurationSeconds = 10,
            PositionSeconds = 5,
        };

        var result = Execute(
            new RestartPreviewCommand(),
            new EditorOperationEmptyOptions(),
            session,
            transport);

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        transport.SeekCalls.ShouldBe(1);
        transport.LastSeekSeconds.ShouldBe(0);
        transport.PlayCalls.ShouldBe(1);
        session.IsPlaying.ShouldBeTrue();
        session.PlaybackPositionSeconds.ShouldBe(0);
    }

    private static PreviewCommandExecutionResult<PreviewTransportResult> Execute<TOptions>(
        IPreviewCommand<TOptions, PreviewTransportResult> command,
        TOptions options,
        PreviewSessionState session,
        IEditorPreviewTransport transport)
        => new PreviewCommandExecutor().Execute(
            command,
            new PreviewCommandContext(
                session,
                null,
                new EditorSelectionSnapshot(-1, [], []),
                EmptyEditorPreviewSettings.Instance,
                EmptyEditorPreviewInstrumentCatalog.Instance,
                EmptyEditorPreviewSoundPlayer.Instance,
                EmptyEditorPreviewScheduler.Instance,
                transport,
                default),
            options);

    private sealed class FakePreviewTransport : IEditorPreviewTransport
    {
        public bool HasEvents { get; set; } = true;
        public bool IsPlaying { get; set; }
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; } = 10;
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int SeekCalls { get; private set; }
        public double? LastSeekSeconds { get; private set; }

        public void Play()
        {
            PlayCalls++;
            if (HasEvents)
                IsPlaying = true;
        }

        public void Pause()
        {
            PauseCalls++;
            IsPlaying = false;
        }

        public void Stop()
        {
            StopCalls++;
            IsPlaying = false;
            PositionSeconds = 0;
        }

        public void Seek(double seconds)
        {
            SeekCalls++;
            LastSeekSeconds = seconds;
            PositionSeconds = Math.Clamp(seconds, 0, Math.Max(0, DurationSeconds));
        }
    }
}
