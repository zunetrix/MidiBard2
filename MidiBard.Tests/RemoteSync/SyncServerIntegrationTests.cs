using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MidiBard.RemoteControl;
using MidiBard.RemoteSync;
using MidiBard.RemoteSync.Server;

namespace MidiBard.Tests.RemoteSync;

/// <summary>
/// End-to-end tests over a real TCP socket: RemoteControlServer upgrade path
/// + SyncServer handshake, exercised with the BCL ClientWebSocket.
/// </summary>
public class SyncServerIntegrationTests
{
    private const string Token = "integration-test-token";

    public SyncServerIntegrationTests() => Infrastructure.DalamudTestSetup.Initialize();

    [Fact]
    public async Task FollowerHandshakeAndClockProbeRoundTrip()
    {
        using var syncServer = new SyncServer(() => "Conductor Char");
        var port = GetFreePort();
        using var server = new RemoteControlServer(
            new NullApi(),
            port,
            Token,
            restEnabled: false,
            wsSyncHandler: syncServer.HandleWebSocketAsync);
        server.Start();

        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Authorization", $"Bearer {Token}");
        await client.ConnectAsync(
            new Uri($"ws://127.0.0.1:{port}/ws/sync"),
            TestTimeout());

        await SendAsync(client, new HelloMessage
        {
            CharacterName = "Follower Char",
            ContentId = 77,
            PluginVersion = "test",
        });

        var welcome = (await ReceiveAsync(client)).ShouldBeOfType<WelcomeMessage>();
        welcome.ConductorName.ShouldBe("Conductor Char");
        welcome.ProgressIntervalMs.ShouldBe(SyncServer.ProgressIntervalMs);
        welcome.PingIntervalMs.ShouldBe(SyncServer.PingIntervalMs);

        syncServer.SessionCount.ShouldBe(1);
        var session = syncServer.SnapshotSessions().ShouldHaveSingleItem();
        session.CharacterName.ShouldBe("Follower Char");
        session.ContentId.ShouldBe(77);
        session.HelloReceived.ShouldBeTrue();

        var before = SyncClock.NowMs;
        await SendAsync(client, new PingMessage { T0 = before });
        var pong = (await ReceiveAsync(client)).ShouldBeOfType<PongMessage>();
        pong.T0.ShouldBe(before);
        var arrived = SyncClock.NowMs;

        // Same process = same clock, so the four timestamps must be causally ordered.
        pong.T1.ShouldBeInRange(before, arrived);
        pong.T2.ShouldBeInRange(pong.T1, arrived);

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", TestTimeout());
        await WaitUntilAsync(() => syncServer.SessionCount == 0);
    }

    [Fact]
    public async Task ProtocolMismatchIsRejectedWithErrorAndClose()
    {
        using var syncServer = new SyncServer(() => "Conductor Char");
        var port = GetFreePort();
        using var server = new RemoteControlServer(
            new NullApi(),
            port,
            Token,
            restEnabled: false,
            wsSyncHandler: syncServer.HandleWebSocketAsync);
        server.Start();

        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Authorization", $"Bearer {Token}");
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/sync"), TestTimeout());

        var rawHello = """{"v":99,"type":"hello","protocolVersion":99,"characterName":"X","contentId":1}""";
        await client.SendAsync(
            Encoding.UTF8.GetBytes(rawHello),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestTimeout());

        var error = (await ReceiveAsync(client)).ShouldBeOfType<SyncErrorMessage>();
        error.Code.ShouldBe("protocol_mismatch");
    }

