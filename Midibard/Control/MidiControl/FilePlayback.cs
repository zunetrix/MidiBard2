using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Playlist.Services;

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
        var playback = Plugin.CurrentBardPlayback.CreatePlayback(midifile, path);

        playback.Speed = Plugin.Config.PlaySpeed;
        playback.Finished += Playback_Finished;

        DalamudApi.PluginLog.Debug($"[LoadPlayback] -> {path} OK! in {stopwatch.Elapsed.TotalMilliseconds} ms");

        if (Plugin.Config.showNowPlayingInfo)
        {
            DalamudApi.ChatGui.Print($"[MidiBard 2] Now Playing: {playback.DisplayName}");
        }

        // Send IPC message with filename and duration
        var songName = Plugin.PlaylistManager.GetPostSongName(Plugin.PlaylistManager.CurrentSongIndex);
        var totalDuration = playback.GetDuration<MetricTimeSpan>();
        var totalDurationFormated = $"{totalDuration.Hours}:{totalDuration.Minutes:00}:{totalDuration.Seconds:00}";
        Plugin.PluginIpc.MidiBardPlayingInfoPub.SendMessage((songName, totalDurationFormated));
        Plugin.PluginIpc.MidiBardPlayingFileNamePub.SendMessage(songName);

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
                    Plugin.PlaylistManager.SetCurrentSongAsPlayed();
                    return;
                }
                if (!Plugin.PlaylistManager.FilePathList.Any())
                    return;
                if (Plugin.SlaveMode)
                    return;

                // Set song as played for solo
                Plugin.PlaylistManager.SetCurrentSongAsPlayed();

                var fromSeconds = TimeSpan.FromSeconds(Plugin.Config.SecondsBetweenTracks);
                PerformWaiting(fromSeconds, ref waitProgress, ref waitStatus);
                if (waitStatus == Status.canceled) return;

                switch ((PlayMode)Plugin.Config.PlayMode)
                {
                    case PlayMode.Single:
                        break;
                    case PlayMode.SingleRepeat:
                        Plugin.CurrentBardPlayback?.MoveToTime(new MidiTimeSpan(0));
                        Plugin.MidiPlayerControl.DoPlay();
                        break;
                    case PlayMode.ListOrdered when Plugin.PlaylistManager.CurrentSongIndex >= Plugin.PlaylistManager.FilePathList.Count - 1:
                        break;
                    case PlayMode.ListOrdered:
                    case PlayMode.ListRepeat:
                    case PlayMode.Random:
                        Plugin.MidiPlayerControl.Next(true);
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
        var midiFileService = ServiceContainer.GetService<IMidiFileService>();
        MidiFile midiFile = await Task.Run(() => midiFileService.LoadMidiFile(filePath));

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
            await Plugin.InstrumentSwitcher.WaitSwitchInstrumentForSong(Path.GetFileNameWithoutExtension(filePath));
            Plugin.Ui.TrackVisualizerWindow.RefreshPlotData();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e.ToString());
        }
        finally
        {
            Plugin.LyricsPlayer.LoadLyrics(filePath);
        }

        return true;
    }

    internal async Task<bool> LoadPlayback(string filename, Stream filePath)
    {
        var midiFileService = ServiceContainer.GetService<IMidiFileService>();
        MidiFile midiFile = await Task.Run(() => midiFileService.LoadMidiFile(filePath));

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
            await Plugin.InstrumentSwitcher.WaitSwitchInstrumentForSong(filename);
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
