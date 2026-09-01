using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MidiBard.RemoteControl;

internal sealed record StatusResponse(
    long LatestEventSequence,
    PlaybackStatusResponse Playback,
    EnsembleStatusResponse Ensemble);

internal sealed record PlaybackStatusResponse(
    string State,
    string PlayMode,
    NowPlayingResponse? NowPlaying);

internal sealed record NowPlayingResponse(
    Guid PlaybackId,
    string FileName,
    long PositionMs,
    long DurationMs);

internal sealed record EnsembleStatusResponse(
    bool InParty,
    bool IsPartyLeader,
    bool Running,
    bool MonitoringEnabled,
    bool SyncClientsEnabled);

internal sealed record LoadPlaybackRequest(string? FileName);

internal sealed record LoadPlaybackResponse(
    Guid PlaybackId,
    string FileName,
    long DurationMs);

internal sealed record PlaybackHandleRequest(Guid PlaybackId);

internal sealed record EventPollResponse(
    IReadOnlyList<PlaybackEventResponse> Events,
    long LatestSequence);

internal sealed record PlaybackEventResponse(
    long Sequence,
    string Type,
    Guid PlaybackId);

internal sealed record ErrorResponse(string Code, string Message);

internal sealed class RemoteControlException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public RemoteControlException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

internal interface IRemoteControlApi
{
    Task<StatusResponse> GetStatusAsync();
    Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request);
    Task PlayAsync(PlaybackHandleRequest request);
    Task StopAsync(PlaybackHandleRequest request);
    Task BeginEnsembleReadyCheckAsync(PlaybackHandleRequest request);
    EventPollResponse PollEvents(long afterSequence, int timeoutMs);
}

internal static class RemoteControlJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
