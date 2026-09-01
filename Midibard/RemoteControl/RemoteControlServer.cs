using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MidiBard.RemoteControl;

internal sealed class RemoteControlServer : IDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 64 * 1024;

    private readonly IRemoteControlApi _api;
    private readonly string _token;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _acceptLoop;

    public bool IsListening { get; private set; }

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
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        IsListening = true;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();

            try
            {
                var request = await ReadRequestAsync(stream, _cancellation.Token);

                if (!IsAuthorized(request.Headers.GetValueOrDefault("Authorization")))
                {
                    await WriteErrorAsync(
                        stream,
                        401,
                        "unauthorized",
                        "Invalid remote-control token.");
                    return;
                }

                await RouteAsync(stream, request);
            }
            catch (RemoteControlException exception)
            {
                await WriteErrorAsync(
                    stream,
                    exception.StatusCode,
                    exception.Code,
                    exception.Message);
            }
            catch (JsonException)
            {
                await WriteErrorAsync(
                    stream,
                    400,
                    "invalid_request",
                    "Request body is not valid JSON.");
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                // Server shutdown; connection is being discarded.
            }
            catch (Exception exception)
            {
                DalamudApi.PluginLog.Error(
                    exception,
                    "Unexpected remote-control request failure.");

                try
                {
                    await WriteErrorAsync(
                        stream,
                        500,
                        "internal_error",
                        "Remote-control request failed.");
                }
                catch
                {
                    // The client may already have disconnected.
                }
            }
        }
    }

    private async Task RouteAsync(NetworkStream stream, HttpRequest request)
    {
        var path = RequestPath(request.Target);

        if (request.Method == "GET" && path == "/api/v1/status")
        {
            await WriteJsonAsync(stream, 200, await _api.GetStatusAsync());
            return;
        }

        if (request.Method == "GET" && path == "/api/v1/events")
        {
            var after = ParseLongQuery(request.Target, "after", 0);
            var timeoutMs = ParseIntQuery(request.Target, "timeoutMs", 0);
            await WriteJsonAsync(
                stream,
                200,
                _api.PollEvents(after, timeoutMs));
            return;
        }

        if (request.Method == "POST" && path == "/api/v1/playback/load")
        {
            var payload = ReadJson<LoadPlaybackRequest>(request.Body);
            await WriteJsonAsync(
                stream,
                200,
                await _api.LoadPlaybackAsync(payload));
            return;
        }

        if (request.Method == "POST" && path == "/api/v1/playback/play")
        {
            await _api.PlayAsync(ReadJson<PlaybackHandleRequest>(request.Body));
            await WriteNoContentAsync(stream);
            return;
        }

        if (request.Method == "POST" && path == "/api/v1/playback/stop")
        {
            await _api.StopAsync(ReadJson<PlaybackHandleRequest>(request.Body));
            await WriteNoContentAsync(stream);
            return;
        }

        if (request.Method == "POST" && path == "/api/v1/ensemble/ready-check")
        {
            await _api.BeginEnsembleReadyCheckAsync(
                ReadJson<PlaybackHandleRequest>(request.Body));
            await WriteNoContentAsync(stream);
            return;
        }

        await WriteErrorAsync(
            stream,
            404,
            "invalid_request",
            "Unknown remote-control endpoint.");
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

    private static T ReadJson<T>(byte[] body)
    {
        if (body.Length == 0)
        {
            throw new RemoteControlException(
                400,
                "invalid_request",
                "Request body is required.");
        }

        return JsonSerializer.Deserialize<T>(
                body,
                RemoteControlJson.Options)
            ?? throw new RemoteControlException(
                400,
                "invalid_request",
                "Request body is required.");
    }

    private static async Task<HttpRequest> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(512);

        while (true)
        {
            if (headerBytes.Count >= MaxHeaderBytes)
            {
                throw new RemoteControlException(
                    400,
                    "invalid_request",
                    "Request headers are too large.");
            }

            var buffer = new byte[1];
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                throw new RemoteControlException(
                    400,
                    "invalid_request",
                    "Request ended before headers were complete.");
            }

            headerBytes.Add(buffer[0]);
            var count = headerBytes.Count;
            if (count >= 4 &&
                headerBytes[count - 4] == '\r' &&
                headerBytes[count - 3] == '\n' &&
                headerBytes[count - 2] == '\r' &&
                headerBytes[count - 1] == '\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText.Split(
            new[] { "\r\n" },
            StringSplitOptions.None);

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 ||
            !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            throw new RemoteControlException(
                400,
                "invalid_request",
                "Invalid HTTP request line.");
        }

        var headers = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines[1..])
        {
            if (line.Length == 0)
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new RemoteControlException(
                    400,
                    "invalid_request",
                    "Invalid HTTP request header.");
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var lengthValue) &&
            (!int.TryParse(lengthValue, out contentLength) ||
             contentLength < 0 ||
             contentLength > MaxBodyBytes))
        {
            throw new RemoteControlException(
                400,
                "invalid_request",
                "Invalid request body length.");
        }

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < body.Length)
        {
            var read = await stream.ReadAsync(
                body.AsMemory(offset),
                cancellationToken);
            if (read == 0)
            {
                throw new RemoteControlException(
                    400,
                    "invalid_request",
                    "Request body ended early.");
            }

            offset += read;
        }

        return new HttpRequest(
            requestLine[0].ToUpperInvariant(),
            requestLine[1],
            headers,
            body);
    }

    private static string RequestPath(string target)
    {
        var queryIndex = target.IndexOf('?');
        return queryIndex < 0 ? target : target[..queryIndex];
    }

    private static string? QueryValue(string target, string name)
    {
        var queryIndex = target.IndexOf('?');
        if (queryIndex < 0 || queryIndex == target.Length - 1)
            return null;

        foreach (var pair in target[(queryIndex + 1)..].Split('&'))
        {
            if (pair.Length == 0)
                continue;

            var separator = pair.IndexOf('=');
            var rawName = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(
                    Uri.UnescapeDataString(rawName.Replace("+", " ")),
                    name,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var rawValue = separator < 0 ? string.Empty : pair[(separator + 1)..];
            return Uri.UnescapeDataString(rawValue.Replace("+", " "));
        }

        return null;
    }

    private static long ParseLongQuery(
        string target,
        string name,
        long defaultValue)
    {
        var value = QueryValue(target, name);
        if (value == null)
            return defaultValue;

        if (!long.TryParse(value, out var parsed))
        {
            throw new RemoteControlException(
                400,
                "invalid_request",
                $"{name} must be an integer.");
        }

        return parsed;
    }

    private static int ParseIntQuery(
        string target,
        string name,
        int defaultValue)
    {
        var value = QueryValue(target, name);
        if (value == null)
            return defaultValue;

        if (!int.TryParse(value, out var parsed))
        {
            throw new RemoteControlException(
                400,
                "invalid_request",
                $"{name} must be an integer.");
        }

        return parsed;
    }

    private static Task WriteErrorAsync(
        NetworkStream stream,
        int statusCode,
        string code,
        string message)
    {
        return WriteJsonAsync(
            stream,
            statusCode,
            new ErrorResponse(code, message));
    }

    private static Task WriteNoContentAsync(NetworkStream stream)
    {
        return WriteResponseAsync(
            stream,
            204,
            "application/json; charset=utf-8",
            Array.Empty<byte>());
    }

    private static Task WriteJsonAsync<T>(
        NetworkStream stream,
        int statusCode,
        T value)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            value,
            RemoteControlJson.Options);

        return WriteResponseAsync(
            stream,
            statusCode,
            "application/json; charset=utf-8",
            body);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string contentType,
        byte[] body)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            409 => "Conflict",
            410 => "Gone",
            500 => "Internal Server Error",
            _ => "Error",
        };

        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            "Connection: close\r\n" +
            "\r\n");

        await stream.WriteAsync(header);
        if (body.Length > 0)
            await stream.WriteAsync(body);

        await stream.FlushAsync();
    }

    public void Dispose()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        IsListening = false;
        _cancellation.Cancel();
        _listener.Stop();
        _cancellation.Dispose();
    }

    private sealed record HttpRequest(
        string Method,
        string Target,
        Dictionary<string, string> Headers,
        byte[] Body);
}
