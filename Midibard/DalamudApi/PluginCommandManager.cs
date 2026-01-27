using System;
using System.Linq;

using Dalamud.Game.Command;

using MidiBard.Control.CharacterControl;
using MidiBard.Util;
using MidiBard.IPC;
using MidiBard.Control.MidiControl;

namespace MidiBard;

public class PluginCommandManager : IDisposable
{
    private Plugin Plugin { get; }

    public PluginCommandManager(Plugin plugin)
    {
        Plugin = plugin;

        DalamudApi.CommandManager.AddHandler("/midibard", new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Toggle MidiBard window",
        });

        DalamudApi.CommandManager.AddHandler("/mbard", new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Toggle MidiBard window",
        });
    }

    private void OnMainCommand(string command, string args)
    {
        var parsedArgs = ArgumentParser.ParseChatArgs(args);
        // api.DalamudApi.PluginLog.Warning($"command: [{command}] {string.Join('|', parsedArgs)}");

        if (parsedArgs.Any())
        {
            var subcommand = parsedArgs[0];
            switch (subcommand)
            {
                case "cancel":
                    PerformActions.DoPerformActionOnTick(0);
                    break;
                case "perform":
                    try
                    {
                        var instrumentInput = parsedArgs[1];
                        if (instrumentInput == "cancel")
                        {
                            PerformActions.DoPerformActionOnTick(0);
                        }
                        else if (uint.TryParse(instrumentInput, out var id1) && id1 < Plugin.InstrumentStrings.Length)
                        {
                            SwitchInstrument.SwitchToContinue(id1);
                        }
                        else if (SwitchInstrument.TryParseInstrumentName(instrumentInput, out var id2))
                        {
                            SwitchInstrument.SwitchToContinue(id2);
                        }
                    }
                    catch (Exception e)
                    {
                        DalamudApi.PluginLog.Warning(e, "error when parsing or finding instrument strings");
                        DalamudApi.ChatGui.PrintError($"failed parsing command argument \"{args}\"");
                    }

                    break;

                case "startensemble":
                    Plugin.EnsembleManager.BeginEnsembleReadyCheck();
                    break;
                case "stopensemble":
                    IPCHandles.UpdateInstrument(false);
                    break;
                case "updateinstrument":
                    if (Plugin.CurrentBardPlayback?.MidiFileConfig is { } midiFileConfig)
                    {
                        IPCHandles.UpdateMidiFileConfig(midiFileConfig);
                    }
                    break;
                case "switchto":
                    {
                        if (parsedArgs.Count < 2)
                            return;

                        if (int.TryParse(parsedArgs[1], out int songIndex) && PlaylistManager.IsValidSongIndex(songIndex))
                        {
                            MidiPlayerControl.StopLrc();
                            PlaylistManager.LoadPlayback(songIndex - 1);
                        }
                        break;
                    }
                case "loadsong":
                    {
                        if (parsedArgs.Count < 2)
                            return;

                        int songIndex = PlaylistManager.FindSongIndex(parsedArgs[1]);
                        if (songIndex >= 0)
                        {
                            MidiPlayerControl.StopLrc();
                            PlaylistManager.LoadPlayback(songIndex);
                        }
                        break;
                    }
                case "reloadplaylist":
                    PlaylistManager.CurrentContainer = PlaylistManager.LoadLastPlaylist();
                    break;
                case "playpause":
                    MidiPlayerControl.PlayPause();
                    break;
                case "play":
                    MidiPlayerControl.Play();
                    break;
                case "pause":
                    MidiPlayerControl.Pause();
                    break;
                case "stop":
                    MidiPlayerControl.Stop();
                    break;
                case "next":
                    MidiPlayerControl.Next();
                    break;
                case "prev":
                    MidiPlayerControl.Prev();
                    break;
                case "visual":
                    switch (parsedArgs[1])
                    {
                        case "on":
                            Plugin.Ui.TrackVisualizerWindow.IsOpen = true;
                            break;
                        case "off":
                            Plugin.Ui.TrackVisualizerWindow.IsOpen = false;
                            break;
                        default:
                            break;
                    }
                    break;
                case "rewind":
                    {
                        double timeInSeconds = -5;
                        try
                        {
                            // TODO: implement safe parse like player time slider time span
                            timeInSeconds = -double.Parse(parsedArgs[1]);
                        }
                        catch
                        {
                            // ignored
                        }

                        MidiPlayerControl.MoveTime(timeInSeconds);
                    }
                    break;
                case "fastforward":
                    {
                        double timeInSeconds = 5;
                        try
                        {
                            timeInSeconds = double.Parse(parsedArgs[1]);
                        }
                        catch
                        {
                            // ignored
                        }

                        MidiPlayerControl.MoveTime(timeInSeconds);
                    }
                    break;
                case "transpose":
                    {
                        try
                        {
                            if (parsedArgs[1] == "set")
                            {
                                Plugin.Config.TransposeGlobal = int.Parse(parsedArgs[2]);
                            }
                            else
                            {
                                Plugin.Config.TransposeGlobal += int.Parse(parsedArgs[1]);
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                    break;
            }
        }
        else
        {
            Plugin.Ui.MainWindow.Toggle();
        }
    }

    public void Dispose()
    {
        DalamudApi.CommandManager.RemoveHandler("/midibard");
        DalamudApi.CommandManager.RemoveHandler("/mbard");
    }
}
