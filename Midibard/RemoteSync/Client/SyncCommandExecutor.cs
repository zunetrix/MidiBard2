using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.Json;
using MidiBard.Managers;

namespace MidiBard.RemoteSync.Client;

/// <summary>
/// Applies conductor commands to the local playback. Shared by two entry points:
/// messages received over the WebSocket (gateway client) and messages relayed by
/// the gateway over local IPC (sibling clients). Game/playback mutations always
/// run on the framework thread. When this client is the gateway
/// (<see cref="Configuration.RemoteSyncRelayToLocalIpc"/>), WebSocket messages are
/// re-broadcast to same-machine siblings with clock fields rewritten to the local
/// machine clock, so siblings never need their own offset measurement.
/// </summary>
internal sealed class SyncCommandExecutor
{
    private readonly Plugin _plugin;
    private readonly PlaybackSyncCorrector _corrector = new();

    /// <summary>Last measured drift vs the conductor (ms, positive = ahead). long.MinValue = unknown.</summary>
    private long _lastDriftMs = long.MinValue;

    public long? LastDriftMs
    {
        get
        {
            var value = System.Threading.Interlocked.Read(ref _lastDriftMs);
            return value == long.MinValue ? null : value;
        }
    }

    /// <summary>Conductor-derived state (presence, compensation mode) expires this long after the last progress frame.</summary>
    private const long ConductorPresenceTtlMs = 3000;

    private int _conductorCompensationMode = (int)CompensationModes.None;
    private long _lastProgressSeenAtMs = long.MinValue;

    /// <summary>
    /// True while conductor progress frames keep arriving, directly over the WebSocket
    /// or relayed by a same-machine gateway (so passive siblings count as followers too).
    /// </summary>
    public bool IsConductorPresent
    {
        get
        {
            var seenAt = System.Threading.Interlocked.Read(ref _lastProgressSeenAtMs);
            return seenAt != long.MinValue && SyncClock.NowMs - seenAt <= ConductorPresenceTtlMs;
        }
    }

    /// <summary>
    /// Compensation mode dictated by the conductor, or null when none is active or the
    /// conductor went silent (disconnect, sync disabled). Read per MIDI event on the
    /// playback thread, hence the lock-free fields.
    /// </summary>
    public CompensationModes? ConductorCompensationMode
    {
        get
        {
            if (!IsConductorPresent)
                return null;

            var mode = (CompensationModes)System.Threading.Volatile.Read(ref _conductorCompensationMode);
            return mode == CompensationModes.None ? null : mode;
        }
    }

