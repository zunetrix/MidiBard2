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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Util.Lyrics;

namespace MidiBard.Control.MidiControl;

public class FilePlayback
{
    private Plugin Plugin { get; }
    internal float waitProgress = 0;
    internal Status waitStatus = Status.notWaiting;
    public bool IsWaiting => waitStatus == Status.waiting;
    public float GetWaitWaitProgress => waitProgress;
    internal void CancelWaiting() => waitStatus = Status.canceled;
    internal void SkipWaiting() => waitStatus = Status.skipped;

    public FilePlayback(Plugin plugin)
    {
        Plugin = plugin;
    }

    private BardPlayback GetPlaybackInstance(MidiFile midifile, string path)
    {
        DalamudApi.PluginLog.Debug($"[LoadPlayback] -> {path} START");
        var stopwatch = Stopwatch.StartNew();
        var playback = BardPlayback.GetBardPlayback(midifile, path);

        playback.InterruptNotesOnStop = true;
        playback.TrackNotes = true;
        playback.TrackProgram = true;
        playback.Speed = Plugin.Config.PlaySpeed;
        playback.Finished += Playback_Finished;

        DalamudApi.PluginLog.Debug($"[LoadPlayback] -> {path} OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");

        if (Plugin.Config.showNowPlayingInfo)
        {
            DalamudApi.ChatGui.Print(string.Format("[MidiBard 2] Now Playing: {0}", playback.DisplayName));
        }

        Plugin.PluginIpc.MidiBardPlayingFileNamePub.SendMessage(PlaylistManager.GetPostSongName(PlaylistManager.CurrentSongIndex));
        return playback;
    }

    private void Playback_Finished(object sender, EventArgs e)
    {
        Task.Run(() =>
        {
            try
            {
                if (Plugin.AgentMetronome.EnsembleModeRunning)
                {
                    // Set song as played for ensemble
                    PlaylistManager.SetCurrentSongAsPlayed();
                    return;
                }
                if (!PlaylistManager.FilePathList.Any())
                    return;
                if (Plugin.SlaveMode)
                    return;

                // Set song as played for solo
                PlaylistManager.SetCurrentSongAsPlayed();

                var fromSeconds = TimeSpan.FromSeconds(Plugin.Config.SecondsBetweenTracks);
                PerformWaiting(fromSeconds, ref waitProgress, ref waitStatus);
                if (waitStatus == Status.canceled) return;

                switch ((PlayMode)Plugin.Config.PlayMode)
                {
                    case PlayMode.Single:
                        break;
                    case PlayMode.SingleRepeat:
                        Plugin.CurrentBardPlayback?.MoveToTime(new MidiTimeSpan(0));
                        MidiPlayerControl.DoPlay();
                        break;
                    case PlayMode.ListOrdered when PlaylistManager.CurrentSongIndex >= PlaylistManager.FilePathList.Count - 1:
                        break;
                    case PlayMode.ListOrdered:
                    case PlayMode.ListRepeat:
                    case PlayMode.Random:
                        MidiPlayerControl.Next(true);
                        break;
                }
            }
            catch (Exception exception)
            {
                DalamudApi.PluginLog.Error(exception, "Unexpected exception when Playback finished.");
            }
        });
    }

    internal async Task<bool> LoadPlayback(string filePath)
    {
        MidiFile midiFile = await Task.Run(() => PlaylistManager.LoadSongFile(filePath));

        if (midiFile == null)
        {
            // delete file if can't be loaded(likely to be deleted locally)
            //DalamudApi.PluginLog.Debug($"[LoadPlayback] removing {index}");
            //DalamudApi.PluginLog.Debug($"[LoadPlayback] removing {PlaylistManager.FilePathList[index].path}");
            //PlaylistManager.RemoveSync(index);
            return false;
        }

        var playback = await Task.Run(() => GetPlaybackInstance(midiFile, filePath));
        Plugin.CurrentBardPlayback?.Dispose();
        Plugin.CurrentBardPlayback = playback;

        Plugin.BardPlayDevice.ResetChannelStates();
        // TODO: refactor sync track config flow should be executed here instead of inside WaitSwitchInstrumentForSong

        try
        {
            await SwitchInstrument.WaitSwitchInstrumentForSong(Path.GetFileNameWithoutExtension(filePath));
            Plugin.Ui.TrackVisualizerWindow.RefreshPlotData();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
        }
        finally
        {
            Lrc.InitLrc(filePath);
        }

        return true;
    }

    internal async Task<bool> LoadPlayback(string filename, Stream filePath)
    {
        MidiFile midiFile = await Task.Run(() => PlaylistManager.LoadMidiFile(filePath));

        if (midiFile == null)
        {
            // delete file if can't be loaded(likely to be deleted locally)
            //DalamudApi.PluginLog.Debug($"[LoadPlayback] removing {index}");
            //DalamudApi.PluginLog.Debug($"[LoadPlayback] removing {PlaylistManager.FilePathList[index].path}");
            //PlaylistManager.RemoveSync(index);
            return false;
        }

        var playback = await Task.Run(() => GetPlaybackInstance(midiFile, null));
        Plugin.CurrentBardPlayback?.Dispose();
        Plugin.CurrentBardPlayback = playback;

        Plugin.BardPlayDevice.ResetChannelStates();

        try
        {
            await SwitchInstrument.WaitSwitchInstrumentForSong(filename);
            Plugin.Ui.TrackVisualizerWindow.RefreshPlotData();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
        }

        return true;

    }

    internal enum Status
    {
        notWaiting,
        waiting,
        skipped,
        canceled,
    }

    internal void PerformWaiting(TimeSpan waitTime, ref float progress, ref Status status)
    {
        status = Status.waiting;
        progress = 0;
        var end = DateTime.UtcNow + waitTime;
        try
        {
            while (status == Status.waiting && progress < 1)
            {
                var remain = end - DateTime.UtcNow;
                progress = (float)(1 - remain / waitTime);
                Thread.Sleep(25);
                if (status != Status.waiting) return;
            }
        }
        finally
        {
            progress = 0;
        }

        status = Status.notWaiting;
    }
}
