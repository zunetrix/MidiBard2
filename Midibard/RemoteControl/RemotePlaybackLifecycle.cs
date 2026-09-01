using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MidiBard.RemoteControl;

internal enum RemotePlaybackState
{
    Idle,
    Ready,
    Playing,
    Paused,
    Completed,
}

internal enum RemotePlaybackEventType
{
    PlaybackStarted,
    PlaybackPaused,
    PlaybackCompleted,
    PlaybackStopped,
    EnsembleStarted,
    EnsembleStopped,
}

internal sealed record RemotePlaybackEvent(
    long Sequence,
    RemotePlaybackEventType Type,
    Guid PlaybackId);

internal sealed record RemotePlaybackSnapshot(
    Guid? PlaybackId,
    string? FileName,
    long DurationMs,
    RemotePlaybackState State);

internal sealed class RemoteEventHistoryLostException : Exception
{
    public RemoteEventHistoryLostException()
        : base("Requested event history is no longer available.")
    {
    }
}

internal sealed class RemoteEventJournal
{
    private readonly object _sync = new();
    private readonly Queue<RemotePlaybackEvent> _events = new();
    private readonly int _capacity;
    private long _latestSequence;

    public RemoteEventJournal(int capacity = 256)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public long LatestSequence
    {
        get
        {
            lock (_sync)
                return _latestSequence;
        }
    }

    public RemotePlaybackEvent Publish(Guid playbackId, RemotePlaybackEventType type)
    {
        lock (_sync)
        {
            var item = new RemotePlaybackEvent(++_latestSequence, type, playbackId);
            _events.Enqueue(item);
            while (_events.Count > _capacity)
                _events.Dequeue();

            Monitor.PulseAll(_sync);
            return item;
        }
    }

    public IReadOnlyList<RemotePlaybackEvent> GetAfter(long sequence)
    {
        lock (_sync)
        {
            ThrowIfHistoryLost(sequence);
            return _events.Where(item => item.Sequence > sequence).ToArray();
        }
    }

    public IReadOnlyList<RemotePlaybackEvent> WaitForEventsAfter(long sequence, TimeSpan timeout)
    {
        lock (_sync)
        {
            ThrowIfHistoryLost(sequence);

            if (_latestSequence <= sequence && timeout > TimeSpan.Zero)
                Monitor.Wait(_sync, timeout);

            ThrowIfHistoryLost(sequence);
            return _events.Where(item => item.Sequence > sequence).ToArray();
        }
    }

    private void ThrowIfHistoryLost(long sequence)
    {
        if (_events.Count == 0)
            return;

        var oldestSequence = _events.Peek().Sequence;
        if (sequence < oldestSequence - 1)
            throw new RemoteEventHistoryLostException();
    }
}

internal sealed class RemotePlaybackLifecycle
{
    private readonly object _sync = new();

    public RemoteEventJournal Events { get; } = new();

    private Guid? _playbackId;
    private string? _fileName;
    private long _durationMs;
    private RemotePlaybackState _state = RemotePlaybackState.Idle;

    public RemotePlaybackSnapshot GetSnapshot()
    {
        lock (_sync)
            return new RemotePlaybackSnapshot(_playbackId, _fileName, _durationMs, _state);
    }

    public bool IsCurrent(Guid playbackId)
    {
        lock (_sync)
            return _playbackId == playbackId;
    }

    public RemotePlaybackSnapshot OnPlaybackLoaded(string filePath, long durationMs)
    {
        lock (_sync)
        {
            if (_playbackId is Guid previousId &&
                _state is not RemotePlaybackState.Idle and not RemotePlaybackState.Completed)
            {
                Events.Publish(previousId, RemotePlaybackEventType.PlaybackStopped);
            }

            _playbackId = Guid.NewGuid();
            _fileName = Path.GetFileName(filePath);
            _durationMs = Math.Max(0, durationMs);
            _state = RemotePlaybackState.Ready;

            return new RemotePlaybackSnapshot(_playbackId, _fileName, _durationMs, _state);
        }
    }

    public void OnPlaybackStarted()
    {
        lock (_sync)
        {
            if (_playbackId is not Guid playbackId)
                return;

            _state = RemotePlaybackState.Playing;
            Events.Publish(playbackId, RemotePlaybackEventType.PlaybackStarted);
        }
    }

    public void OnPlaybackPaused()
    {
        lock (_sync)
        {
            if (_playbackId is not Guid playbackId || _state == RemotePlaybackState.Completed)
                return;

            if (_state == RemotePlaybackState.Paused)
                return;

            _state = RemotePlaybackState.Paused;
            Events.Publish(playbackId, RemotePlaybackEventType.PlaybackPaused);
        }
    }

    public void OnPlaybackCompleted()
    {
        lock (_sync)
        {
            if (_playbackId is not Guid playbackId || _state == RemotePlaybackState.Completed)
                return;

            _state = RemotePlaybackState.Completed;
            Events.Publish(playbackId, RemotePlaybackEventType.PlaybackCompleted);
        }
    }

    public void OnPlaybackStopped()
    {
        lock (_sync)
        {
            if (_playbackId is not Guid playbackId)
                return;

            if (_state is not RemotePlaybackState.Completed and not RemotePlaybackState.Idle)
                Events.Publish(playbackId, RemotePlaybackEventType.PlaybackStopped);

            _playbackId = null;
            _fileName = null;
            _durationMs = 0;
            _state = RemotePlaybackState.Idle;
        }
    }

    public void OnEnsembleStarted()
    {
        lock (_sync)
        {
            if (_playbackId is Guid playbackId)
                Events.Publish(playbackId, RemotePlaybackEventType.EnsembleStarted);
        }
    }

    public void OnEnsembleStopped()
    {
        lock (_sync)
        {
            if (_playbackId is Guid playbackId)
                Events.Publish(playbackId, RemotePlaybackEventType.EnsembleStopped);
        }
    }
}
