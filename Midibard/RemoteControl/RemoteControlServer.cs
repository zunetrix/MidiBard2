using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MidiBard.RemoteControl;

internal sealed class RemoteControlServer : IDisposable
{
    private readonly IRemoteControlApi _api;
    private readonly string _token;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _acceptLoop;

    public bool IsListening => _listener.IsListening;

    public RemoteControlServer(
        IRemoteControlApi api,
        int port,
        string token)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Remote-control token is required.", nameof(token));

        _api = api;
        _token = token;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.RemoteEndPoint == null ||
                !IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
            {
                await WriteErrorAsync(context.Response, 401, "unauthorized", "Remote control is loopback-only.");
                return;
            }

            if (!IsAuthorized(context.Request.Headers["Authorization"]))
            {
                await WriteErrorAsync(context.Response, 401, "unauthorized", "Invalid remote-control token.");
                return;
            }

            var method = context.Request.HttpMethod;
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

            if (method == "GET" && path == "/api/v1/status")
            {
                await WriteJsonAsync(context.Response, 200, await _api.GetStatusAsync());
                return;
            }

            if (method == "GET" && path == "/api/v1/events")
            {
                var after = ParseLongQuery(context.Request, "after", 0);
                var timeoutMs = ParseIntQuery(context.Request, "timeoutMs", 0);
                await WriteJsonAsync(context.Response, 200, _api.PollEvents(after, timeoutMs));
                return;
            }

            if (method == "POST" && path == "/api/v1/playback/load")
            {
                var request = await ReadJsonAsync<LoadPlaybackRequest>(context.Request);
                await WriteJsonAsync(context.Response, 200, await _api.LoadPlaybackAsync(request));
                return;
            }

            if (method == "POST" && path == "/api/v1/playback/play")
            {
                await _api.PlayAsync(await ReadJsonAsync<PlaybackHandleRequest>(context.Request));
                WriteNoContent(context.Response);
                return;
            }

            if (method == "POST" && path == "/api/v1/playback/stop")
            {
                await _api.StopAsync(await ReadJsonAsync<PlaybackHandleRequest>(context.Request));
                WriteNoContent(context.Response);
                return;
            }

            if (method == "POST" && path == "/api/v1/ensemble/ready-check")
            {
                await _api.BeginEnsembleReadyCheckAsync(
                    await ReadJsonAsync<PlaybackHandleRequest>(context.Request));
                WriteNoContent(context.Response);
                return;
            }

            await WriteErrorAsync(context.Response, 404, "invalid_request", "Unknown remote-control endpoint.");
        }
        catch (RemoteControlException exception)
        {
            await WriteErrorAsync(
                context.Response,
                exception.StatusCode,
                exception.Code,
                exception.Message);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, 400, "invalid_request", "Request body is not valid JSON.");
        }
        catch (Exception exception)
        {
            DalamudApi.PluginLog.Error(exception, "Unexpected remote-control request failure.");
            await WriteErrorAsync(context.Response, 500, "internal_error", "Remote-control request failed.");
        }
    }

    private bool IsAuthorized(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization == null ||
            !authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var supplied = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(_token);
        return supplied.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
            throw new RemoteControlException(400, "invalid_request", "Request body is required.");

        var value = await JsonSerializer.DeserializeAsync<T>(
            request.InputStream,
            RemoteControlJson.Options);

        return value ?? throw new RemoteControlException(
            400,
            "invalid_request",
            "Request body is required.");
    }

    private static long ParseLongQuery(HttpListenerRequest request, string name, long defaultValue)
    {
        var value = request.QueryString[name];
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        if (!long.TryParse(value, out var parsed))
            throw new RemoteControlException(400, "invalid_request", $"{name} must be an integer.");

        return parsed;
    }

    private static int ParseIntQuery(HttpListenerRequest request, string name, int defaultValue)
    {
        var value = request.QueryString[name];
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        if (!int.TryParse(value, out var parsed))
            throw new RemoteControlException(400, "invalid_request", $"{name} must be an integer.");

        return parsed;
    }

    private static async Task WriteJsonAsync<T>(
        HttpListenerResponse response,
        int statusCode,
        T value)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            response.OutputStream,
            value,
            RemoteControlJson.Options);
        response.Close();
    }

    private static Task WriteErrorAsync(
        HttpListenerResponse response,
        int statusCode,
        string code,
        string message)
    {
        return WriteJsonAsync(
            response,
            statusCode,
            new ErrorResponse(code, message));
    }

    private static void WriteNoContent(HttpListenerResponse response)
    {
        response.StatusCode = 204;
        response.Close();
    }

    public void Dispose()
    {
        _cancellation.Cancel();

        if (_listener.IsListening)
            _listener.Stop();

        _listener.Close();
        _cancellation.Dispose();
    }
}
