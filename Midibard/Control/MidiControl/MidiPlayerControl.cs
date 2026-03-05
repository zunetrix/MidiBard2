using System;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Extensions.General;

namespace MidiBard.Control.MidiControl;

internal class MidiPlayerControl
{
    private Plugin Plugin { get; }
    public int playDeltaTime = 0;
    public MidiPlayerStatus _status = MidiPlayerStatus.Stopped;

    public MidiPlayerControl(Plugin plugin)
    {
        Plugin = plugin;
    }

    public void Play()
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            if (!Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Any() ?? false)
            {
                DalamudApi.PluginLog.Information("empty playlist");
                return;
            }

            if (Plugin.PlaylistManager.CurrentSongIndex < 0)
            {
                Plugin.PlaylistManager.LoadPlayback(0, true);
            }
            else
            {
                Plugin.PlaylistManager.LoadPlayback(null, true);
            }
        }
        else
        {
            try
            {
                if (Plugin.CurrentBardPlayback.GetCurrentTime<MidiTimeSpan>() == Plugin.CurrentBardPlayback.GetDuration<MidiTimeSpan>())
                {
                    Plugin.CurrentBardPlayback.MoveToTime(new MidiTimeSpan(0));
                }

                DoPlay();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error when try to start playing, maybe the playback has been disposed?");
            }
        }
    }

    public void DoPlay(bool isEnsemble = false)
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded) return;

        if (Plugin.Config.autoPostSongName)
        {
            Plugin.PlaylistManager.SendSongToChat(Plugin.PlaylistManager.CurrentSongIndex);
        }

        playDeltaTime = 0;
        Plugin.CurrentBardPlayback.Start();
        _status = MidiPlayerStatus.Playing;

        if (isEnsemble)
        {
            Plugin.LyricsPlayer.EnsembleStart();
        }

        Plugin.LyricsPlayer.Play();
    }

    public void Pause()
    {
        Plugin.CurrentBardPlayback.Stop();
        _status = MidiPlayerStatus.Paused;
    }

    public void PlayPause()
    {
        if (Plugin.FilePlayback.IsWaiting)
        {
            Plugin.FilePlayback.SkipWaiting();
        }
        else
        {
            if (Plugin.CurrentBardPlayback.IsRunning)
            {
                Pause();
                var TimeSpan = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
                DalamudApi.PluginLog.Information($"Timespan: [{TimeSpan.Minutes}:{TimeSpan.Seconds}:{TimeSpan.Milliseconds}]");
            }
            else
            {
                Play();
            }
        }
    }

    public void Stop()
    {
        // Set song as played if stoped
        Plugin.PlaylistManager.SetCurrentSongAsPlayed();
        Plugin.CurrentBardPlayback.Dispose();
        // TODO: reset state?
        Plugin.CurrentBardPlayback = new BardPlayback(Plugin);
        Plugin.LyricsPlayer.Stop();
        _status = MidiPlayerStatus.Stopped;
    }

    public void Next(bool startPlaying = false)
    {
        Plugin.LyricsPlayer.Stop();
        _status = MidiPlayerStatus.Stopped;
        var songIndex = GetSongIndex(Plugin.PlaylistManager.CurrentSongIndex, true);
        Plugin.PlaylistManager.LoadPlayback(songIndex, Plugin.CurrentBardPlayback.IsRunning || startPlaying);
    }

    public void Prev()
    {
        Plugin.LyricsPlayer.Stop();
        _status = MidiPlayerStatus.Stopped;
        var songIndex = GetSongIndex(Plugin.PlaylistManager.CurrentSongIndex, false);
        Plugin.PlaylistManager.LoadPlayback(songIndex, Plugin.CurrentBardPlayback.IsRunning);

    }

    public int GetSongIndex(int songIndex, bool next)
    {
        var playMode = (PlayMode)Plugin.Config.PlayMode;
        switch (playMode)
        {
            case PlayMode.Single:
            case PlayMode.SingleRepeat:
            case PlayMode.ListOrdered:
            case PlayMode.ListRepeat:
                songIndex += next ? 1 : -1;
                break;
        }

        if (playMode == PlayMode.ListRepeat)
        {
            songIndex = songIndex.Cycle(0, (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0) - 1);
        }
        else if (playMode == PlayMode.Random)
        {
            if ((Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0) > 1)
            {
                var r = new Random();
                do
                {
                    songIndex = r.Next(0, Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0);
                } while (songIndex == Plugin.PlaylistManager.CurrentSongIndex);
            }
        }

        return songIndex;
    }

    public void SetTime(ITimeSpan time)
    {
        var bardPlayback = Plugin.CurrentBardPlayback;
        if (bardPlayback is null) return;

        try
        {
            if (bardPlayback.IsRunning)
            {
                bardPlayback.MoveToTime(time);
            }
            else
            {
                bardPlayback.MoveToTime(time);
                bardPlayback.PlaybackStart = time;
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString(), "error when try setting current playback time");
        }
    }

    public void MoveTime(double timeInSeconds)
    {
        try
        {
            var metricTimeSpan = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
            var dura = Plugin.CurrentBardPlayback.GetDuration<MetricTimeSpan>();
            var totalMicroseconds = metricTimeSpan.TotalMicroseconds + (long)(timeInSeconds * 1_000_000);
            if (totalMicroseconds < 0) totalMicroseconds = 0;
            if (totalMicroseconds > dura.TotalMicroseconds) totalMicroseconds = dura.TotalMicroseconds;
            Plugin.CurrentBardPlayback.MoveToTime(new MetricTimeSpan(totalMicroseconds));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString(), "error when try moving current playback time");
        }
    }

    public bool ChangeDeltaTime(int delta)
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded || !Plugin.CurrentBardPlayback.IsRunning)
        {
            playDeltaTime = 0;
            return false;
        }

        var currentTime = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
        long msTime = currentTime.TotalMicroseconds;
        //DalamudApi.PluginLog.Debug("curTime:" + msTime);
        if (msTime + delta * 1000 < 0)
        {
            return false;
        }
        msTime += delta * 1000;
        MetricTimeSpan newTime = new MetricTimeSpan(msTime);
        //DalamudApi.PluginLog.Debug("newTime:" + newTime.TotalMicroseconds);
        Plugin.CurrentBardPlayback.MoveToTime(newTime);
        playDeltaTime += delta;

        return true;
    }

    internal void StopLrc()
    {
        Plugin.LyricsPlayer.Stop();
        _status = MidiPlayerStatus.Stopped;
        playDeltaTime = 0;
    }

    public enum MidiPlayerStatus
    {
        Stopped,
        Paused,
        Playing
    }
}

