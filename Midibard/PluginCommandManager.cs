using System;

using Dalamud.Game.Command;

using MidiBard.Control.CharacterControl;
using MidiBard.Util;

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

        if (parsedArgs.Count > 0)
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
                            Plugin.InstrumentSwitcher.SwitchToContinue(id1);
                        }
                        else if (Plugin.InstrumentSwitcher.TryParseInstrumentName(instrumentInput, out var id2))
                        {
                            Plugin.InstrumentSwitcher.SwitchToContinue(id2);
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
                    Plugin.IpcProvider.UpdateInstrument(false);
                    break;
                case "updateinstrument":
                    if (Plugin.CurrentBardPlayback?.MidiFileConfig is { } midiFileConfig)
                    {
                        Plugin.IpcProvider.UpdateMidiFileConfig(midiFileConfig);
                    }
                    break;
                case "switchto":
                    {
                        if (parsedArgs.Count < 2)
                            return;

                        if (int.TryParse(parsedArgs[1], out int songIndex) && Plugin.PlaylistManager.IsValidSongIndex(songIndex))
                        {
                            Plugin.MidiPlayerControl.StopLrc();
                            Plugin.PlaylistManager.LoadPlayback(songIndex - 1);
                        }
                        break;
                    }
                case "loadsong":
                    {
                        if (parsedArgs.Count < 2)
                            return;

                        int songIndex = Plugin.PlaylistManager.FindSongIndex(parsedArgs[1]);
                        if (songIndex >= 0)
                        {
                            Plugin.MidiPlayerControl.StopLrc();
                            Plugin.PlaylistManager.LoadPlayback(songIndex);
                        }
                        break;
                    }
                case "reloadplaylist":
                    Plugin.PlaylistManager.LoadLastPlaylist();
                    break;
                case "playpause":
                    Plugin.MidiPlayerControl.PlayPause();
                    break;
                case "play":
                    Plugin.MidiPlayerControl.Play();
                    break;
                case "pause":
                    Plugin.MidiPlayerControl.Pause();
                    break;
                case "stop":
                    Plugin.MidiPlayerControl.Stop();
                    break;
                case "next":
                    Plugin.MidiPlayerControl.Next();
                    break;
                case "prev":
                    Plugin.MidiPlayerControl.Prev();
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

                        Plugin.MidiPlayerControl.MoveTime(timeInSeconds);
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

                        Plugin.MidiPlayerControl.MoveTime(timeInSeconds);
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
