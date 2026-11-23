using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl;
using MidiBard.Extensions.Json;
using MidiBard.Managers;
using MidiBard.RemoteSync.Client;
using MidiBard.RemoteSync.Server;
using MidiBard.Structs;

namespace MidiBard.RemoteSync;

/// <summary>
/// Facade owned by <see cref="Plugin"/>. There is no explicit role: hosting the
/// sync server makes this client the Conductor, connecting to one makes it a
/// Follower. The <see cref="Executor"/> always exists so a client that is
/// neither (a sibling behind a gateway) still applies commands relayed over
/// local IPC.
/// </summary>
internal sealed class RemoteSyncManager : IDisposable
{
    private readonly Plugin _plugin;
    private string _localCharacterName = string.Empty;

    public SyncServer? Server { get; private set; }
    public SyncClient? Client { get; private set; }
    public PlaybackProgressBroadcaster? Broadcaster { get; private set; }
    public SyncCommandExecutor Executor { get; }

    public bool IsConductorActive => Server != null;
    public bool IsFollowerActive => Client != null;

    /// <summary>
    /// True when a remote conductor dictates this client's track assignment: connected
    /// as a follower, or a same-machine sibling receiving the gateway relay. Such clients
    /// skip the load-time auto-equip, because their local track status is stale until the
    /// conductor's config arrives, which then equips the assigned instrument.
    /// </summary>
    public bool IsFollowingConductor
        => !IsConductorActive
           && (Client?.State == SyncClientState.Connected || Executor.IsConductorPresent);

    /// <summary>
    /// Instrument attack compensation to apply to solo playback while remote sync is
    /// active, or null for direct (uncompensated) key presses. The conductor uses its own
    /// setting; followers and gateway siblings use the mode the conductor dictates.
    /// Read per MIDI event on the playback thread.
    /// </summary>
    public CompensationModes? ActiveCompensationMode
    {
        get
        {
            if (IsConductorActive)
            {
                var mode = _plugin.Config.RemoteSyncCompensationMode;
                return mode == CompensationModes.None ? null : mode;
            }

            return Executor.ConductorCompensationMode;
        }
    }

    public RemoteSyncManager(Plugin plugin)
    {
        _plugin = plugin;
        Executor = new SyncCommandExecutor(plugin);
    }

    /// <summary>
    /// Rebuilds server/client from the current configuration. Must be called on the
    /// framework thread (settings UI); also restarts the shared TCP listener so the
    /// <c>/ws/sync</c> endpoint appears or disappears with the conductor role.
    /// </summary>
    public void Refresh()
    {
        _localCharacterName =
            DalamudApi.ObjectTable.LocalPlayer?.Name.ToString() ?? string.Empty;

        Broadcaster?.Dispose();
        Broadcaster = null;
        Server?.Dispose();
        Server = null;
        Client?.Dispose();
        Client = null;

        var config = _plugin.Config;
        if (config.RemoteSyncServerEnabled)
        {
            Server = new SyncServer(() => _localCharacterName);
            Broadcaster = new PlaybackProgressBroadcaster(_plugin, Server);
        }
        else if (config.RemoteSyncConnectEnabled)
        {
            var client = new SyncClient(
                config.RemoteSyncServerAddress,
                config.RemoteSyncServerToken,
                GetLocalIdentityAsync,
                OnConductorMessageAsync,
                BuildStatusReport);
            client.Start();
            Client = client;
        }

        // Track the solo slot grid while either role is active (diagnostic for the
        // per-client 50 ms quantization phase; see SoloGridTracker).
        if (_plugin.SoloPerformanceMonitor is { } soloMonitor)
            soloMonitor.GridTrackingEnabled = Server != null || Client != null;

        // The listener hosts /ws/sync, so it must restart whenever the conductor
        // role toggles; RefreshRemoteControlServer reads this manager's state.
        // The tunnel follows the listener (a remote Conductor is reached through it).
        _plugin.RefreshRemoteControlServer();
        _plugin.RefreshTunnelService();
    }

    /// <summary>Handler passed to the remote-control listener for upgraded <c>/ws/sync</c> connections.</summary>
    public Task HandleWebSocketAsync(WebSocket socket, string ipAddress)
    {
        var server = Server;
        if (server == null)
        {
            socket.Abort();
            return Task.CompletedTask;
        }

        return server.HandleWebSocketAsync(socket, ipAddress);
    }

    /// <summary>Entry point for sync messages relayed by a same-machine gateway over local IPC.</summary>
    public void HandleRelayedEnvelope(string protocolJson)
    {
        // The conductor is the source of truth and a WS-connected follower already
        // receives everything first-hand; only passive siblings obey the relay.
        if (IsConductorActive || IsFollowerActive)
            return;

        var message = SyncProtocol.Deserialize(protocolJson);
        if (message != null)
            Executor.ApplyFromGateway(message);
    }

    //  Conductor-side lifecycle notifications (hooked from MidiPlayerControl/FilePlayback)

    public void NotifyPlay(long positionMs) => Broadcaster?.NotifyPlay(positionMs);

    public void NotifyPause() => Broadcaster?.NotifyPause();

    public void NotifyStop() => Broadcaster?.NotifyStop();

    public void NotifySeek(long positionMs) => Broadcaster?.NotifySeek(positionMs);

    public void NotifyLoad(string filePath) => Broadcaster?.NotifyLoad(filePath);

