using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MidiBard.RemoteControl;

internal sealed record RemoteControlQueryParameter(
    string Name,
    Type Type,
    bool Required,
    string Description,
    object? DefaultValue = null);

internal sealed record RemoteControlEndpointResult(int StatusCode, object? Body);

internal sealed class RemoteControlRequestContext
{
    public string Target { get; }
    public byte[] Body { get; }

    public RemoteControlRequestContext(string target, byte[] body)
    {
        Target = target;
        Body = body;
    }

    public long GetLongQuery(string name, long defaultValue)
    {
        var value = QueryValue(name);
        if (value == null)
            return defaultValue;

        if (!long.TryParse(value, out var parsed))
            throw InvalidQuery(name);

        return parsed;
    }

    public int GetIntQuery(string name, int defaultValue)
    {
        var value = QueryValue(name);
        if (value == null)
            return defaultValue;

        if (!int.TryParse(value, out var parsed))
            throw InvalidQuery(name);

        return parsed;
    }

    public int? GetOptionalIntQuery(string name)
    {
        var value = QueryValue(name);
        if (value == null)
            return null;

        if (!int.TryParse(value, out var parsed))
            throw InvalidQuery(name);

        return parsed;
    }

    private string? QueryValue(string name)
    {
        var queryIndex = Target.IndexOf('?');
        if (queryIndex < 0 || queryIndex == Target.Length - 1)
            return null;

        foreach (var pair in Target[(queryIndex + 1)..].Split('&'))
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

    private static RemoteControlException InvalidQuery(string name)
        => new(400, "invalid_request", name + " must be an integer.");
}

internal sealed class RemoteControlEndpointDefinition
{
    public string Method { get; }
    public string Path { get; }
    public string OperationId { get; }
    public string Description { get; }
    public Type? RequestType { get; }
    public Type? ResponseType { get; }
    public int SuccessStatusCode { get; }
    public IReadOnlyList<RemoteControlQueryParameter> QueryParameters { get; }
    public IReadOnlyList<int> ErrorStatusCodes { get; }
    public Func<IRemoteControlApi, RemoteControlRequestContext, Task<RemoteControlEndpointResult>> ExecuteAsync { get; }

    private RemoteControlEndpointDefinition(
        string method,
        string path,
        string operationId,
        string description,
        Type? requestType,
        Type? responseType,
        int successStatusCode,
        IReadOnlyList<RemoteControlQueryParameter> queryParameters,
        IReadOnlyList<int> errorStatusCodes,
        Func<IRemoteControlApi, RemoteControlRequestContext, Task<RemoteControlEndpointResult>> executeAsync)
    {
        Method = method;
        Path = path;
        OperationId = operationId;
        Description = description;
        RequestType = requestType;
        ResponseType = responseType;
        SuccessStatusCode = successStatusCode;
        QueryParameters = queryParameters;
        ErrorStatusCodes = errorStatusCodes;
        ExecuteAsync = executeAsync;
    }

    public static RemoteControlEndpointDefinition Get<TResponse>(
        string path,
        string operationId,
        string description,
        Func<IRemoteControlApi, RemoteControlRequestContext, Task<TResponse>> handler,
        IReadOnlyList<RemoteControlQueryParameter>? queryParameters = null,
        params int[] errorStatusCodes)
    {
        return new RemoteControlEndpointDefinition(
            "GET",
            path,
            operationId,
            description,
            null,
            typeof(TResponse),
            200,
            queryParameters ?? Array.Empty<RemoteControlQueryParameter>(),
            WithCommonErrors(errorStatusCodes),
            async (api, request) => new RemoteControlEndpointResult(200, await handler(api, request)));
    }

    public static RemoteControlEndpointDefinition Post<TRequest, TResponse>(
        string path,
        string operationId,
        string description,
        Func<IRemoteControlApi, TRequest, Task<TResponse>> handler,
        params int[] errorStatusCodes)
    {
        return new RemoteControlEndpointDefinition(
            "POST",
            path,
            operationId,
            description,
            typeof(TRequest),
            typeof(TResponse),
            200,
            Array.Empty<RemoteControlQueryParameter>(),
            WithCommonErrors(errorStatusCodes),
            async (api, request) =>
            {
                var payload = ReadJson<TRequest>(request.Body);
                return new RemoteControlEndpointResult(200, await handler(api, payload));
            });
    }

    public static RemoteControlEndpointDefinition Post<TRequest>(
        string path,
        string operationId,
        string description,
        Func<IRemoteControlApi, TRequest, Task> handler,
        params int[] errorStatusCodes)
    {
        return new RemoteControlEndpointDefinition(
            "POST",
            path,
            operationId,
            description,
            typeof(TRequest),
            null,
            204,
            Array.Empty<RemoteControlQueryParameter>(),
            WithCommonErrors(errorStatusCodes),
            async (api, request) =>
            {
                await handler(api, ReadJson<TRequest>(request.Body));
                return new RemoteControlEndpointResult(204, null);
            });
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

        return JsonSerializer.Deserialize<T>(body, RemoteControlJson.Options)
            ?? throw new RemoteControlException(
                400,
                "invalid_request",
                "Request body is required.");
    }

