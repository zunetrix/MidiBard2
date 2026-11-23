using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl;

namespace MidiBard.RemoteSync.Server;

/// <summary>
/// Conductor side: broadcasts periodic <see cref="ProgressMessage"/> frames and
/// immediate lifecycle events (play/pause/stop/seek/load) to all followers.
/// Position reads go straight to DryWetMidi's lock-protected playback clock,
/// so the loop runs on a background task without framework-thread hops.
/// </summary>
internal sealed class PlaybackProgressBroadcaster : IDisposable
{
    private readonly Plugin _plugin;
    private readonly SyncServer _server;
    private readonly CancellationTokenSource _cancellation = new();

    public PlaybackProgressBroadcaster(Plugin plugin, SyncServer server)
    {
        _plugin = plugin;
        _server = server;
        _ = Task.Run(BroadcastLoopAsync);
    }

    /// <summary>
    /// SyncClock instant until which conductor-side play notifications are muted.
    /// Set by <see cref="BroadcastScheduledStart"/>: the conductor starts itself from
    /// the same schedule, and the resulting DoPlay must not echo a <see cref="PlayMessage"/>
    /// that would re-seek followers right after their own scheduled start.
    /// </summary>
    private long _suppressPlayUntil;

    // Immediate lifecycle events (called from MidiPlayerControl/FilePlayback hooks)
    public void NotifyPlay(long positionMs)
    {
        if (SyncClock.NowMs < Volatile.Read(ref _suppressPlayUntil))
            return;

        _server.Broadcast(new PlayMessage { PositionMs = positionMs, SentAt = SyncClock.NowMs });
    }

    public void NotifyPause() => _server.Broadcast(new PauseMessage());

    public void NotifyStop() => _server.Broadcast(new StopMessage());

    public void NotifySeek(long positionMs) => _server.Broadcast(new SeekMessage { PositionMs = positionMs });

    public void NotifyLoad(string filePath)
        => _server.Broadcast(new LoadFileMessage { FileName = Path.GetFileName(filePath), FilePath = filePath });

    public void NotifySyncFolderReload() => _server.Broadcast(new SyncFolderReloadMessage());

    /// <summary>Pushes the current song's track/performer assignment to all followers.</summary>
    public void NotifyMidiFileConfig(string fileName, string configJson)
        => _server.Broadcast(new MidiConfigMessage { FileName = fileName, ConfigJson = configJson });

    /// <summary>Tells all followers to equip (or put away) their assigned track's instrument.</summary>
    public void NotifyEquipInstruments(bool takeout)
        => _server.Broadcast(new EquipInstrumentMessage { Takeout = takeout });

    /// <summary>
    /// Broadcasts a start scheduled <paramref name="leadMs"/> from now at the current
    /// position and returns the message so the conductor can schedule itself from it.
    /// </summary>
    public ScheduleStartMessage BroadcastScheduledStart(int leadMs = 3000)
    {
        var message = new ScheduleStartMessage
        {
            StartAtMs = SyncClock.NowMs + leadMs,
            PositionMs = GetPositionMsSafe(),
        };

        Volatile.Write(ref _suppressPlayUntil, message.StartAtMs + 1000);
        _server.Broadcast(message);
        return message;
    }

    // Periodic progress
    private async Task BroadcastLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            var interval = Math.Clamp(_plugin.Config.RemoteSyncBroadcastIntervalMs, 50, 5000);
            try
            {
                await Task.Delay(interval, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_server.SessionCount == 0)
                continue;

            try
            {
                _server.Broadcast(BuildProgress());
            }
            catch (Exception exception)
            {
                DalamudApi.PluginLog.Warning(exception, "[RemoteSync] Progress broadcast failed.");
            }
        }
    }

    private ProgressMessage BuildProgress()
    {
        var playback = _plugin.CurrentBardPlayback;
        var state = _plugin.MidiPlayerControl._status switch
        {
            MidiPlayerControl.MidiPlayerStatus.Playing => SyncPlaybackState.Playing,
            MidiPlayerControl.MidiPlayerStatus.Paused => SyncPlaybackState.Paused,
            _ => SyncPlaybackState.Stopped,
        };

        long positionMs = 0, durationMs = 0;
        float speed = 1f;
        string? fileName = null;

        if (playback?.IsLoaded == true)
        {
            positionMs = GetPositionMsSafe();
            try
            {
                durationMs = (long)playback.GetDuration<MetricTimeSpan>().TotalMilliseconds;
            }
            catch
            {
                // Playback may be disposed mid-read; next tick recovers.
            }

            speed = (float)playback.Speed;
            fileName = Path.GetFileName(playback.FilePath);
        }

        return new ProgressMessage
        {
            State = state,
            PositionMs = positionMs,
            DurationMs = durationMs,
            Speed = speed,
            FileName = fileName,
            SentAt = SyncClock.NowMs,
            CompensationMode = _plugin.Config.RemoteSyncCompensationMode,
        };
    }

    private long GetPositionMsSafe()
    {
        try
        {
            var playback = _plugin.CurrentBardPlayback;
            if (playback?.IsLoaded != true)
                return 0;

            return (long)playback.GetCurrentTime<MetricTimeSpan>().TotalMilliseconds;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