    [Fact]
    public async Task InvalidTokenIsRejectedBeforeUpgrade()
    {
        using var syncServer = new SyncServer(() => "Conductor Char");
        var port = GetFreePort();
        using var server = new RemoteControlServer(
            new NullApi(),
            port,
            Token,
            restEnabled: false,
            wsSyncHandler: syncServer.HandleWebSocketAsync);
        server.Start();

        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Authorization", "Bearer wrong-token");
        await Should.ThrowAsync<WebSocketException>(
            client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/sync"), TestTimeout()));

        syncServer.SessionCount.ShouldBe(0);
    }

    [Fact]
    public async Task QueryStringTokenIsAccepted()
    {
        using var syncServer = new SyncServer(() => "Conductor Char");
        var port = GetFreePort();
        using var server = new RemoteControlServer(
            new NullApi(),
            port,
            Token,
            restEnabled: false,
            wsSyncHandler: syncServer.HandleWebSocketAsync);
        server.Start();

        using var client = new ClientWebSocket();
        await client.ConnectAsync(
            new Uri($"ws://127.0.0.1:{port}/ws/sync?token={Uri.EscapeDataString(Token)}"),
            TestTimeout());

        client.State.ShouldBe(WebSocketState.Open);
    }

    [Fact]
    public async Task SyncEndpointIsNotFoundWithoutHandler()
    {
        var port = GetFreePort();
        using var server = new RemoteControlServer(new NullApi(), port, Token);
        server.Start();

        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Authorization", $"Bearer {Token}");
        await Should.ThrowAsync<WebSocketException>(
            client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/sync"), TestTimeout()));
    }

    [Fact]
    public async Task RestIsDisabledWhenOnlySyncIsEnabled()
    {
        using var syncServer = new SyncServer(() => "Conductor Char");
        var port = GetFreePort();
        using var server = new RemoteControlServer(
            new NullApi(),
            port,
            Token,
            restEnabled: false,
            wsSyncHandler: syncServer.HandleWebSocketAsync);
        server.Start();

        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        var response = await http.GetAsync($"http://127.0.0.1:{port}/api/v1/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task SendAsync(ClientWebSocket client, SyncMessage message)
    {
        await client.SendAsync(
            SyncProtocol.Serialize(message),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestTimeout());
    }

    private static async Task<SyncMessage?> ReceiveAsync(ClientWebSocket client)
    {
        var buffer = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var result = await client.ReceiveAsync(
                buffer.AsMemory(total),
                TestTimeout());
            total += result.Count;
            if (result.EndOfMessage)
                return SyncProtocol.Deserialize(buffer.AsSpan(0, total));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition was not met in time.");

            await Task.Delay(25);
        }
    }

    private static CancellationToken TestTimeout()
        => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class NullApi : IRemoteControlApi
    {
        public Task<StatusResponse> GetStatusAsync() => throw new NotSupportedException();
        public Task<PlaylistsResponse> GetPlaylistsAsync() => throw new NotSupportedException();
        public Task<PlaylistResponse> GetPlaylistAsync(int? playlistId) => throw new NotSupportedException();
        public Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request) => throw new NotSupportedException();
        public Task<LoadPlaybackResponse> LoadPlaylistSongAsync(LoadPlaylistSongRequest request) => throw new NotSupportedException();
        public Task PlayAsync(PlaybackHandleRequest request) => throw new NotSupportedException();
        public Task PauseAsync(PlaybackHandleRequest request) => throw new NotSupportedException();
        public Task StopAsync(PlaybackHandleRequest request) => throw new NotSupportedException();
        public Task PreviousAsync(PlaybackHandleRequest request) => throw new NotSupportedException();
        public Task NextAsync(PlaybackHandleRequest request) => throw new NotSupportedException();
        public Task SeekAsync(SeekPlaybackRequest request) => throw new NotSupportedException();
        public Task SetPlayModeAsync(SetPlayModeRequest request) => throw new NotSupportedException();
        public Task<EnsembleVisualizationResponse> GetEnsembleVisualizationAsync() => throw new NotSupportedException();
        public Task BeginEnsembleReadyCheckAsync(PlaybackHandleRequest request) => throw new NotSupportedException();
        public EventPollResponse PollEvents(long afterSequence, int timeoutMs) => throw new NotSupportedException();
    }
}
