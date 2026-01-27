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

using System.Numerics;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using MidiBard.Resources;

namespace MidiBard;

public partial class PluginUI
{
    private static uint UIcurrentInstrument;

    private void DrawMusicControlPanel()
    {
        //ManualDelay();
        if (Lrc.LrcLoaded())
        {
            LRCDeltaTime();
        }

        var inputDevices = InputDeviceManager.Devices;
        if (inputDevices.Length > 0)
        {
            if (ImGui.BeginCombo(Language.setting_label_midi_input_device, InputDeviceManager.CurrentInputDevice.DeviceName()))
            {
                if (ImGui.Selectable("None##device", InputDeviceManager.CurrentInputDevice is null))
                {
                    InputDeviceManager.SetDevice(null);
                }

                for (int i = 0; i < inputDevices.Length; i++)
                {
                    var device = inputDevices[i];
                    if (ImGui.Selectable($"{device.Name}##{i}", device.Name == InputDeviceManager.CurrentInputDevice?.Name))
                    {
                        InputDeviceManager.SetDevice(device);
                    }
                }

                ImGui.EndCombo();
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) InputDeviceManager.SetDevice(null);
            ImGuiUtil.ToolTip(Language.setting_tooltip_select_input_device);
        }

        //-------------------

        // InstrumentComboBox();

        //-------------------

        // SliderProgressBar();

        //-------------------

        if (Plugin.Config.UiShowGuitarToneMode)
        {
            if (ImGuiUtil.EnumCombo(Language.setting_label_tone_mode, ref Plugin.Config.GuitarToneMode, labelsOverride: GetToneModeLabels(), toolTips: GetToneModeToolTips()))
            {
                IPC.IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_tone_mode);
        }

        //-------------------

        // ImGui.BeginGroup();
        // float totalWidth = ImGui.GetContentRegionAvail().X;
        // float spacing = ImGui.GetStyle().ItemSpacing.X;
        // float inputWidth = (totalWidth - spacing) / 3f;
        if (Plugin.Config.UiShowPlaySpeed)
        {
            // ImGui.PushItemWidth(inputWidth);
            if (ImGui.InputFloat(Language.setting_label_set_play_speed, ref Plugin.Config.PlaySpeed, 0.1f, 0.5f, GetBpmString(), ImGuiInputTextFlags.AutoSelectAll))
            {
                SetSpeed();
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                Plugin.Config.PlaySpeed = 1;
                SetSpeed();
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_set_speed);
            // ImGui.PopItemWidth();
        }

        if (Plugin.Config.UiShowTransposeGlobal)
        {
            if (ImGui.InputInt(Language.setting_label_transpose_all, ref Plugin.Config.TransposeGlobal, 12))
            {
                Plugin.Config.SetTransposeGlobal(Plugin.Config.TransposeGlobal);
                IPC.IPCHandles.GlobalTranspose(Plugin.Config.TransposeGlobal);
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                Plugin.Config.SetTransposeGlobal(0);
                IPC.IPCHandles.GlobalTranspose(Plugin.Config.TransposeGlobal);
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_transpose_all);
        }

        ImGui.BeginGroup();

        //-------------------

        if (Plugin.Config.UiShowAdaptNotesOOR)
        {
            if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref Plugin.Config.AdaptNotesOOR))
            {
                IPC.IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_auto_adapt_notes);

            ImGui.SameLine();
            ImGui.Dummy(new Vector2(20, 0));
            ImGui.SameLine();
        }

        //-------------------

        if (Plugin.Config.UiShowAutoAlignMidi)
        {
            if (ImGui.Checkbox(Language.setting_label_auto_align_loaded_midi, ref Plugin.Config.AlignMidi))
            {
                IPC.IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_auto_align_loaded_midi);
        }

        ImGui.EndGroup();

        // SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        // SetNextItemWidth(itemWidth);
    }

    private static void SetSpeed()
    {
        Plugin.Config.PlaySpeed = Plugin.Config.PlaySpeed.Clamp(0.1f, 10f);
        var currenttime = Plugin.CurrentBardPlayback?.GetCurrentTime(TimeSpanType.Midi);
        if (currenttime is not null)
        {
            Plugin.CurrentBardPlayback.Speed = Plugin.Config.PlaySpeed;
            Plugin.CurrentBardPlayback?.MoveToTime(currenttime);
        }

        if (DalamudApi.PartyList.IsPartyLeader())
            IPC.IPCHandles.PlaybackSpeed(Plugin.Config.PlaySpeed);
    }

    private static string GetBpmString()
    {
        Tempo bpm = null;
        var currentTime = Plugin.CurrentBardPlayback?.GetCurrentTime(TimeSpanType.Midi);
        if (currentTime != null)
        {
            bpm = Plugin.CurrentBardPlayback?.TempoMap?.GetTempoAtTime(currentTime);
        }

        var label = $" {Plugin.Config.PlaySpeed:F2}";

        if (bpm != null) label += $" ({bpm.BeatsPerMinute * Plugin.Config.PlaySpeed:F1} bpm)";
        return label;
    }

    private static void ManualDelay()
    {
        if (ImGui.Button("-10ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(-10);
        }
        ImGui.SameLine();
        if (ImGui.Button("-2ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(-2);
        }
        ImGui.SameLine();
        if (ImGui.Button("+2ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(2);
        }
        ImGui.SameLine();
        if (ImGui.Button("+10ms"))
        {
            MidiPlayerControl.ChangeDeltaTime(10);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Manual Sync: " + $"{MidiPlayerControl.playDeltaTime} ms");
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiPlayerControl.ChangeDeltaTime(-MidiPlayerControl.playDeltaTime);
        }
        ImGuiUtil.ToolTip("Delay time(ms) add on top of current progress to help sync between bards.");
    }

    private static void LRCDeltaTime()
    {
        if (ImGui.Button("-50ms"))
        {
            Lrc.ChangeLRCDeltaTime(-50);
        }
        ImGui.SameLine();
        if (ImGui.Button("+50ms"))
        {
            Lrc.ChangeLRCDeltaTime(50);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("LRC Sync: " + $"{Lrc.LRCDeltaTime} ms");
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Lrc.ChangeLRCDeltaTime(-Lrc.LRCDeltaTime);
        }
        ImGuiUtil.ToolTip("Delay time(ms) add on top of lyrics.");
    }

}
