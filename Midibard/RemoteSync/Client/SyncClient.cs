using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MidiBard.RemoteSync.Client;

internal enum SyncClientState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Follower-side WebSocket client. Connects to the conductor's <c>/ws/sync</c>
/// endpoint, identifies itself, keeps the clock probes flowing, and reconnects
/// with backoff. Network failures never touch local playback: every pump
/// swallows its own errors and simply retries.
/// </summary>
internal sealed class SyncClient : IDisposable
{
    private const int ReceiveBufferBytes = 16 * 1024;
    private const int MaxTextMessageBytes = 512 * 1024;
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private const int StatusReportIntervalMs = 2000;
    private const int PingBurstCount = 5;
    private const int PingBurstSpacingMs = 500;

    private readonly string _address;
    private readonly string _token;
    private readonly Func<Task<(string CharacterName, long ContentId)>> _identityProvider;
    private readonly Func<SyncMessage, Task> _onMessage;
    private readonly Func<StatusReportMessage>? _statusFactory;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _socketGate = new();

    private ClientWebSocket? _socket;
    private Channel<OutgoingFrame>? _sendQueue;
    private bool _disposed;

    public SyncClientState State { get; private set; } = SyncClientState.Disconnected;
    public string StatusMessage { get; private set; } = "Stopped";
    public string ConductorName { get; private set; } = string.Empty;
    public int PingIntervalMs { get; private set; } = 2_000;
    public int ProgressIntervalMs { get; private set; } = 1000;

    /// <summary>NTP-style offset estimator fed by ping/pong (offset = conductor − local).</summary>
    public ClockSyncEstimator Clock { get; } = new();

    public long LastRttMs => Clock.RttMs;
    public long LastOffsetMs => Clock.OffsetMs;

    public SyncClient(
        string address,
        string token,
        Func<Task<(string CharacterName, long ContentId)>> identityProvider,
        Func<SyncMessage, Task> onMessage,
        Func<StatusReportMessage>? statusFactory = null)
    {
        _address = address;
        _token = token;
        _identityProvider = identityProvider;
        _onMessage = onMessage;
        _statusFactory = statusFactory;
    }

    public void Start()
    {
        _ = Task.Run(() => ConnectLoopAsync(_cancellation.Token));
    }

    /// <summary>Queues a message to the conductor; false when not connected.</summary>
    public bool TrySend(SyncMessage message)
        => _sendQueue?.Writer.TryWrite(new OutgoingFrame(SyncProtocol.Serialize(message))) == true;

    /// <summary>
    /// Send-queue item. A null <see cref="Payload"/> is a clock ping: it is serialized
    /// by the send pump with <see cref="PingMessage.T0"/> stamped right before the
    /// socket write, so time spent queued behind other frames never biases the offset.
    /// </summary>
    private readonly record struct OutgoingFrame(byte[]? Payload)
    {
        public static OutgoingFrame Ping => new(null);
    }

