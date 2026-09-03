using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Managers;
using MidiBard.Playlist;
using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.RemoteControl;

internal sealed class RemoteControlService : IRemoteControlApi
{
    private const int MaxEventPollTimeoutMs = 30000;

    private readonly Plugin _plugin;

    public RemoteControlService(Plugin plugin)
    {
        _plugin = plugin;
    }

    public Task<StatusResponse> GetStatusAsync()
    {
        return DalamudApi.Framework.RunOnFrameworkThread(BuildStatus);
    }

    public Task<PlaylistsResponse> GetPlaylistsAsync()
    {
        return DalamudApi.Framework.Run(async () =>
        {
            var current = _plugin.PlaylistManager.CurrentPlaylist;
            var currentId = current?.IsTemp == false ? current.Id : (int?)null;
            var playlists = await _plugin.PlaylistManager.GetAllPlaylistsAsync();

            return new PlaylistsResponse(
                playlists.Select(playlist => new PlaylistSummaryResponse(
                    playlist.Id,
                    playlist.Name,
                    playlist.Songs.Count,
                    DurationMs(playlist.Duration),
                    currentId == playlist.Id))
                .ToArray());
        });
    }

    public Task<PlaylistResponse> GetPlaylistAsync(int? playlistId)
    {
        return DalamudApi.Framework.Run(async () =>
        {
            if (playlistId is null)
            {
                var current = _plugin.PlaylistManager.CurrentPlaylist;
                if (current == null)
                    throw PlaylistNotFound("No current playlist is available.");

                return ToPlaylistResponse(current, isCurrent: true);
            }

            if (playlistId <= 0)
                throw InvalidRequest("playlistId must be greater than zero.");

            var playlist = await _plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId.Value);
            if (playlist == null)
                throw PlaylistNotFound($"Playlist {playlistId.Value} was not found.");

            var currentPlaylist = _plugin.PlaylistManager.CurrentPlaylist;
            var isCurrent = currentPlaylist?.IsTemp == false
                && currentPlaylist.Id == playlist.Id;
            return ToPlaylistResponse(playlist, isCurrent);
        });
    }

    public Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request)
    {
        var fileName = request.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw InvalidRequest("fileName must be an exact MIDI basename including extension.");
        }

        return DalamudApi.Framework.Run(async () =>
        {
            var availability = BuildControlAvailability(
                _plugin.RemotePlaybackLifecycle.GetSnapshot());
            RequireCanLoad(availability);

            var matches = FindExactFileNameMatches(_plugin.PlaylistManager.CurrentPlaylist, fileName);
            if (matches.Count == 0)
            {
                throw new RemoteControlException(
                    404,
                    "playback_not_found",
                    $"No song named '{fileName}' exists in the current playlist.");
            }

            if (matches.Count > 1)
            {
                throw new RemoteControlException(
                    409,
                    "playback_ambiguous",
                    $"More than one song named '{fileName}' exists in the current playlist.");
            }

            var loaded = await _plugin.PlaybackUserActions.LoadPlaylistSong(matches[0]);

            if (!loaded)
            {
                throw new RemoteControlException(
                    500,
                    "internal_error",
                    $"MidiBard could not load '{fileName}'.");
            }

            var snapshot = _plugin.RemotePlaybackLifecycle.GetSnapshot();
            if (snapshot.PlaybackId is not Guid playbackId || snapshot.FileName == null)
            {
                throw new RemoteControlException(
                    500,
                    "internal_error",
                    "MidiBard loaded the song but did not establish playback state.");
            }

            return new LoadPlaybackResponse(
                playbackId,
                snapshot.FileName,
                snapshot.DurationMs);
        });
    }

    public Task<LoadPlaybackResponse> LoadPlaylistSongAsync(LoadPlaylistSongRequest request)
    {
        if (request.PlaylistId <= 0)
            throw InvalidRequest("playlistId must be greater than zero.");
        if (request.SongId <= 0)
            throw InvalidRequest("songId must be greater than zero.");

        return DalamudApi.Framework.Run(async () =>
        {
            var availability = BuildControlAvailability(
                _plugin.RemotePlaybackLifecycle.GetSnapshot());
            RequireCanLoad(availability);

            var result = await _plugin.PlaylistManager.LoadPlaylistSongAsync(
                request.PlaylistId,
                request.SongId);

            switch (result)
            {
                case PlaylistSongLoadResult.PlaylistNotFound:
                    throw PlaylistNotFound($"Playlist {request.PlaylistId} was not found.");
                case PlaylistSongLoadResult.SongNotFound:
                    throw new RemoteControlException(
                        404,
                        "playback_not_found",
                        $"Song {request.SongId} was not found in playlist {request.PlaylistId}.");
                case PlaylistSongLoadResult.PerformanceUnavailable:
                    throw PerformanceUnavailable();
                case PlaylistSongLoadResult.PlaybackBusy:
                    throw InvalidState("Cannot load a song while playback or ensemble performance is active.");
                case PlaylistSongLoadResult.LoadFailed:
                    throw new RemoteControlException(
                        500,
                        "internal_error",
                        "MidiBard could not load the selected song.");
                case PlaylistSongLoadResult.Loaded:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result));
            }

            var snapshot = _plugin.RemotePlaybackLifecycle.GetSnapshot();
            if (snapshot.PlaybackId is not Guid playbackId || snapshot.FileName == null)
            {
                throw new RemoteControlException(
                    500,
                    "internal_error",
                    "MidiBard loaded the song but did not establish playback state.");
            }

            return new LoadPlaybackResponse(
                playbackId,
                snapshot.FileName,
                snapshot.DurationMs);
        });
    }

    public async Task PlayAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            var availability = BuildControlAvailability(snapshot);
            if (!availability.Player.CanPerform)
                throw PerformanceUnavailable();
            if (!availability.CanPlay)
            {
                if (AgentManager.AgentMetronome.EnsembleModeRunning)
                    throw InvalidState("Cannot start solo playback while an ensemble is running.");

                throw InvalidState($"Cannot play while playback state is {ToWireState(snapshot.State)}.");
            }

            _plugin.PlaybackUserActions.PlayPause();
        });
    }

    public async Task PauseAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            if (AgentManager.AgentMetronome.EnsembleModeRunning)
                throw InvalidState("Cannot pause solo playback while an ensemble is running.");
            if (snapshot.State != RemotePlaybackState.Playing)
                throw InvalidState("Cannot pause unless playback is currently playing.");

            _plugin.PlaybackUserActions.PlayPause();
        });
    }

    public async Task StopAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            RequireCurrentPlayback(request.PlaybackId);
            _plugin.PlaybackUserActions.StopPlayback();
        });
    }

    public async Task PreviousAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            RequireCanNavigate(snapshot);
            _plugin.MidiPlayerControl.Prev();
        });
    }

    public async Task NextAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            RequireCanNavigate(snapshot);
            _plugin.MidiPlayerControl.Next();
        });
    }

    public async Task SeekAsync(SeekPlaybackRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            RequireCanSeek(snapshot);

            if (request.PositionMs < 0 || request.PositionMs > snapshot.DurationMs)
            {
                throw InvalidRequest(
                    $"positionMs must be between 0 and {snapshot.DurationMs}.");
            }

            var position = TimeSpan.FromMilliseconds(request.PositionMs);
            _plugin.MidiPlayerControl.SetTime(
                new MetricTimeSpan(request.PositionMs * 1000));
            _plugin.IpcProvider.SetPlaybackTime(position);
        });
    }

    public async Task SetPlayModeAsync(SetPlayModeRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            _plugin.Config.PlayMode = (int)ParsePlayMode(request.PlayMode);
            _plugin.IpcProvider.SyncAllSettings();
        });
    }

    public async Task SetEnsembleAutoAdvanceAsync(
        SetEnsembleAutoAdvanceRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            _plugin.Config.EnableEnsemblePlayMode = request.Enabled;
            _plugin.IpcProvider.SyncAllSettings();
        });
    }

    public async Task BeginEnsembleReadyCheckAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            var availability = BuildControlAvailability(snapshot);
            if (!availability.Player.CanPerform)
                throw PerformanceUnavailable();

            if (snapshot.State != RemotePlaybackState.Ready)
                throw InvalidState("Ensemble ready-check requires a loaded, ready playback.");

            if (!DalamudApi.PartyList.IsInParty() ||
                !DalamudApi.PartyList.IsPartyLeader() ||
                !_plugin.Config.MonitorOnEnsemble ||
                !_plugin.Config.SyncClients)
            {
                throw new RemoteControlException(
                    409,
                    "ensemble_unavailable",
                    "This MidiBard client is not ready to control a synchronized ensemble.");
            }

            if (AgentManager.AgentMetronome.EnsembleModeRunning)
            {
                throw new RemoteControlException(
                    409,
                    "ensemble_unavailable",
                    "An ensemble is already running.");
            }

            _plugin.PlaybackUserActions.BeginEnsembleReadyCheck();
        });
    }

    public EventPollResponse PollEvents(long afterSequence, int timeoutMs)
    {
        if (afterSequence < 0)
            throw InvalidRequest("after must be zero or greater.");
        if (timeoutMs < 0 || timeoutMs > MaxEventPollTimeoutMs)
            throw InvalidRequest($"timeoutMs must be between 0 and {MaxEventPollTimeoutMs}.");

        try
        {
            var events = _plugin.RemotePlaybackLifecycle.Events.WaitForEventsAfter(
                afterSequence,
                TimeSpan.FromMilliseconds(timeoutMs));

            return new EventPollResponse(
                events.Select(ToResponse).ToArray(),
                _plugin.RemotePlaybackLifecycle.Events.LatestSequence);
        }
        catch (RemoteEventHistoryLostException)
        {
            throw new RemoteControlException(
                410,
                "event_history_lost",
                "Requested event history is no longer available.");
        }
    }

    internal static IReadOnlyList<int> FindExactFileNameMatches(
        PlaylistModel? playlist,
        string fileName)
    {
        if (playlist == null)
            return Array.Empty<int>();

        return playlist.Songs
            .Select((playlistSong, index) => new
            {
                Index = index,
                FileName = playlistSong.Song == null
                    ? null
                    : Path.GetFileName(playlistSong.Song.FilePath),
            })
            .Where(item => string.Equals(
                item.FileName,
                fileName,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Index)
            .ToArray();
    }

    private StatusResponse BuildStatus()
    {
        var snapshot = _plugin.RemotePlaybackLifecycle.GetSnapshot();
        var availability = BuildControlAvailability(snapshot);
        NowPlayingResponse? nowPlaying = null;

        if (snapshot.PlaybackId is Guid playbackId && snapshot.FileName != null)
        {
            var currentTime = _plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
            var positionMs = currentTime == null ? 0 : currentTime.TotalMicroseconds / 1000;
            var loadedPlaylist = _plugin.PlaylistManager.CurrentPlaylist;
            var currentSongId = _plugin.PlaylistManager.CurrentPlayingSong?.Song?.Id;
            nowPlaying = new NowPlayingResponse(
                playbackId,
                snapshot.FileName,
                Math.Max(0, positionMs),
                snapshot.DurationMs,
                loadedPlaylist?.IsTemp == false ? loadedPlaylist.Id : null,
                currentSongId > 0 ? currentSongId : null);
        }

        var ensembleRunning =
            AgentManager.AgentMetronome.EnsembleModeRunning &&
            AgentManager.AgentPerformance.InPerformanceMode;

        var currentPlaylist = _plugin.PlaylistManager.CurrentPlaylist;

        return new StatusResponse(
            _plugin.RemotePlaybackLifecycle.Events.LatestSequence,
            new PlaybackStatusResponse(
                ToWireState(snapshot.State),
                ToWirePlayMode((PlayMode)_plugin.Config.PlayMode),
                nowPlaying),
            new EnsembleStatusResponse(
                DalamudApi.PartyList.IsInParty(),
                DalamudApi.PartyList.IsPartyLeader(),
                ensembleRunning,
                _plugin.Config.MonitorOnEnsemble,
                _plugin.Config.SyncClients,
                _plugin.Config.EnableEnsemblePlayMode),
            new PlayerStatusResponse(
                availability.Player.PlayerLoaded,
                (int)availability.Player.ClassJobId,
                availability.Player.ClassJobAbbreviation,
                availability.Player.CanPerform),
            new PlaybackControlsResponse(
                availability.CanLoad,
                availability.CanPlay,
                availability.CanPause,
                availability.CanStop,
                availability.CanStartEnsemble,
                CanNavigate(snapshot),
                CanNavigate(snapshot),
                CanSeek(snapshot)),
            currentPlaylist == null
                ? null
                : new CurrentPlaylistResponse(
                    currentPlaylist.Id,
                    currentPlaylist.Name,
                    currentPlaylist.IsTemp));
    }

    private PlaybackControlAvailabilitySnapshot BuildControlAvailability(
        RemotePlaybackSnapshot snapshot)
    {
        return PlaybackControlAvailability.Evaluate(
            PlaybackControlAvailability.GetPlayerSnapshot(),
            ToControlState(snapshot.State),
            snapshot.PlaybackId.HasValue && snapshot.FileName != null,
            AgentManager.AgentMetronome.EnsembleModeRunning,
            DalamudApi.PartyList.IsInParty(),
            DalamudApi.PartyList.IsPartyLeader(),
            _plugin.Config.MonitorOnEnsemble,
            _plugin.Config.SyncClients);
    }

    private static PlaybackControlState ToControlState(RemotePlaybackState state)
    {
        return state switch
        {
            RemotePlaybackState.Idle => PlaybackControlState.Idle,
            RemotePlaybackState.Ready => PlaybackControlState.Ready,
            RemotePlaybackState.Playing => PlaybackControlState.Playing,
            RemotePlaybackState.Paused => PlaybackControlState.Paused,
            RemotePlaybackState.Completed => PlaybackControlState.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private static void RequireCanLoad(PlaybackControlAvailabilitySnapshot availability)
    {
        if (!availability.Player.CanPerform)
            throw PerformanceUnavailable();
        if (!availability.CanLoad)
            throw InvalidState("Cannot load a song while playback or ensemble performance is active.");
    }

    private bool CanNavigate(RemotePlaybackSnapshot snapshot)
    {
        return PlaybackControlAvailability.GetPlayerSnapshot().CanPerform
            && snapshot.PlaybackId.HasValue
            && snapshot.FileName != null
            && !AgentManager.AgentMetronome.EnsembleModeRunning
            && (_plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0) > 0;
    }

    private bool CanSeek(RemotePlaybackSnapshot snapshot)
    {
        return PlaybackControlAvailability.GetPlayerSnapshot().CanPerform
            && snapshot.PlaybackId.HasValue
            && snapshot.FileName != null
            && !AgentManager.AgentMetronome.EnsembleModeRunning;
    }

    private void RequireCanNavigate(RemotePlaybackSnapshot snapshot)
    {
        if (!PlaybackControlAvailability.GetPlayerSnapshot().CanPerform)
            throw PerformanceUnavailable();
        if (!CanNavigate(snapshot))
            throw InvalidState(
                "Cannot change songs while ensemble playback is active or no playlist is available.");
    }

    private void RequireCanSeek(RemotePlaybackSnapshot snapshot)
    {
        if (!PlaybackControlAvailability.GetPlayerSnapshot().CanPerform)
            throw PerformanceUnavailable();
        if (!CanSeek(snapshot))
            throw InvalidState("Cannot seek while ensemble playback is active.");
    }

    internal static PlaylistResponse ToPlaylistResponse(
        PlaylistModel playlist,
        bool isCurrent)
    {
        var songs = new List<PlaylistSongResponse>();
        for (var index = 0; index < playlist.Songs.Count; index++)
        {
            var playlistSong = playlist.Songs[index];
            var song = playlistSong.Song;
            if (song == null)
                continue;

            songs.Add(new PlaylistSongResponse(
                song.Id,
                index + 1,
                Path.GetFileName(song.FilePath),
                song.Name,
                song.Artist,
                song.ReleaseYear,
                DurationMs(song.Duration),
                song.PlayCount,
                song.LastPlayedAt?.ToString("O"),
                playlistSong.IsPlayed,
                song.Rating,
                song.Tags
                    .Select(tag => tag.Name ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray(),
                song.Comments,
                song.IsValid,
                song.FileLastModifiedAt.ToString("O"),
                playlistSong.AddedAt.ToString("O")));
        }

        return new PlaylistResponse(
            playlist.Id,
            playlist.Name,
            isCurrent,
            playlist.IsTemp,
            songs.Count,
            DurationMs(playlist.Duration),
            songs);
    }

    private static long DurationMs(TimeSpan duration)
        => Math.Max(0, (long)duration.TotalMilliseconds);

    private RemotePlaybackSnapshot RequireCurrentPlayback(Guid playbackId)
    {
        if (playbackId == Guid.Empty || !_plugin.RemotePlaybackLifecycle.IsCurrent(playbackId))
        {
            throw new RemoteControlException(
                409,
                "playback_changed",
                "The requested playback is no longer active.");
        }

        return _plugin.RemotePlaybackLifecycle.GetSnapshot();
    }

    private static PlaybackEventResponse ToResponse(RemotePlaybackEvent item)
    {
        return new PlaybackEventResponse(
            item.Sequence,
            item.Type switch
            {
                RemotePlaybackEventType.PlaybackLoaded => "playback_loaded",
                RemotePlaybackEventType.PlaybackStarted => "playback_started",
                RemotePlaybackEventType.PlaybackPaused => "playback_paused",
                RemotePlaybackEventType.PlaybackCompleted => "playback_completed",
                RemotePlaybackEventType.PlaybackStopped => "playback_stopped",
                RemotePlaybackEventType.EnsembleStarted => "ensemble_started",
                RemotePlaybackEventType.EnsembleStopped => "ensemble_stopped",
                RemotePlaybackEventType.StatusChanged => "status_changed",
                _ => throw new ArgumentOutOfRangeException(nameof(item.Type)),
            },
            item.PlaybackId);
    }

    private static string ToWireState(RemotePlaybackState state)
    {
        return state switch
        {
            RemotePlaybackState.Idle => "idle",
            RemotePlaybackState.Ready => "ready",
            RemotePlaybackState.Playing => "playing",
            RemotePlaybackState.Paused => "paused",
            RemotePlaybackState.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private static string ToWirePlayMode(PlayMode playMode)
    {
        return playMode switch
        {
            PlayMode.Single => "single",
            PlayMode.SingleRepeat => "single_repeat",
            PlayMode.ListOrdered => "list_ordered",
            PlayMode.ListRepeat => "list_repeat",
            PlayMode.Random => "random",
            _ => throw new ArgumentOutOfRangeException(nameof(playMode)),
        };
    }

    private static PlayMode ParsePlayMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "single" => PlayMode.Single,
            "single_repeat" => PlayMode.SingleRepeat,
            "list_ordered" => PlayMode.ListOrdered,
            "list_repeat" => PlayMode.ListRepeat,
            "random" => PlayMode.Random,
            _ => throw InvalidRequest(
                "playMode must be one of: single, single_repeat, list_ordered, list_repeat, random."),
        };
    }

    private static RemoteControlException PlaylistNotFound(string message)
        => new(404, "playlist_not_found", message);

    private static RemoteControlException PerformanceUnavailable()
        => new(
            409,
            "performance_unavailable",
            "Switch to Bard before loading or starting playback.");

    private static RemoteControlException InvalidRequest(string message)
        => new(400, "invalid_request", message);

    private static RemoteControlException InvalidState(string message)
        => new(409, "invalid_playback_state", message);
}
