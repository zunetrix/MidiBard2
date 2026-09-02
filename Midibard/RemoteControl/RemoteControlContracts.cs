#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MidiBard.RemoteControl;

internal sealed record StatusResponse(
    long LatestEventSequence,
    PlaybackStatusResponse Playback,
    EnsembleStatusResponse Ensemble,
    PlayerStatusResponse Player,
    PlaybackControlsResponse Controls,
    CurrentPlaylistResponse? CurrentPlaylist);

internal sealed record PlaybackStatusResponse(
    string State,
    string PlayMode,
    NowPlayingResponse? NowPlaying);

internal sealed record NowPlayingResponse(
    Guid PlaybackId,
    string FileName,
    long PositionMs,
    long DurationMs,
    int? PlaylistId,
    int? SongId);

internal sealed record EnsembleStatusResponse(
    bool InParty,
    bool IsPartyLeader,
    bool Running,
    bool MonitoringEnabled,
    bool SyncClientsEnabled);

internal sealed record PlayerStatusResponse(
    bool Loaded,
    int ClassJobId,
    string ClassJobAbbreviation,
    bool CanPerform);

internal sealed record PlaybackControlsResponse(
    bool CanLoad,
    bool CanPlay,
    bool CanPause,
    bool CanStop,
    bool CanStartEnsemble);

internal sealed record CurrentPlaylistResponse(
    int Id,
    string Name,
    bool IsTemporary);

internal sealed record LoadPlaybackRequest(string FileName);

internal sealed record LoadPlaylistSongRequest(
    int PlaylistId,
    int SongId);

internal sealed record LoadPlaybackResponse(
    Guid PlaybackId,
    string FileName,
    long DurationMs);

internal sealed record PlaylistsResponse(
    IReadOnlyList<PlaylistSummaryResponse> Playlists);

internal sealed record PlaylistSummaryResponse(
    int Id,
    string Name,
    int SongCount,
    long DurationMs,
    bool IsCurrent);

internal sealed record PlaylistResponse(
    int Id,
    string Name,
    bool IsCurrent,
    bool IsTemporary,
    int SongCount,
    long DurationMs,
    IReadOnlyList<PlaylistSongResponse> Songs);

internal sealed record PlaylistSongResponse(
    int SongId,
    int Position,
    string FileName,
    string Name,
    string Artist,
    int ReleaseYear,
    long DurationMs,
    int PlayCount,
    string? LastPlayedAt,
    bool IsPlayed,
    int Rating,
    IReadOnlyList<string> Tags,
    string Comments,
    bool IsValid,
    string FileModifiedAt,
    string AddedAt);

internal sealed record PlaybackHandleRequest(Guid PlaybackId);

internal sealed record EventPollResponse(
    IReadOnlyList<PlaybackEventResponse> Events,
    long LatestSequence);

internal sealed record PlaybackEventResponse(
    long Sequence,
    string Type,
    Guid? PlaybackId);

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
    Task<PlaylistsResponse> GetPlaylistsAsync();
    Task<PlaylistResponse> GetPlaylistAsync(int? playlistId);
    Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request);
    Task<LoadPlaybackResponse> LoadPlaylistSongAsync(LoadPlaylistSongRequest request);
    Task PlayAsync(PlaybackHandleRequest request);
    Task PauseAsync(PlaybackHandleRequest request);
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