    private async Task ConnectLoopAsync(CancellationToken cancellationToken)
    {
        var reconnectDelay = InitialReconnectDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            var socket = new ClientWebSocket();
            lock (_socketGate)
            {
                if (_disposed)
                {
                    socket.Dispose();
                    return;
                }

                _socket?.Dispose();
                _socket = socket;
            }

            if (!string.IsNullOrEmpty(_token))
                socket.Options.SetRequestHeader("Authorization", $"Bearer {_token}");

            State = SyncClientState.Connecting;
            StatusMessage = "Connecting...";

            try
            {
                await socket.ConnectAsync(new Uri(_address), cancellationToken);

                State = SyncClientState.Connected;
                StatusMessage = "Connected";
                reconnectDelay = InitialReconnectDelay;
                DalamudApi.PluginLog.Information($"[RemoteSync] Connected to conductor at {_address}.");

                var sendQueue = Channel.CreateBounded<OutgoingFrame>(
                    new BoundedChannelOptions(256)
                    {
                        SingleReader = true,
                        FullMode = BoundedChannelFullMode.DropOldest,
                    });
                _sendQueue = sendQueue;

                var identity = await _identityProvider();
                sendQueue.Writer.TryWrite(new OutgoingFrame(SyncProtocol.Serialize(new HelloMessage
                {
                    CharacterName = identity.CharacterName,
                    ContentId = identity.ContentId,
                    PluginVersion = typeof(SyncClient).Assembly.GetName().Version?.ToString() ?? string.Empty,
                })));

                Clock.Reset();

                using var sessionCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var sendPump = SendPumpAsync(socket, sendQueue, sessionCancellation.Token);
                var pingPump = PingPumpAsync(sendQueue, sessionCancellation.Token);
                var statusPump = StatusPumpAsync(sendQueue, sessionCancellation.Token);

                await ReceivePumpAsync(socket, sessionCancellation.Token);

                sessionCancellation.Cancel();
                sendQueue.Writer.TryComplete();
                await Task.WhenAll(sendPump, pingPump, statusPump);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                StatusMessage = $"Error: {exception.Message}";
                DalamudApi.PluginLog.Warning(exception, "[RemoteSync] Connection to conductor failed.");
            }
            finally
            {
                _sendQueue = null;
                State = SyncClientState.Disconnected;
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            StatusMessage = $"Reconnecting in {reconnectDelay.TotalSeconds:0}s...";
            try
            {
                await Task.Delay(reconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            reconnectDelay = TimeSpan.FromSeconds(
                Math.Min(reconnectDelay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
        }

        State = SyncClientState.Disconnected;
        StatusMessage = "Stopped";
    }

    private static async Task SendPumpAsync(
        ClientWebSocket socket,
        Channel<OutgoingFrame> sendQueue,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in sendQueue.Reader.ReadAllAsync(cancellationToken))
            {
                if (socket.State != WebSocketState.Open)
                    return;

                var payload = frame.Payload
                    ?? SyncProtocol.Serialize(new PingMessage { T0 = SyncClock.NowMs });
                await socket.SendAsync(
                    payload,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task PingPumpAsync(Channel<OutgoingFrame> sendQueue, CancellationToken cancellationToken)
    {
        try
        {
            // Initial burst so the offset estimate is usable within seconds.
            for (var i = 0; i < PingBurstCount && !cancellationToken.IsCancellationRequested; i++)
            {
                sendQueue.Writer.TryWrite(OutgoingFrame.Ping);
                await Task.Delay(PingBurstSpacingMs, cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PingIntervalMs, cancellationToken);
                sendQueue.Writer.TryWrite(OutgoingFrame.Ping);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StatusPumpAsync(Channel<OutgoingFrame> sendQueue, CancellationToken cancellationToken)
    {
        if (_statusFactory == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(StatusReportIntervalMs, cancellationToken);

                try
                {
                    sendQueue.Writer.TryWrite(new OutgoingFrame(SyncProtocol.Serialize(_statusFactory())));
                }
                catch (Exception exception)
                {
                    DalamudApi.PluginLog.Warning(exception, "[RemoteSync] Failed to build status report.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReceivePumpAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferBytes];
        using var messageBuffer = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch
                {
                    // The conductor may already be gone.
                }

                return;
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (messageBuffer.Length > MaxTextMessageBytes)
            {
                messageBuffer.SetLength(0);
                continue;
            }

            if (!result.EndOfMessage)
                continue;

            // Arrival time for clock samples, taken before deserialization/handling.
            var receivedAtMs = SyncClock.NowMs;
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = SyncProtocol.Deserialize(
                    messageBuffer.GetBuffer().AsSpan(0, (int)messageBuffer.Length));
                if (message != null)
                    await HandleMessageAsync(message, receivedAtMs);
            }

            messageBuffer.SetLength(0);
        }
    }

    private async Task HandleMessageAsync(SyncMessage message, long receivedAtMs)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                ConductorName = welcome.ConductorName;
                PingIntervalMs = Math.Max(1000, welcome.PingIntervalMs);
                ProgressIntervalMs = Math.Max(250, welcome.ProgressIntervalMs);
                StatusMessage = $"Connected to {welcome.ConductorName}";
                DalamudApi.PluginLog.Information(
                    $"[RemoteSync] Welcomed by conductor '{welcome.ConductorName}'.");
                break;

            case PongMessage pong when pong.Cid == 0:
                Clock.AddSample(pong.T0, pong.T1, pong.T2, receivedAtMs);
                break;

            case SyncErrorMessage error:
                StatusMessage = $"Conductor error: {error.Message}";
                DalamudApi.PluginLog.Warning(
                    $"[RemoteSync] Conductor reported '{error.Code}': {error.Message}");
                break;
        }

        await _onMessage(message);
    }

    public void Dispose()
    {
        lock (_socketGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Cancel();
            try
            {
                _socket?.Abort();
            }
            catch
            {
                // Already closed.
            }

            _socket?.Dispose();
            _socket = null;
        }

        _cancellation.Dispose();
        State = SyncClientState.Disconnected;
        StatusMessage = "Stopped";
    }
}
