using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Interaction;

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

    public Task<LoadPlaybackResponse> LoadPlaybackAsync(LoadPlaybackRequest request)
    {
        var fileName = request.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw InvalidRequest("fileName must be an exact MIDI basename including extension.");
        }

        return RunOnFrameworkThreadAsync(async () =>
        {
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

            var loaded = await _plugin.PlaylistManager.LoadPlayback(
                matches[0],
                startPlaying: false,
                sync: true);

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

    public async Task PlayAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
            if (snapshot.State is not RemotePlaybackState.Ready
                and not RemotePlaybackState.Paused
                and not RemotePlaybackState.Completed)
            {
                throw InvalidState($"Cannot play while playback state is {ToWireState(snapshot.State)}.");
            }

            _plugin.MidiPlayerControl.Play();
        });
    }

    public async Task StopAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            RequireCurrentPlayback(request.PlaybackId);
            _plugin.MidiPlayerControl.Stop();
        });
    }

    public async Task BeginEnsembleReadyCheckAsync(PlaybackHandleRequest request)
    {
        await DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            var snapshot = RequireCurrentPlayback(request.PlaybackId);
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

            _plugin.EnsembleManager.BeginEnsembleReadyCheck();
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
        NowPlayingResponse? nowPlaying = null;

        if (snapshot.PlaybackId is Guid playbackId && snapshot.FileName != null)
        {
            var currentTime = _plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
            var positionMs = currentTime == null ? 0 : currentTime.TotalMicroseconds / 1000;
            nowPlaying = new NowPlayingResponse(
                playbackId,
                snapshot.FileName,
                Math.Max(0, positionMs),
                snapshot.DurationMs);
        }

        var ensembleRunning =
            AgentManager.AgentMetronome.EnsembleModeRunning &&
            AgentManager.AgentPerformance.InPerformanceMode;

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
                _plugin.Config.SyncClients));
    }

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
                RemotePlaybackEventType.PlaybackStarted => "playback_started",
                RemotePlaybackEventType.PlaybackPaused => "playback_paused",
                RemotePlaybackEventType.PlaybackCompleted => "playback_completed",
                RemotePlaybackEventType.PlaybackStopped => "playback_stopped",
                RemotePlaybackEventType.EnsembleStarted => "ensemble_started",
                RemotePlaybackEventType.EnsembleStopped => "ensemble_stopped",
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

    private static RemoteControlException InvalidRequest(string message)
        => new(400, "invalid_request", message);

    private static RemoteControlException InvalidState(string message)
        => new(409, "invalid_playback_state", message);

    private static Task<T> RunOnFrameworkThreadAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _ = DalamudApi.Framework.RunOnFrameworkThread(() =>
        {
            _ = ExecuteAsync();
        });

        return completion.Task;

        async Task ExecuteAsync()
        {
            try
            {
                completion.SetResult(await action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }
    }
}
