using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MidiBard.RemoteSync.Server;

/// <summary>
/// One connected follower (or gateway) on the conductor side. Owns the receive
/// pump (fragments are accumulated until <c>EndOfMessage</c>) and a single-writer
/// send queue, because <see cref="WebSocket.SendAsync(ArraySegment{byte}, WebSocketMessageType, bool, CancellationToken)"/>
/// must never run concurrently on one socket.
/// </summary>
internal sealed class SyncSession
{
    private const int ReceiveBufferBytes = 16 * 1024;
    private const int MaxTextMessageBytes = 512 * 1024;

    private readonly WebSocket _socket;
    private readonly Channel<SendItem> _sendQueue = Channel.CreateBounded<SendItem>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public Guid Id { get; } = Guid.NewGuid();
    public string IpAddress { get; }
    public DateTime ConnectedAt { get; } = DateTime.Now;

    public string CharacterName { get; private set; } = string.Empty;
    public long ContentId { get; private set; }
    public string PluginVersion { get; private set; } = string.Empty;
    public bool HelloReceived { get; private set; }

    /// <summary>Last moment any frame arrived; used by the idle reaper.</summary>
    public DateTime LastActivity { get; private set; } = DateTime.Now;

    /// <summary>Most recent diagnostic report from this follower, for the conductor UI.</summary>
    public StatusReportMessage? LastReport { get; set; }

    /// <summary>
    /// <see cref="SyncClock"/> instant at which the frame currently being handled was
    /// fully received. Read by the message handler (same receive-pump thread) to
    /// stamp <see cref="PongMessage.T1"/> independent of deserialization/handling time.
    /// </summary>
    public long LastFrameReceivedAtMs { get; private set; }

    /// <summary>
    /// Send-queue item. A non-null <see cref="Pong"/> is serialized by the send pump
    /// with <see cref="PongMessage.T2"/> stamped right before the socket write.
    /// </summary>
    private readonly record struct SendItem(byte[]? Payload, bool Binary, PongMessage? Pong = null);

    public SyncSession(WebSocket socket, string ipAddress)
    {
        _socket = socket;
        IpAddress = ipAddress;
    }

    public void ApplyHello(HelloMessage hello)
    {
        CharacterName = hello.CharacterName;
        ContentId = hello.ContentId;
        PluginVersion = hello.PluginVersion;
        HelloReceived = true;
    }

    /// <summary>Queues a protocol message; drops the oldest queued frame if the client cannot keep up.</summary>
    public bool TrySend(SyncMessage message)
        => _sendQueue.Writer.TryWrite(new SendItem(SyncProtocol.Serialize(message), Binary: false));

    public bool TrySendBinary(byte[] payload)
        => _sendQueue.Writer.TryWrite(new SendItem(payload, Binary: true));

    /// <summary>Queues a clock pong; its send time <see cref="PongMessage.T2"/> is stamped at write time.</summary>
    public bool TrySendPong(PongMessage pong)
        => _sendQueue.Writer.TryWrite(new SendItem(null, Binary: false, pong));

    private volatile string? _pendingCloseDescription;
    private WebSocketCloseStatus _pendingCloseStatus;

    /// <summary>
    /// Requests an orderly close. Queued frames (such as a final error message) are
    /// flushed first: the close frame is written by the send pump after the queue
    /// drains, never concurrently with another send.
    /// </summary>
    public void RequestClose(WebSocketCloseStatus status, string description)
    {
        _pendingCloseStatus = status;
        _pendingCloseDescription = description;
        _sendQueue.Writer.TryComplete();
    }

    public bool CloseRequested => _pendingCloseDescription != null;

    /// <summary>Hard-kills the connection; used when an orderly close got no acknowledgement.</summary>
    public void Abort()
    {
        try
        {
            _socket.Abort();
        }
        catch
        {
            // Already gone.
        }
    }

    /// <summary>
    /// Runs the send and receive pumps until the connection ends. Messages are
    /// surfaced through <paramref name="onMessage"/> (invoked on a thread-pool
    /// thread; the handler marshals to the framework thread where needed).
    /// </summary>
    public async Task RunAsync(
        Func<SyncSession, SyncMessage, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var sendPump = SendPumpAsync(cancellationToken);
        try
        {
            await ReceivePumpAsync(onMessage, cancellationToken);
        }
        finally
        {
            _sendQueue.Writer.TryComplete();
            await sendPump;
        }
    }

    private async Task SendPumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _sendQueue.Reader.ReadAllAsync(cancellationToken))
            {
                if (_socket.State != WebSocketState.Open)
                    return;

                var payload = item.Pong is { } pong
                    ? SyncProtocol.Serialize(pong with { T2 = SyncClock.NowMs })
                    : item.Payload!;
                await _socket.SendAsync(
                    payload,
                    item.Binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }

            // Queue completed: flush a requested close after the last frame.
            // CloseOutputAsync only sends the close frame, so it cannot collide
            // with the receive pump awaiting the peer's acknowledgement.
            if (_pendingCloseDescription is { } description &&
                _socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseOutputAsync(_pendingCloseStatus, description, cancellationToken);
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

    private async Task ReceivePumpAsync(
        Func<SyncSession, SyncMessage, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferBytes];
        using var messageBuffer = new MemoryStream();

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
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

            LastActivity = DateTime.Now;

            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Peer-initiated close: the send pump acknowledges after draining,
                // so the close frame never races another SendAsync.
                RequestClose(WebSocketCloseStatus.NormalClosure, "bye");
                return;
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (messageBuffer.Length > MaxTextMessageBytes)
            {
                RequestClose(WebSocketCloseStatus.MessageTooBig, "message too big");
                return;
            }

            if (!result.EndOfMessage)
                continue;

            LastFrameReceivedAtMs = SyncClock.NowMs;
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = SyncProtocol.Deserialize(
                    messageBuffer.GetBuffer().AsSpan(0, (int)messageBuffer.Length));
                if (message != null)
                    await onMessage(this, message);
            }

            // Binary frames are file-transfer chunks; ignored until that phase lands.
            messageBuffer.SetLength(0);
        }
    }
}