    /// <summary>
    /// Pushes the current track/performer assignment to all followers (no-op unless
    /// Conductor). Mirrors the local <c>UpdateMidiFileConfig</c> IPC broadcast.
    /// </summary>
    public void NotifyMidiFileConfig(MidiFileConfig? config, string? filePath = null)
    {
        var broadcaster = Broadcaster;
        if (broadcaster == null || config == null)
            return;

        try
        {
            var fileName = Path.GetFileName(filePath ?? _plugin.CurrentBardPlayback?.FilePath ?? string.Empty);
            broadcaster.NotifyMidiFileConfig(fileName, config.JsonSerialize());
        }
        catch (Exception exception)
        {
            DalamudApi.PluginLog.Warning(exception, "[RemoteSync] Failed to broadcast midi file config.");
        }
    }

    /// <summary>Tells all followers to equip or put away their instrument (no-op unless Conductor).</summary>
    public void NotifyEquipInstruments(bool takeout) => Broadcaster?.NotifyEquipInstruments(takeout);

    /// <summary>
    /// ContentIds of every remote-sync participant: the conductor itself plus all
    /// identified followers. Empty unless this client is the Conductor. Lets
    /// party-based logic (track assignment rules, member presence) treat connected
    /// followers as "present" in solo mode.
    /// </summary>
    public List<ulong> GetParticipantCids()
    {
        var result = new List<ulong>();
        var server = Server;
        if (server == null)
            return result;

        var selfCid = DalamudApi.PlayerState.ContentId;
        if (selfCid != 0)
            result.Add(selfCid);

        foreach (var session in server.SnapshotSessions())
        {
            if (session.HelloReceived && session.ContentId != 0)
                result.Add((ulong)session.ContentId);
        }

        return result;
    }

    /// <summary>
    /// Conductor with connected followers: routes a user-initiated play into a
    /// scheduled start so every client (this one included) begins at the same
    /// instant. Returns true when scheduled - the caller must not start playback.
    /// </summary>
    public bool TryScheduleRemoteStart()
    {
        if (Server is not { SessionCount: > 0 } || Broadcaster == null)
            return false;

        if (AgentPerformanceMetronome.IsEnsembleModeRunning)
            return false;

        StartAll(Math.Clamp(_plugin.Config.RemoteSyncPlayLeadMs, 300, 10_000));
        return true;
    }

    /// <summary>
    /// Synchronized start for everyone, the conductor included: followers receive a
    /// <c>scheduleStart</c> and the conductor schedules itself from the same instant.
    /// </summary>
    public void StartAll(int leadMs = 3000)
    {
        var broadcaster = Broadcaster;
        if (broadcaster == null)
            return;

        var message = broadcaster.BroadcastScheduledStart(leadMs);
        Executor.ApplyFromGateway(message with { LocalClock = true });
    }

    private Task OnConductorMessageAsync(SyncMessage message)
    {
        Executor.ApplyFromConductor(message, Client?.Clock.OffsetMs ?? 0);
        return Task.CompletedTask;
    }

    private StatusReportMessage BuildStatusReport()
    {
        var playback = _plugin.CurrentBardPlayback;
        var state = _plugin.MidiPlayerControl._status switch
        {
            MidiPlayerControl.MidiPlayerStatus.Playing => SyncPlaybackState.Playing,
            MidiPlayerControl.MidiPlayerStatus.Paused => SyncPlaybackState.Paused,
            _ => SyncPlaybackState.Stopped,
        };

        long positionMs = 0;
        string? fileName = null;
        if (playback?.IsLoaded == true)
        {
            try
            {
                positionMs = (long)playback.GetCurrentTime<MetricTimeSpan>().TotalMilliseconds;
            }
            catch
            {
                // Playback may be disposed mid-read.
            }

            fileName = Path.GetFileName(playback.FilePath);
        }

        return new StatusReportMessage
        {
            State = state,
            PositionMs = positionMs,
            DriftMs = Executor.LastDriftMs,
            FileName = fileName,
            SoloGridPhaseMs = GetLocalGridPhase(Client?.Clock.OffsetMs ?? 0),
            SoloGridSessionId = _plugin.SoloPerformanceMonitor?.Grid.SessionId,
        };
    }

    /// <summary>
    /// This client's solo slot-grid phase [0, 50) on the conductor's clock
    /// (<paramref name="offsetMs"/> = conductor − local; 0 on the conductor), or null
    /// when no solo send session is active.
    /// </summary>
    public int? GetLocalGridPhase(long offsetMs = 0)
    {
        var grid = _plugin.SoloPerformanceMonitor?.Grid;
        return grid?.GetBoundaryMs(SyncClock.NowMs) is { } boundary
            ? SoloGridTracker.SlotPhase(boundary, offsetMs)
            : null;
    }

    private static Task<(string CharacterName, long ContentId)> GetLocalIdentityAsync()
        => DalamudApi.Framework.Run(() =>
        {
            var name = DalamudApi.ObjectTable.LocalPlayer?.Name.ToString() ?? string.Empty;
            var contentId = (long)DalamudApi.PlayerState.ContentId;
            return (name, contentId);
        });

    public void Dispose()
    {
        if (_plugin.SoloPerformanceMonitor is { } soloMonitor)
            soloMonitor.GridTrackingEnabled = false;

        Broadcaster?.Dispose();
        Broadcaster = null;
        Server?.Dispose();
        Server = null;
        Client?.Dispose();
        Client = null;
    }
}
