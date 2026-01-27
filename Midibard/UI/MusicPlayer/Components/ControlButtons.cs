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
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;

using MidiBard.Resources;



namespace MidiBard;

public partial class PluginUI
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
        var PlayPauseIcon = Plugin.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        if (ImGuiUtil.IconButton(PlayPauseIcon, "##btnPlayPause"))
        {
            DalamudApi.PluginLog.Debug($"PlayPause pressed. was playing: {Plugin.IsPlaying}");
            MidiPlayerControl.PlayPause();
        }
        ImGui.SameLine();
        ImGui.EndDisabled();
    }

    private void DrawButtonStop()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##btnStop", "Stop"))
        {
            if (FilePlayback.IsWaiting)
            {
                FilePlayback.CancelWaiting();
            }
            else
            {
                MidiPlayerControl.Stop();
            }

            StopEnsemble();
        }
    }

    private void DrawButtonFastForward(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FastForward, "##btnFastForward", "Fast forward"))
        {
            MidiPlayerControl.Next();
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiPlayerControl.Prev();
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

        if (ImGuiUtil.IconButton(icon, "##btnPlayMode"))
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
        Vector4? btnColor = Plugin.Ui.showSettingsWindow ? Plugin.Config.themeColor : null;

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Cog, "##btnSettingsWindow", color: btnColor))
        {
            Plugin.Ui.ToggleSettingsWindow();
        }
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_settings_panel);
    }

    private void DrawButtonVisualization()
    {
        ImGui.SameLine();
        Vector4? color = Plugin.Ui.showTrackVisualizerWindow ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Film, "##btnTrackVisualizerToggle", Language.icon_button_tooltip_visualization, color))
        {
            showTrackVisualizerWindow ^= true;
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _resetPlotWindowPosition = true;
        }
    }

    private void DrawButtonShowEnsembleWindow(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        Vector4? btnColor = Plugin.Ui.ShowEnsembleWindow ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Users, "##btnEnsemble", color: btnColor))
        {
            ShowEnsembleWindow ^= true;
        }
        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_ensemble_panel);
    }

    private static void StopEnsemble()
    {
        if (Plugin.Config.playOnMultipleDevices && DalamudApi.PartyList.Length > 1)
        {
            PartyChatCommand.SendClose();
        }
        else if (DalamudApi.PartyList.Length <= 1)
        {
            SwitchInstrument.SwitchToContinue(0);
            MidiPlayerControl.Stop();
            return;
        }
        else
        {
            IPCHandles.UpdateInstrument(false);
        }
    }
}
