using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Resources;

namespace MidiBard;

public partial class MainWindow
{
    private static string GetPlayModeLabel(int labelIndex)
    {
        string[] playModeOptionsLabels = {
            Language.play_mode_single,
            Language.play_mode_single_repeat,
            Language.play_mode_list_ordered,
            Language.play_mode_list_repeat,
            Language.play_mode_random,
        };

        if (labelIndex < playModeOptionsLabels.Length)
        {
            return playModeOptionsLabels[labelIndex];
        }

        return string.Empty;
    }

    private void DrawButtonPlayPause(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        var PlayPauseIcon = Plugin.CurrentBardPlayback.IsRunning ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        if (ImGuiUtil.IconButton(PlayPauseIcon, "##btnPlayPause", size: Style.Dimensions.PlayerButton))
        {
            DalamudApi.PluginLog.Debug($"PlayPause pressed. was playing: {Plugin.CurrentBardPlayback.IsRunning}");
            Plugin.MidiPlayerControl.PlayPause();
        }
        ImGui.SameLine();
        ImGui.EndDisabled();
    }

    private void DrawButtonStop()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##btnStop", "Stop", size: Style.Dimensions.PlayerButton))
        {
            if (Plugin.FilePlayback.IsWaiting)
            {
                Plugin.FilePlayback.CancelWaiting();
            }
            else
            {
                Plugin.MidiPlayerControl.Stop();
            }

            StopEnsemble();
        }
    }

    private void DrawButtonFastForward(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FastForward, "##btnFastForward", "Fast forward", size: Style.Dimensions.PlayerButton))
        {
            Plugin.MidiPlayerControl.Next();
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Plugin.MidiPlayerControl.Prev();
        }
        ImGui.EndDisabled();
    }

    private void DrawButtonPlayMode(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        FontAwesomeIcon icon = (PlayMode)Plugin.Config.PlayMode switch
        {
            PlayMode.Single => FontAwesomeIcon.Reply,
            PlayMode.ListOrdered => FontAwesomeIcon.SortAmountDownAlt,
            PlayMode.ListRepeat => FontAwesomeIcon.Sync,
            PlayMode.SingleRepeat => FontAwesomeIcon.Redo,
            PlayMode.Random => FontAwesomeIcon.Random,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (ImGuiUtil.IconButton(icon, "##btnPlayMode", size: Style.Dimensions.PlayerButton))
        {
            Plugin.Config.PlayMode += 1;
            Plugin.Config.PlayMode %= 5;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Plugin.Config.PlayMode += 4;
            Plugin.Config.PlayMode %= 5;
        }
        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(GetPlayModeLabel(Plugin.Config.PlayMode));
    }

    private void DrawButtonShowSettingsWindow()
    {
        ImGui.SameLine();
        Vector4? btnColor = Plugin.Ui.SettingsWindow.IsOpen ? Plugin.Config.themeColor : null;

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Cog, "##btnSettingsWindow", color: btnColor, size: Style.Dimensions.PlayerButton))
        {
            Plugin.Ui.SettingsWindow.Toggle();
        }
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_settings_panel);
    }

    private void DrawButtonVisualization()
    {
        ImGui.SameLine();
        Vector4? color = Plugin.Ui.TrackVisualizerWindow.IsOpen ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Film, "##btnTrackVisualizerToggle", Language.icon_button_tooltip_visualization, color, size: Style.Dimensions.PlayerButton))
        {
            Plugin.Ui.TrackVisualizerWindow.Toggle();
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Plugin.Ui.TrackVisualizerWindow.ResetPosition();
        }
    }

    private void DrawButtonPianoRollVisualization()
    {
        ImGui.SameLine();
        Vector4? color = Plugin.Ui.PianoRollWindow.IsOpen ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.LayerGroup, "##btnPianoRollVisualizerToggle", "Piano Roll View", color, size: Style.Dimensions.PlayerButton))
        {
            Plugin.Ui.PianoRollWindow.Toggle();
        }
    }

    private void DrawButtonShowEnsembleWindow(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        Vector4? btnColor = Plugin.Ui.EnsembleWindow.IsOpen ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Users, "##btnEnsemble", color: btnColor, size: Style.Dimensions.PlayerButton))
        {
            Plugin.Ui.EnsembleWindow.Toggle();
        }
        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_ensemble_panel);
    }

    private void StopEnsemble()
    {
        if (Plugin.Config.playOnMultipleDevices && DalamudApi.PartyList.Length > 1)
        {
            Plugin.PartyChatCommand.SendClose();
        }
        else if (DalamudApi.PartyList.Length <= 1)
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(0);
            Plugin.MidiPlayerControl.Stop();
            return;
        }
        else
        {
            Plugin.IpcProvider.UpdateInstrument(false);
        }
    }
}