    public SyncCommandExecutor(Plugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>Handles a message received from the conductor over the WebSocket.</summary>
    /// <param name="offsetMs">Measured clock offset (conductor − local).</param>
    public void ApplyFromConductor(SyncMessage message, long offsetMs)
    {
        Apply(message, offsetMs);
        RelayToSiblings(message, offsetMs);
    }

    /// <summary>Handles a message relayed by the gateway over local IPC (clock fields already local).</summary>
    public void ApplyFromGateway(SyncMessage message)
        => Apply(message, offsetMs: 0);

    private void Apply(SyncMessage message, long offsetMs)
    {
        switch (message)
        {
            case ProgressMessage progress:
                ApplyProgress(progress, offsetMs);
                break;

            case PlayMessage play:
                _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    _corrector.Reset();
                    var playback = _plugin.CurrentBardPlayback;
                    if (playback?.IsLoaded != true)
                        return;

                    // Compensate the delivery latency: the conductor has been playing
                    // since SentAt, so start that much further in.
                    var targetMs = play.PositionMs;
                    if (play.SentAt != 0)
                        targetMs += Math.Max(0, SyncClock.NowMs - (play.SentAt - offsetMs));

                    if (targetMs > 0)
                        playback.MoveToTime(new MetricTimeSpan(targetMs * 1000L));

                    EnsureSoloPlaybackPath();
                    _plugin.MidiPlayerControl.DoPlay();

                    // Pull the start residual (below the soft threshold) down to ~zero.
                    _corrector.BeginConvergence();
                });
                break;

            case PauseMessage:
                _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    _corrector.Reset();
                    RestoreBaseSpeed();
                    if (_plugin.MidiPlayerControl._status == MidiPlayerControl.MidiPlayerStatus.Playing)
                        _plugin.MidiPlayerControl.Pause();
                });
                break;

            case StopMessage:
                _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    _corrector.Reset();
                    if (_plugin.MidiPlayerControl._status != MidiPlayerControl.MidiPlayerStatus.Stopped)
                        _plugin.MidiPlayerControl.Stop();
                });
                break;

            case SeekMessage seek:
                _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
                {
                    _corrector.Reset();
                    _plugin.MidiPlayerControl.SetTime(new MetricTimeSpan(seek.PositionMs * 1000L));
                });
                break;

            case LoadFileMessage load:
                HandleLoadFile(load);
                break;

            case SyncFolderReloadMessage:
                ReloadSyncFolder();
                break;

            case ScheduleStartMessage schedule:
                ScheduleStart(schedule, offsetMs);
                break;

            case MidiConfigMessage midiConfig:
                ApplyMidiFileConfig(midiConfig);
                break;

            case EquipInstrumentMessage equip:
                _ = DalamudApi.Framework.RunOnFrameworkThread(
                    () => _plugin.IpcProvider.ApplySetInstrument(equip.Takeout));
                break;
        }
    }

    // Drift correction
    private void ApplyProgress(ProgressMessage progress, long offsetMs)
    {
        // Mode first: it must be current before playback starts, not only while playing.
        System.Threading.Volatile.Write(ref _conductorCompensationMode, (int)progress.CompensationMode);
        System.Threading.Interlocked.Exchange(ref _lastProgressSeenAtMs, SyncClock.NowMs);

        if (progress.State != SyncPlaybackState.Playing)
            return;

        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var playback = _plugin.CurrentBardPlayback;
            if (playback?.IsLoaded != true)
            {
                _stateMismatchSinceMs = 0;
                return;
            }

            if (!playback.IsRunning)
            {
                // Conductor is playing but this client is not (missed 'play' while
                // still loading, late join, manual pause): join after a short grace.
                TryRecoverPlayingState(progress, offsetMs, playback);
                return;
            }

            _stateMismatchSinceMs = 0;

            long localMs;
            try
            {
                localMs = (long)playback.GetCurrentTime<MetricTimeSpan>().TotalMilliseconds;
            }
            catch
            {
                return;
            }

            // offset = conductor − local, so the send instant on the local clock
            // is SentAt − offset. Elapsed network+scheduling time advances the
            // conductor's position at its playback speed.
            var elapsedMs = SyncClock.NowMs - (progress.SentAt - offsetMs);
            var expected = progress.PositionMs + (long)(elapsedMs * progress.Speed);
            var drift = localMs - expected;
            System.Threading.Interlocked.Exchange(ref _lastDriftMs, drift);

            var config = _plugin.Config;
            var correction = _corrector.Evaluate(
                drift,
                expected,
                progress.DurationMs,
                config.RemoteSyncSoftThresholdMs,
                config.RemoteSyncHardThresholdMs,
                config.RemoteSyncMaxSpeedNudge);

            switch (correction.Kind)
            {
                case SyncCorrectionKind.ResetSpeed:
                    RestoreBaseSpeed();
                    break;

                case SyncCorrectionKind.NudgeSpeed:
                    var baseSpeed = config.PlaySpeed;
                    playback.Speed = (float)Math.Clamp(
                        baseSpeed * correction.SpeedFactor,
                        baseSpeed * 0.9,
                        baseSpeed * 1.1);
                    break;

                case SyncCorrectionKind.Jump:
                    DalamudApi.PluginLog.Warning($"[RemoteSync] Drift {drift:+0;-0}ms -> hard seek.");
                    playback.MoveToTime(new MetricTimeSpan(correction.TargetPositionMs * 1000L));
                    break;
            }
        });
    }

    private void RestoreBaseSpeed()
    {
        var playback = _plugin.CurrentBardPlayback;
        if (playback?.IsLoaded == true)
            playback.Speed = _plugin.Config.PlaySpeed;
    }

    /// <summary>Grace period before a follower force-joins a playing conductor (ms).</summary>
    private const int StateMismatchGraceMs = 2000;
    private long _stateMismatchSinceMs;

    private void TryRecoverPlayingState(ProgressMessage progress, long offsetMs, BardPlayback playback)
    {
        // Only when it's clearly the same song.
        if (!string.IsNullOrEmpty(progress.FileName) &&
            !string.Equals(Path.GetFileName(playback.FilePath), progress.FileName, StringComparison.OrdinalIgnoreCase))
        {
            _stateMismatchSinceMs = 0;
            return;
        }

        var now = SyncClock.NowMs;
        if (_stateMismatchSinceMs == 0)
        {
            _stateMismatchSinceMs = now;
            return;
        }

        if (now - _stateMismatchSinceMs < StateMismatchGraceMs)
            return;

        _stateMismatchSinceMs = 0;

        var elapsedMs = now - (progress.SentAt - offsetMs);
        var target = Math.Max(0, progress.PositionMs + (long)(elapsedMs * progress.Speed));
        DalamudApi.PluginLog.Warning(
            $"[RemoteSync] Conductor is playing but this client is not - joining at {target}ms.");

        _corrector.Reset();
        playback.MoveToTime(new MetricTimeSpan(target * 1000L));
        EnsureSoloPlaybackPath();
        _plugin.MidiPlayerControl.DoPlay();
        _corrector.BeginConvergence();
    }

    // File / playlist commands
    private void HandleLoadFile(LoadFileMessage load)
    {
        var fileName = Path.GetFileName(load.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        _ = Task.Run(async () =>
        {
            // Already on that song? Nothing to do.
            var current = _plugin.CurrentBardPlayback;
            if (current?.IsLoaded == true &&
                string.Equals(Path.GetFileName(current.FilePath), fileName, StringComparison.OrdinalIgnoreCase))
                return;

            // 1. Current playlist - the usual setup is every client sharing the
            //    conductor's playlist; loading by index keeps auto-advance working.
            var songs = _plugin.PlaylistManager.CurrentPlaylist?.Songs;
            if (songs != null)
            {
                var index = songs.FindIndex(ps => string.Equals(
                    Path.GetFileName(ps?.Song?.FilePath ?? string.Empty), fileName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && await _plugin.PlaylistManager.LoadPlayback(index, startPlaying: false, sync: false))
                {
                    DalamudApi.PluginLog.Information($"[RemoteSync] Loaded '{fileName}' from the current playlist (#{index + 1}).");
                    return;
                }
            }

            // 2. Sync folder (files pushed by the conductor).
            var folder = _plugin.Config.RemoteSyncFolder;
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var candidate = Path.Combine(folder, fileName);
                if (File.Exists(candidate) && await _plugin.FilePlayback.LoadPlayback(candidate))
                {
                    DalamudApi.PluginLog.Information($"[RemoteSync] Loaded '{fileName}' from the sync folder.");
                    return;
                }
            }

            // 3. The conductor's absolute path - resolves for clients on the same
            //    machine or sharing a network drive.
            if (!string.IsNullOrWhiteSpace(load.FilePath) && IsMidiFile(load.FilePath) && File.Exists(load.FilePath)
                && await _plugin.FilePlayback.LoadPlayback(load.FilePath))
            {
                DalamudApi.PluginLog.Information($"[RemoteSync] Loaded '{fileName}' from the conductor's path.");
                return;
            }

            DalamudApi.PluginLog.Warning(
                $"[RemoteSync] 'load': '{fileName}' not found in the current playlist, sync folder or conductor path.");
        });
    }

    private static bool IsMidiFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".mid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".midi", StringComparison.OrdinalIgnoreCase);
    }

    private void ReloadSyncFolder()
    {
        _ = Task.Run(async () =>
        {
            var folder = _plugin.Config.RemoteSyncFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                DalamudApi.PluginLog.Warning(
                    $"[RemoteSync] syncFolderReload: folder '{folder}' not found. Push files first.");
                return;
            }

            var files = Directory
                .EnumerateFiles(folder, "*.mid", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(folder, "*.midi", SearchOption.TopDirectoryOnly))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                DalamudApi.PluginLog.Information($"[RemoteSync] syncFolderReload: no MIDI files in '{folder}'.");
                return;
            }

            await _plugin.PlaylistManager.LoadTempPlaylistAsync(files);
            DalamudApi.PluginLog.Information(
                $"[RemoteSync] Temp playlist rebuilt from sync folder ({files.Length} files).");
        });
    }

    // Track/performer assignment
    private void ApplyMidiFileConfig(MidiConfigMessage message)
    {
        if (string.IsNullOrEmpty(message.ConfigJson))
            return;

        _ = Task.Run(async () =>
        {
            MidiFileConfig? config;
            try
            {
                config = message.ConfigJson.JsonDeserialize<MidiFileConfig>();
            }
            catch (Exception exception)
            {
                DalamudApi.PluginLog.Warning(exception, "[RemoteSync] midiConfig: malformed config payload.");
                return;
            }

            if (config?.Tracks == null)
                return;

            // The accompanying 'load' may still be in flight; wait for the right file.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !IsConfigTargetLoaded(message.FileName))
                await Task.Delay(100);

            await DalamudApi.Framework.RunOnFrameworkThread(() =>
            {
                var playback = _plugin.CurrentBardPlayback;
                if (playback?.IsLoaded != true || !IsConfigTargetLoaded(message.FileName))
                {
                    DalamudApi.PluginLog.Warning(
                        $"[RemoteSync] midiConfig: '{message.FileName}' is not the loaded song; config not applied.");
                    return;
                }

                playback.MidiFileConfig = config;
                playback.SyncTrackStatusWithMidiFileConfig();
                DalamudApi.PluginLog.Information(
                    $"[RemoteSync] Applied track assignment for '{message.FileName}'.");

                EquipAssignedInstrument(playback, config);
            });
        });
    }

    /// <summary>
    /// Takes over the load-time auto-equip that followers skip (see
    /// <see cref="RemoteSyncManager.IsFollowingConductor"/>): equips the instrument of the
    /// first track the conductor assigned to this character. Never while playing, and
    /// leaves the current instrument alone when nothing is assigned here.
    /// </summary>
    private void EquipAssignedInstrument(BardPlayback playback, MidiFileConfig config)
    {
        if (!_plugin.Config.bmpTrackNames || playback.IsRunning)
            return;

        var cid = DalamudApi.PlayerState.ContentId;
        var track = config.Tracks.FirstOrDefault(t =>
            t.Enabled && MidiFileConfig.IsCidOnTrack(cid, t, _plugin.Config.EnsembleMemberConfigs));
        if (track == null)
            return;

        DalamudApi.PluginLog.Debug($"[RemoteSync] Equipping assigned instrument {track.Instrument}.");
        _plugin.InstrumentSwitcher.SwitchToContinue(track.Instrument);
    }

    private bool IsConfigTargetLoaded(string fileName)
    {
        var playback = _plugin.CurrentBardPlayback;
        if (playback?.IsLoaded != true)
            return false;

        return string.IsNullOrEmpty(fileName)
            || string.Equals(Path.GetFileName(playback.FilePath), fileName, StringComparison.OrdinalIgnoreCase);
    }

    // Scheduled start
    private void ScheduleStart(ScheduleStartMessage schedule, long offsetMs)
    {
        var startAtLocal = schedule.LocalClock ? schedule.StartAtMs : schedule.StartAtMs - offsetMs;
        var delayMs = startAtLocal - SyncClock.NowMs;

        if (delayMs < -2000)
        {
            DalamudApi.PluginLog.Warning(
                $"[RemoteSync] scheduleStart arrived {-delayMs}ms late; ignoring.");
            return;
        }

        _ = Task.Run(async () =>
        {
            // Pre-seek while waiting so the start itself is just DoPlay.
            await DalamudApi.Framework.RunOnFrameworkThread(() =>
            {
                _corrector.Reset();
                var playback = _plugin.CurrentBardPlayback;
                if (playback?.IsLoaded == true)
                    _plugin.MidiPlayerControl.SetTime(new MetricTimeSpan(Math.Max(0, schedule.PositionMs) * 1000L));
            });

            // Hop onto the framework thread BEFORE the start instant and spin there:
            // RunOnFrameworkThread callbacks run on the next game frame, so hopping
            // at the instant itself would add 0..frame-interval of per-client jitter
            // (and a systematic delay for clients rendering slower than the conductor).
            double frameMs;
            try { frameMs = DalamudApi.Framework.UpdateDelta.TotalMilliseconds; }
            catch { frameMs = 17; }
            var frameLeadMs = (long)Math.Clamp(frameMs * 2.5 + 10, 40, 250);

            var remaining = startAtLocal - SyncClock.NowMs;
            if (remaining > frameLeadMs + 40)
                await Task.Delay((int)(remaining - frameLeadMs - 20));

            await DalamudApi.Framework.RunOnFrameworkThread(() =>
            {
                // Busy-wait the last milliseconds on the game thread for a frame-exact start.
                while (SyncClock.NowMs < startAtLocal)
                    System.Threading.Thread.SpinWait(64);

                if (_plugin.CurrentBardPlayback?.IsLoaded != true)
                    return;

                EnsureSoloPlaybackPath();
                _plugin.MidiPlayerControl.DoPlay();
                DalamudApi.PluginLog.Information(
                    $"[RemoteSync] Scheduled start fired {SyncClock.NowMs - startAtLocal}ms after target.");

                // Pull the start residual (below the soft threshold) down to ~zero.
                _corrector.BeginConvergence();
            });
        });
    }

    /// <summary>
    /// Remote sync always plays through the direct solo path. A stale ensemble
    /// timer (known quirk: it can stay running after heartbeat/ensemble use out of
    /// party) silently routes every note through the ensemble compensation buffer,
    /// delaying this client's audio relative to clients with a clean timer.
    /// </summary>
    private void EnsureSoloPlaybackPath()
    {
        var ensembleManager = _plugin.EnsembleManager;
        if (!DalamudApi.PartyList.IsInParty() && ensembleManager.EnsembleTimer.IsRunning)
        {
            ensembleManager.EnsembleTimer.Reset();
            DalamudApi.PluginLog.Warning(
                "[RemoteSync] Ensemble timer was running outside a party - reset so notes are not delayed by ensemble compensation.");
        }
    }

    // Gateway relay
    private void RelayToSiblings(SyncMessage message, long offsetMs)
    {
        if (!_plugin.Config.RemoteSyncRelayToLocalIpc)
            return;

        // Rewrite conductor-clock fields to the local machine clock so siblings
        // (which never measure an offset) can use them directly.
        SyncMessage? toRelay = message switch
        {
            ProgressMessage progress => progress with { SentAt = progress.SentAt - offsetMs },
            PlayMessage play when play.SentAt != 0 => play with { SentAt = play.SentAt - offsetMs },
            ScheduleStartMessage schedule when !schedule.LocalClock =>
                schedule with { StartAtMs = schedule.StartAtMs - offsetMs, LocalClock = true },
            PlayMessage or PauseMessage or StopMessage or SeekMessage
                or LoadFileMessage or SyncFolderReloadMessage or ScheduleStartMessage
                or MidiConfigMessage or EquipInstrumentMessage => message,
            _ => null, // handshake/clock/diagnostic messages are not relayed
        };

        if (toRelay == null)
            return;

        try
        {
            _plugin.IpcProvider.RemoteSyncEnvelope(SyncProtocol.SerializeToString(toRelay));
        }
        catch (Exception exception)
        {
            DalamudApi.PluginLog.Warning(exception, "[RemoteSync] Failed to relay message over IPC.");
        }
    }
}
