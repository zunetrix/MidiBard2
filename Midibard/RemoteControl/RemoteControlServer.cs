using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly byte[] OpenApiDocument =
        OpenApiSpecGenerator.Generate(RemoteControlApiContract.Endpoints);

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
                var path = RequestPath(request.Target);

                if (request.Method == "GET" && path == "/openapi.json")
                {
                    await WriteResponseAsync(
                        stream,
                        200,
                        "application/json; charset=utf-8",
                        OpenApiDocument);
                    return;
                }

                if (request.Method == "GET" &&
                    RemoteControlWebAssets.TryGet(path, out var webAsset))
                {
                    await WriteResponseAsync(
                        stream,
                        200,
                        webAsset.ContentType,
                        webAsset.Content);
                    return;
                }

                if (request.Method == "GET" &&
                    TryParseInstrumentIconPath(path, out var iconId) &&
                    _api is IRemoteControlWebAssetProvider assetProvider)
                {
                    var instrumentIcon =
                        await assetProvider.GetInstrumentIconAsync(iconId);
                    if (instrumentIcon == null)
                    {
                        await WriteErrorAsync(
                            stream,
                            404,
                            "invalid_request",
                            "Instrument icon was not found.");
                        return;
                    }

                    await WriteResponseAsync(
                        stream,
                        200,
                        instrumentIcon.ContentType,
                        instrumentIcon.Content);
                    return;
                }

                if (!path.StartsWith("/api/v1/", StringComparison.Ordinal))
                {
                    await WriteErrorAsync(
                        stream,
                        404,
                        "invalid_request",
                        "Unknown remote-control endpoint.");
                    return;
                }

                if (!IsAuthorized(request.Headers.GetValueOrDefault("Authorization")))
                {
                    await WriteErrorAsync(
                        stream,
                        401,
                        "unauthorized",
                        "Invalid remote-control token.");
                    return;
                }

                await RouteApiAsync(stream, request, path);
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

    private async Task RouteApiAsync(
        NetworkStream stream,
        HttpRequest request,
        string path)
    {
        var endpoint = RemoteControlApiContract.Endpoints.FirstOrDefault(candidate =>
            candidate.Method == request.Method && candidate.Path == path);

        if (endpoint == null)
        {
            await WriteErrorAsync(
                stream,
                404,
                "invalid_request",
                "Unknown remote-control endpoint.");
            return;
        }

        var result = await endpoint.ExecuteAsync(
            _api,
            new RemoteControlRequestContext(request.Target, request.Body));

        if (result.StatusCode == 204 || result.Body == null)
        {
            await WriteNoContentAsync(stream);
            return;
        }

        await WriteJsonAsync(stream, result.StatusCode, result.Body);
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

    private static bool TryParseInstrumentIconPath(
        string path,
        out int iconId)
    {
        const string prefix = "/instrument-icons/";
        const string suffix = ".png";

        iconId = 0;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
            !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = path[prefix.Length..^suffix.Length];
        return int.TryParse(value, out iconId) && iconId > 0;
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
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "Content-Security-Policy: default-src 'self'; connect-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'\r\n" +
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