    private static IReadOnlyList<int> WithCommonErrors(IEnumerable<int> errorStatusCodes)
    {
        var result = new SortedSet<int> { 401, 500 };
        foreach (var statusCode in errorStatusCodes)
            result.Add(statusCode);
        return new List<int>(result);
    }
}

internal static class RemoteControlApiContract
{
    public static IReadOnlyList<RemoteControlEndpointDefinition> Endpoints { get; } =
        new RemoteControlEndpointDefinition[]
        {
            RemoteControlEndpointDefinition.Get<StatusResponse>(
                "/api/v1/status",
                "getStatus",
                "Get current playback and ensemble status.",
                (api, _) => api.GetStatusAsync()),

            RemoteControlEndpointDefinition.Get<PlaylistsResponse>(
                "/api/v1/playlists",
                "getPlaylists",
                "Get summaries for persisted MidiBard playlists.",
                (api, _) => api.GetPlaylistsAsync()),

            RemoteControlEndpointDefinition.Get<PlaylistResponse>(
                "/api/v1/playlist",
                "getPlaylist",
                "Get the current playlist, or inspect a persisted playlist without making it current.",
                (api, request) => api.GetPlaylistAsync(request.GetOptionalIntQuery("playlistId")),
                new[]
                {
                    new RemoteControlQueryParameter("playlistId", typeof(int), false, "Persisted playlist ID. Omit to return the current playlist."),
                },
                400,
                404),

            RemoteControlEndpointDefinition.Get<EventPollResponse>(
                "/api/v1/events",
                "pollEvents",
                "Long-poll playback and ensemble lifecycle events.",
                (api, request) => Task.FromResult(api.PollEvents(
                    request.GetLongQuery("after", 0),
                    request.GetIntQuery("timeoutMs", 0))),
                new[]
                {
                    new RemoteControlQueryParameter("after", typeof(long), false, "Return events after this sequence number.", 0L),
                    new RemoteControlQueryParameter("timeoutMs", typeof(int), false, "Long-poll timeout in milliseconds, from 0 through 30000.", 0),
                },
                400,
                410),

            RemoteControlEndpointDefinition.Post<LoadPlaybackRequest, LoadPlaybackResponse>(
                "/api/v1/playback/load",
                "loadPlayback",
                "Load one song from the current playlist by exact MIDI basename.",
                (api, request) => api.LoadPlaybackAsync(request),
                400,
                404,
                409),

            RemoteControlEndpointDefinition.Post<LoadPlaylistSongRequest, LoadPlaybackResponse>(
                "/api/v1/playback/load-song",
                "loadPlaylistSong",
                "Load one song by stable persisted playlist and song IDs.",
                (api, request) => api.LoadPlaylistSongAsync(request),
                400,
                404,
                409),

            RemoteControlEndpointDefinition.Post<PlaybackHandleRequest>(
                "/api/v1/playback/play",
                "playPlayback",
                "Start or resume the currently loaded playback.",
                (api, request) => api.PlayAsync(request),
                409),

            RemoteControlEndpointDefinition.Post<PlaybackHandleRequest>(
                "/api/v1/playback/pause",
                "pausePlayback",
                "Pause the currently playing solo playback.",
                (api, request) => api.PauseAsync(request),
                409),

            RemoteControlEndpointDefinition.Post<PlaybackHandleRequest>(
                "/api/v1/playback/stop",
                "stopPlayback",
                "Stop the currently loaded playback.",
                (api, request) => api.StopAsync(request),
                409),

            RemoteControlEndpointDefinition.Post<PlaybackHandleRequest>(
                "/api/v1/playback/previous",
                "previousPlayback",
                "Load the previous song using MidiBard's current play-mode navigation.",
                (api, request) => api.PreviousAsync(request),
                409),

            RemoteControlEndpointDefinition.Post<PlaybackHandleRequest>(
                "/api/v1/playback/next",
                "nextPlayback",
                "Load the next song using MidiBard's current play-mode navigation.",
                (api, request) => api.NextAsync(request),
                409),

            RemoteControlEndpointDefinition.Post<SeekPlaybackRequest>(
                "/api/v1/playback/seek",
                "seekPlayback",
                "Seek the currently loaded solo playback to an absolute position in milliseconds.",
                (api, request) => api.SeekAsync(request),
                400,
                409),

            RemoteControlEndpointDefinition.Post<PlaybackHandleRequest>(
                "/api/v1/ensemble/ready-check",
                "beginEnsembleReadyCheck",
                "Begin an ensemble ready check for the currently loaded playback.",
                (api, request) => api.BeginEnsembleReadyCheckAsync(request),
                409),
        };
}
