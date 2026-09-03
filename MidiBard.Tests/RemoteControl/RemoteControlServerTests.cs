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

        var playlistsResponse = await client.GetAsync("playlists");
        playlistsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var playlistsJson = JsonDocument.Parse(await playlistsResponse.Content.ReadAsStringAsync());
        playlistsJson.RootElement.GetProperty("playlists")[0]
            .GetProperty("name").GetString().ShouldBe("Recording");

        var playlistResponse = await client.GetAsync("playlist?playlistId=4");
        playlistResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.RequestedPlaylistId.ShouldBe(4);
        using var playlistJson = JsonDocument.Parse(await playlistResponse.Content.ReadAsStringAsync());
        playlistJson.RootElement.GetProperty("songs")[0]
            .GetProperty("fileName").GetString().ShouldBe("Exact Song.mid");

        var visualizationResponse =
            await client.GetAsync("ensemble/visualization");
        visualizationResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var visualizationJson = JsonDocument.Parse(
            await visualizationResponse.Content.ReadAsStringAsync());
        visualizationJson.RootElement.GetProperty("instruments")[0]
            .GetProperty("instrumentName").GetString().ShouldBe("Flute");

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

        var loadSongResponse = await client.PostAsync(
            "playback/load-song",
            new StringContent(
                """{"playlistId":4,"songId":42}""",
                Encoding.UTF8,
                "application/json"));
        loadSongResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.LoadedPlaylistId.ShouldBe(4);
        api.LoadedSongId.ShouldBe(42);

        var pauseResponse = await client.PostAsync(
            "playback/pause",
            new StringContent(
                "{\"playbackId\":\"" + api.PlaybackId + "\"}",
                Encoding.UTF8,
                "application/json"));
        pauseResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.PauseCalls.ShouldBe(1);

        var previousResponse = await client.PostAsync(
            "playback/previous",
            new StringContent(
                "{\"playbackId\":\"" + api.PlaybackId + "\"}",
                Encoding.UTF8,
                "application/json"));
        previousResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.PreviousCalls.ShouldBe(1);

        var nextResponse = await client.PostAsync(
            "playback/next",
            new StringContent(
                "{\"playbackId\":\"" + api.PlaybackId + "\"}",
                Encoding.UTF8,
                "application/json"));
        nextResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.NextCalls.ShouldBe(1);

        var seekResponse = await client.PostAsync(
            "playback/seek",
            new StringContent(
                "{\"playbackId\":\"" + api.PlaybackId + "\",\"positionMs\":4321}",
                Encoding.UTF8,
                "application/json"));
        seekResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.SeekPositionMs.ShouldBe(4321);

        var playModeResponse = await client.PostAsync(
            "playback/play-mode",
            new StringContent(
                """{"playMode":"list_repeat"}""",
                Encoding.UTF8,
                "application/json"));
        playModeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.PlayMode.ShouldBe("list_repeat");

        var autoAdvanceResponse = await client.PostAsync(
            "ensemble/auto-advance",
            new StringContent(
                """{"enabled":true}""",
                Encoding.UTF8,
                "application/json"));
        autoAdvanceResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        api.EnsembleAutoAdvance.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidPlaylistQueryIsRejectedByEndpointParser()
    {
        var port = GetFreePort();
        using var server = new RemoteControlServer(new FakeApi(), port, "test-token");
        server.Start();

        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{port}/api/v1/")
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await client.GetAsync("playlist?playlistId=not-an-int");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
        public int? RequestedPlaylistId { get; private set; }
        public int? LoadedPlaylistId { get; private set; }
        public int? LoadedSongId { get; private set; }
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int PreviousCalls { get; private set; }
        public int NextCalls { get; private set; }
        public long? SeekPositionMs { get; private set; }
        public string? PlayMode { get; private set; }
        public bool? EnsembleAutoAdvance { get; private set; }

        public Task<StatusResponse> GetStatusAsync() => Task.FromResult(
            new StatusResponse(
                7,
                new PlaybackStatusResponse("idle", "single", null),
                new EnsembleStatusResponse(
                    false,
                    false,
                    false,
                    true,
                    true,
                    false),
                new PlayerStatusResponse(true, 23, "BRD", true),
                new PlaybackControlsResponse(
                    true,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false),
                new CurrentPlaylistResponse(4, "Recording", false)));

        public Task<PlaylistsResponse> GetPlaylistsAsync() => Task.FromResult(
            new PlaylistsResponse(
                new[] { new PlaylistSummaryResponse(4, "Recording", 1, 1234, true) }));

        public Task<PlaylistResponse> GetPlaylistAsync(int? playlistId)
        {
            RequestedPlaylistId = playlistId;
            return Task.FromResult(
                new PlaylistResponse(
                    4,
                    "Recording",
                    true,
                    false,
                    1,
                    1234,
                    new[]
                    {
                        new PlaylistSongResponse(
                            42,
                            1,
                            "Exact Song.mid",
                            "Exact Song",
                            "Composer",
                            2026,
                            1234,
                            0,
                            null,
                            false,
                            0,
                            Array.Empty<string>(),
                            string.Empty,
                            true,
                            "2026-09-01T00:00:00.0000000Z",
                            "2026-09-01T00:00:00.0000000Z"),
                    }));
        }

        public Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request)
        {
            LoadedFileName = request.FileName;
            return Task.FromResult(
                new LoadPlaybackResponse(
                    PlaybackId,
                    request.FileName ?? string.Empty,
                    1234));
        }

        public Task<LoadPlaybackResponse> LoadPlaylistSongAsync(LoadPlaylistSongRequest request)
        {
            LoadedPlaylistId = request.PlaylistId;
            LoadedSongId = request.SongId;
            return Task.FromResult(
                new LoadPlaybackResponse(PlaybackId, "Exact Song.mid", 1234));
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

        public Task PreviousAsync(PlaybackHandleRequest request)
        {
            PreviousCalls++;
            return Task.CompletedTask;
        }

        public Task NextAsync(PlaybackHandleRequest request)
        {
            NextCalls++;
            return Task.CompletedTask;
        }

        public Task SeekAsync(SeekPlaybackRequest request)
        {
            SeekPositionMs = request.PositionMs;
            return Task.CompletedTask;
        }

        public Task SetPlayModeAsync(SetPlayModeRequest request)
        {
            PlayMode = request.PlayMode;
            return Task.CompletedTask;
        }

        public Task SetEnsembleAutoAdvanceAsync(
            SetEnsembleAutoAdvanceRequest request)
        {
            EnsembleAutoAdvance = request.Enabled;
            return Task.CompletedTask;
        }

        public Task<EnsembleVisualizationResponse>
            GetEnsembleVisualizationAsync()
            => Task.FromResult(
                new EnsembleVisualizationResponse(
                    PlaybackId,
                    100,
                    new[]
                    {
                        new EnsembleInstrumentActivityResponse(
                            5,
                            "Flute",
                            "Bard One·Gilgamesh",
                            new[]
                            {
                                new EnsembleActivityBucketResponse(
                                    100,
                                    2),
                            }),
                    }));

        public Task BeginEnsembleReadyCheckAsync(PlaybackHandleRequest request)
            => Task.CompletedTask;

        public EventPollResponse PollEvents(long afterSequence, int timeoutMs)
            => new(Array.Empty<PlaybackEventResponse>(), 7);
    }
}
