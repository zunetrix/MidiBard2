using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using MidiBard.RemoteControl;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlServerTests
{
    [Fact]
    public async Task AuthenticatedRequestsUseTheDefinedWireContract()
    {
        var port = GetFreePort();
        var api = new FakeApi();
        using var server = new RemoteControlServer(api, port, "test-token");
        server.Start();

        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{port}/api/v1/")
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var statusResponse = await client.GetAsync("status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var statusJson = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        statusJson.RootElement.GetProperty("latestEventSequence").GetInt64().ShouldBe(7);

        var loadResponse = await client.PostAsync(
            "playback/load",
            new StringContent(
                """{"fileName":"Exact Song.mid"}""",
                Encoding.UTF8,
                "application/json"));
        loadResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.LoadedFileName.ShouldBe("Exact Song.mid");

        using var loadJson = JsonDocument.Parse(await loadResponse.Content.ReadAsStringAsync());
        loadJson.RootElement.GetProperty("playbackId").GetGuid().ShouldBe(api.PlaybackId);
        loadJson.RootElement.GetProperty("fileName").GetString().ShouldBe("Exact Song.mid");

        var playlistResponse = await client.GetAsync("playlist");
        playlistResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var playlistJson = JsonDocument.Parse(await playlistResponse.Content.ReadAsStringAsync());
        playlistJson.RootElement.GetProperty("songs")[0]
            .GetProperty("fileName").GetString().ShouldBe("Exact Song.mid");

        var pauseResponse = await client.PostAsync(
            "playback/pause",
            new StringContent(
                "{\"playbackId\":\"" + api.PlaybackId + "\"}",
                Encoding.UTF8,
                "application/json"));
        pauseResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.PauseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task MissingBearerTokenCannotMutatePlayback()
    {
        var port = GetFreePort();
        var api = new FakeApi();
        using var server = new RemoteControlServer(api, port, "test-token");
        server.Start();

        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{port}/api/v1/")
        };

        var response = await client.PostAsync(
            "playback/play",
            new StringContent(
                $$"""{"playbackId":"{{api.PlaybackId}}"}""",
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        api.PlayCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ControllerAndDocsShellsAreAvailableWithoutBearerToken()
    {
        var port = GetFreePort();
        using var server = new RemoteControlServer(new FakeApi(), port, "test-token");
        server.Start();

        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:" + port + "/")
        };

        var controller = await client.GetAsync("");
        controller.StatusCode.ShouldBe(HttpStatusCode.OK);
        controller.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        (await controller.Content.ReadAsStringAsync()).ShouldContain("/app.js");

        var docs = await client.GetAsync("docs/");
        docs.StatusCode.ShouldBe(HttpStatusCode.OK);
        docs.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    [Fact]
    public async Task OpenApiDocumentIsAvailableWithoutBearerToken()
    {
        var port = GetFreePort();
        using var server = new RemoteControlServer(new FakeApi(), port, "test-token");
        server.Start();

        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:" + port + "/")
        };

        var response = await client.GetAsync("openapi.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement
            .GetProperty("info")
            .GetProperty("title")
            .GetString()
            .ShouldBe("MidiBard 2 Remote Control API");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FakeApi : IRemoteControlApi
    {
        public Guid PlaybackId { get; } = Guid.NewGuid();
        public string? LoadedFileName { get; private set; }
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }

        public Task<StatusResponse> GetStatusAsync() => Task.FromResult(
            new StatusResponse(
                7,
                new PlaybackStatusResponse("idle", "single", null),
                new EnsembleStatusResponse(false, false, false, true, true)));

        public Task<PlaylistResponse> GetPlaylistAsync() => Task.FromResult(
            new PlaylistResponse(new[] { new PlaylistSongResponse("Exact Song.mid") }));

        public Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request)
        {
            LoadedFileName = request.FileName;
            return Task.FromResult(
                new LoadPlaybackResponse(
                    PlaybackId,
                    request.FileName ?? string.Empty,
                    1234));
        }

        public Task PlayAsync(PlaybackHandleRequest request)
        {
            PlayCalls++;
            return Task.CompletedTask;
        }

        public Task PauseAsync(PlaybackHandleRequest request)
        {
            PauseCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync(PlaybackHandleRequest request) => Task.CompletedTask;

        public Task BeginEnsembleReadyCheckAsync(PlaybackHandleRequest request)
            => Task.CompletedTask;

        public EventPollResponse PollEvents(long afterSequence, int timeoutMs)
            => new(Array.Empty<PlaybackEventResponse>(), 7);
    }
}
