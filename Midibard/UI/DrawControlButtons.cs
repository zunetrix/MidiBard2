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

using Dalamud.Interface;

using ImGuiNET;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;

using MidiBard2.Resources;

using static Dalamud.api;
using static MidiBard.ImGuiUtil;

namespace MidiBard;

public partial class PluginUI
{
    private unsafe void DrawButtonVisualization()
    {
        ImGui.SameLine();
        var color = trackVisualizerWindowOpen ? MidiBard.config.themeColor : *ImGui.GetStyleColorVec4(ImGuiCol.Text);
        if (IconButton(FontAwesomeIcon.Film, "btnTrackVisualizerToggle", Language.icon_button_tooltip_visualization,
                ImGui.ColorConvertFloat4ToU32(color)))
        {
            trackVisualizerWindowOpen ^= true;
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _resetPlotWindowPosition = true;
        }
    }

    private unsafe void DrawButtonShowSettingsWindow()
    {
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, MidiBard.Ui.settingsWindowOpen ? MidiBard.config.themeColor : *ImGui.GetStyleColorVec4(ImGuiCol.Text));

        if (IconButton(FontAwesomeIcon.Cog, "btnsettingp"))
        {
            MidiBard.Ui.ToggleSettingsWindow();
        }

        ImGui.PopStyleColor();
        ToolTip(Language.icon_button_tooltip_settings_panel);
    }

    private unsafe void DrawButtonShowEnsembleControl(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, MidiBard.Ui.ShowEnsembleControlWindow ? MidiBard.config.themeColor : *ImGui.GetStyleColorVec4(ImGuiCol.Text));

        if (IconButton(FontAwesomeIcon.Users, "btnensemble"))
        {
            ShowEnsembleControlWindow ^= true;
        }
        ImGui.PopStyleColor();
        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_ensemble_panel);
    }

    private unsafe void DrawButtonPlayPause(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        var PlayPauseIcon = MidiBard.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        if (ImGuiUtil.IconButton(PlayPauseIcon, "btnPlayPause"))
        {
            PluginLog.Debug($"PlayPause pressed. was playing: {MidiBard.IsPlaying}");
            MidiPlayerControl.PlayPause();
        }
        ImGui.SameLine();
        ImGui.EndDisabled();
    }

    private unsafe void DrawButtonStop()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "btnStop", "Stop"))
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

    private unsafe void DrawButtonFastForward(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.FastForward, "btnff", "Fast forward"))
        {
            MidiPlayerControl.Next();
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiPlayerControl.Prev();
        }
        ImGui.EndDisabled();
    }

    private unsafe void DrawButtonPlayMode(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        FontAwesomeIcon icon = (PlayMode)MidiBard.config.PlayMode switch
        {
            PlayMode.Single => FontAwesomeIcon.Reply,
            PlayMode.ListOrdered => FontAwesomeIcon.SortAmountDownAlt,
            PlayMode.ListRepeat => FontAwesomeIcon.Sync,
            PlayMode.SingleRepeat => FontAwesomeIcon.Redo,
            PlayMode.Random => FontAwesomeIcon.Random,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (IconButton(icon, "btnpmode"))
        {
            MidiBard.config.PlayMode += 1;
            MidiBard.config.PlayMode %= 5;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.PlayMode += 4;
            MidiBard.config.PlayMode %= 5;
        }
        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(playModeOptions[MidiBard.config.PlayMode]);
    }

    readonly string[] playModeOptions = new string[]
    {
        Language.play_mode_single,
        Language.play_mode_single_repeat,
        Language.play_mode_list_ordered,
        Language.play_mode_list_repeat,
        Language.play_mode_random,
    };

    private static void StopEnsemble()
    {
        if (MidiBard.config.playOnMultipleDevices && api.PartyList.Length > 1)
        {
            PartyChatCommand.SendClose();
        }
        else if (api.PartyList.Length <= 1)
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
