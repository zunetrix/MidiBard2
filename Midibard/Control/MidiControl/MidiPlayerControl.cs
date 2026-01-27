// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Util;
using MidiBard.Util.Lyrics;

namespace MidiBard.Control.MidiControl;

internal static class MidiPlayerControl
{
    internal static void Play()
    {
        if (Plugin.CurrentBardPlayback == null)
        {
            if (!PlaylistManager.FilePathList.Any())
            {
                DalamudApi.PluginLog.Information("empty playlist");
                return;
            }

            if (PlaylistManager.CurrentSongIndex < 0)
            {
                PlaylistManager.LoadPlayback(0, true);
            }
            else
            {
                PlaylistManager.LoadPlayback(null, true);
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

    public static void DoPlay(bool isEnsemble = false)
    {
        if (Plugin.CurrentBardPlayback == null) return;

        if (Plugin.Config.autoPostSongName)
        {
            PlaylistManager.SendSongToChat(PlaylistManager.CurrentSongIndex);
        }

        playDeltaTime = 0;
        Plugin.CurrentBardPlayback.Start();
        _stat = e_stat.Playing;

        if (isEnsemble)
        {
            Lrc.EnsembleStart();
        }

        Lrc.Play();
    }

    internal static void Pause()
    {
        Plugin.CurrentBardPlayback?.Stop();
        _stat = e_stat.Paused;
    }

    internal static void PlayPause()
    {
        if (FilePlayback.IsWaiting)
        {
            FilePlayback.SkipWaiting();
        }
        else
        {
            if (Plugin.IsPlaying)
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

    internal static void Stop()
    {
        // Set song as played if stoped
        PlaylistManager.SetCurrentSongAsPlayed();
        Plugin.CurrentBardPlayback?.Dispose();
        Plugin.CurrentBardPlayback = null;
        Lrc.Stop();
        _stat = e_stat.Stopped;
    }

    internal static void Next(bool startPlaying = false)
    {
        Lrc.Stop();
        _stat = e_stat.Stopped;
        var songIndex = GetSongIndex(PlaylistManager.CurrentSongIndex, true);
        PlaylistManager.LoadPlayback(songIndex, Plugin.IsPlaying || startPlaying);
    }

    internal static void Prev()
    {
        Lrc.Stop();
        _stat = e_stat.Stopped;
        var songIndex = GetSongIndex(PlaylistManager.CurrentSongIndex, false);
        PlaylistManager.LoadPlayback(songIndex, Plugin.IsPlaying);

    }

    private static int GetSongIndex(int songIndex, bool next)
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
            songIndex = songIndex.Cycle(0, PlaylistManager.FilePathList.Count - 1);
        }
        else if (playMode == PlayMode.Random)
        {
            if (PlaylistManager.FilePathList.Count > 1)
            {
                var r = new Random();
                do
                {
                    songIndex = r.Next(0, PlaylistManager.FilePathList.Count);
                } while (songIndex == PlaylistManager.CurrentSongIndex);
            }
        }

        return songIndex;
    }

    internal static void SetTime(ITimeSpan time)
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

    internal static void MoveTime(double timeInSeconds)
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

    internal static int playDeltaTime = 0;

    public enum e_stat
    {
        Stopped,
        Paused,
        Playing
    }

    public static e_stat _stat = e_stat.Stopped;

    internal static bool ChangeDeltaTime(int delta)
    {
        if (Plugin.CurrentBardPlayback == null || !Plugin.CurrentBardPlayback.IsRunning)
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

    internal static void StopLrc()
    {
        Lrc.Stop();
        _stat = e_stat.Stopped;
        playDeltaTime = 0;
    }
}
