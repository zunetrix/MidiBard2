using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace MidiBard.RemoteSync.Server;

/// <summary>
/// Conductor-side session registry. Receives upgraded WebSocket connections from
/// the remote-control listener, performs the protocol handshake (hello/welcome),
/// answers clock probes, and closes sessions that go silent.
/// </summary>
internal sealed class SyncServer : IDisposable
{
    public const int ProgressIntervalMs = 1000;
    /// <summary>
    /// Follower ping cadence. Frequent enough that the estimator's sample window
    /// (~30 s) tracks oscillator drift between machines and always holds
    /// low-jitter samples.
    /// </summary>
    public const int PingIntervalMs = 2_000;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReaperInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, SyncSession> _sessions = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<string> _conductorNameProvider;

    /// <summary>Raised (on a thread-pool thread) whenever a session connects, identifies, or disconnects.</summary>
    public event Action? SessionsChanged;

    public record SessionSnapshot(
        Guid Id,
        string CharacterName,
        long ContentId,
        string IpAddress,
        DateTime ConnectedAt,
        bool HelloReceived,
        string? State,
        long? PositionMs,
        long? DriftMs,
        int? SoloGridPhaseMs,
        int? SoloGridSessionId);

    public SyncServer(Func<string> conductorNameProvider)
    {
        _conductorNameProvider = conductorNameProvider;
        _ = Task.Run(ReaperLoopAsync);
    }

    public int SessionCount => _sessions.Count;

    public List<SessionSnapshot> SnapshotSessions()
        => _sessions.Values
            .OrderBy(session => session.ConnectedAt)
            .Select(session => new SessionSnapshot(
                session.Id,
                session.CharacterName,
                session.ContentId,
                session.IpAddress,
                session.ConnectedAt,
                session.HelloReceived,
                session.LastReport?.State,
                session.LastReport?.PositionMs,
                session.LastReport?.DriftMs,
                session.LastReport?.SoloGridPhaseMs,
                session.LastReport?.SoloGridSessionId))
            .ToList();

    /// <summary>
    /// Entry point invoked by the remote-control listener after the HTTP upgrade.
    /// Returns when the client disconnects, keeping the underlying TCP connection
    /// alive for exactly as long as the session.
    /// </summary>
    public async Task HandleWebSocketAsync(WebSocket socket, string ipAddress)
    {
        var session = new SyncSession(socket, ipAddress);
        _sessions[session.Id] = session;
        SessionsChanged?.Invoke();
        DalamudApi.PluginLog.Information(
            $"[RemoteSync] Follower connected from {ipAddress} ({_sessions.Count} total).");

        try
        {
            await session.RunAsync(OnMessageAsync, _cancellation.Token);
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            SessionsChanged?.Invoke();
            DalamudApi.PluginLog.Information(
                $"[RemoteSync] Follower disconnected: {DescribeSession(session)} ({_sessions.Count} total).");
        }
    }

    private Task OnMessageAsync(SyncSession session, SyncMessage message)
    {
        switch (message)
        {
            case HelloMessage hello:
                if (hello.ProtocolVersion != SyncProtocol.ProtocolVersion)
                {
                    session.TrySend(new SyncErrorMessage
                    {
                        Code = "protocol_mismatch",
                        Message = $"Conductor speaks protocol v{SyncProtocol.ProtocolVersion}, follower sent v{hello.ProtocolVersion}. Update the older plugin.",
                    });
                    session.RequestClose(WebSocketCloseStatus.ProtocolError, "protocol mismatch");
                    return Task.CompletedTask;
                }

                session.ApplyHello(hello);
                SessionsChanged?.Invoke();
                session.TrySend(new WelcomeMessage
                {
                    ConductorName = _conductorNameProvider(),
                    ProgressIntervalMs = ProgressIntervalMs,
                    PingIntervalMs = PingIntervalMs,
                });
                DalamudApi.PluginLog.Information(
                    $"[RemoteSync] Follower identified: {DescribeSession(session)}");
                break;

            case PingMessage ping:
                session.TrySendPong(new PongMessage
                {
                    Cid = ping.Cid,
                    T0 = ping.T0,
                    T1 = session.LastFrameReceivedAtMs,
                });
                break;

            case StatusReportMessage report:
                session.LastReport = report;
                SessionsChanged?.Invoke();
                break;

            default:
                DalamudApi.PluginLog.Debug(
                    $"[RemoteSync] Unhandled message '{message.Type}' from {DescribeSession(session)}.");
                break;
        }

        return Task.CompletedTask;
    }

    public void Broadcast(SyncMessage message)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.HelloReceived)
                session.TrySend(message);
        }
    }

    public bool SendTo(Guid sessionId, SyncMessage message)
        => _sessions.TryGetValue(sessionId, out var session) && session.TrySend(message);

    private async Task ReaperLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ReaperInterval, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var cutoff = DateTime.Now - IdleTimeout;
            foreach (var session in _sessions.Values)
            {
                if (session.LastActivity >= cutoff)
                    continue;

                if (session.CloseRequested)
                {
                    // Orderly close was already requested a sweep ago and the peer
                    // never acknowledged - the connection is dead, kill it.
                    session.Abort();
                    continue;
                }

                DalamudApi.PluginLog.Warning(
                    $"[RemoteSync] Closing idle session {DescribeSession(session)} (no frames for {IdleTimeout.TotalSeconds:0}s).");
                session.RequestClose(WebSocketCloseStatus.NormalClosure, "idle timeout");
            }
        }
    }

    private static string DescribeSession(SyncSession session)
        => session.HelloReceived
            ? $"{session.CharacterName} ({session.IpAddress})"
            : session.IpAddress;

    public void Dispose()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        _cancellation.Cancel();
        foreach (var session in _sessions.Values)
            session.Abort();

        _sessions.Clear();
        _cancellation.Dispose();
    }
}
