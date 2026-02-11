using System.Numerics;

using Dalamud.Bindings.ImGui;

using MidiBard.Resources;
using MidiBard.Extensions.General;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public partial class MainWindow
{
    private static uint UIcurrentInstrument;

    //TODO: refactor into instrument helper?
    private static string[] GetToneModeToolTips()
    {
        string[] toneModeToolTips = [
             Language.tone_mode_tooltip_off,
             Language.tone_mode_tooltip_standard,
             Language.tone_mode_tooltip_simple,
             Language.tone_mode_tooltip_override_by_track,
             Language.tone_mode_tooltip_program_electric_guitar_mode,
        ];

        return toneModeToolTips;
    }

    //TODO: refactor into instrument helper?
    private static string[] GetToneModeLabels()
    {
        string[] toneModeLabels = [
                Language.tone_mode_option_off,
                Language.tone_mode_option_standard,
                Language.tone_mode_option_simple,
                Language.tone_mode_option_override_by_track,
                Language.tone_mode_option_program_electric_guitar_mode,
            ];

        return toneModeLabels;
    }

    private void DrawMusicControlPanel()
    {
        //ManualDelay();
        if (Plugin.LyricsPlayer.LrcLoaded())
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
                    Plugin.InputDeviceManager.SetDevice(null);
                }

                for (int i = 0; i < inputDevices.Length; i++)
                {
                    var device = inputDevices[i];
                    if (ImGui.Selectable($"{device.Name}##{i}", device.Name == InputDeviceManager.CurrentInputDevice?.Name))
                    {
                        Plugin.InputDeviceManager.SetDevice(device);
                    }
                }

                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                Plugin.InputDeviceManager.SetDevice(null);
            }
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
                Plugin.IpcProvider.SyncAllSettings();
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
            if (ImGui.InputFloat(Language.setting_label_set_play_speed, ref Plugin.Config.PlaySpeed, 0.1f, 0.5f, Plugin.CurrentBardPlayback?.GetBpmLabel(), ImGuiInputTextFlags.AutoSelectAll))
            {
                Plugin.Config.PlaySpeed = Plugin.Config.PlaySpeed.Clamp(0.1f, 10f);
                Plugin.CurrentBardPlayback.SetSpeed(Plugin.Config.PlaySpeed);
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                Plugin.Config.PlaySpeed = 1;
                Plugin.CurrentBardPlayback.SetSpeed(Plugin.Config.PlaySpeed);
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_set_speed);
            // ImGui.PopItemWidth();
        }

        if (Plugin.Config.UiShowTransposeGlobal)
        {
            if (ImGui.InputInt(Language.setting_label_transpose_all, ref Plugin.Config.TransposeGlobal, 12))
            {
                // TODO: find better way to set plugin dependency
                Plugin.Config.SetTransposeGlobal(Plugin.Config.TransposeGlobal, Plugin);
                Plugin.IpcProvider.GlobalTranspose(Plugin.Config.TransposeGlobal);
            }
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                // TODO: find better way to set plugin dependency
                Plugin.Config.SetTransposeGlobal(0, Plugin);
                Plugin.IpcProvider.GlobalTranspose(Plugin.Config.TransposeGlobal);
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_transpose_all);
        }

        ImGui.BeginGroup();

        //-------------------

        if (Plugin.Config.UiShowAdaptNotesOOR)
        {
            if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref Plugin.Config.AdaptNotesOOR))
            {
                Plugin.IpcProvider.SyncAllSettings();
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
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_auto_align_loaded_midi);
        }

        ImGui.EndGroup();

        // SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        // SetNextItemWidth(itemWidth);
    }

    private void ManualDelay()
    {
        if (ImGui.Button("-10ms"))
        {
            Plugin.MidiPlayerControl.ChangeDeltaTime(-10);
        }
        ImGui.SameLine();
        if (ImGui.Button("-2ms"))
        {
            Plugin.MidiPlayerControl.ChangeDeltaTime(-2);
        }
        ImGui.SameLine();
        if (ImGui.Button("+2ms"))
        {
            Plugin.MidiPlayerControl.ChangeDeltaTime(2);
        }
        ImGui.SameLine();
        if (ImGui.Button("+10ms"))
        {
            Plugin.MidiPlayerControl.ChangeDeltaTime(10);
        }
        ImGui.SameLine();
        ImGui.Text("Manual Sync: " + $"{Plugin.MidiPlayerControl.playDeltaTime} ms");
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Plugin.MidiPlayerControl.ChangeDeltaTime(-Plugin.MidiPlayerControl.playDeltaTime);
        }
        ImGuiUtil.ToolTip("Delay time(ms) add on top of current progress to help sync between bards.");
    }

    private void LRCDeltaTime()
    {
        if (ImGui.Button("-50ms"))
        {
            Plugin.LyricsPlayer.ChangeLRCDeltaTime(-50);
        }
        ImGui.SameLine();
        if (ImGui.Button("+50ms"))
        {
            Plugin.LyricsPlayer.ChangeLRCDeltaTime(50);
        }
        ImGui.SameLine();
        ImGui.Text("LRC Sync: " + $"{Plugin.LyricsPlayer.LRCDeltaTime} ms");
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Plugin.LyricsPlayer.ChangeLRCDeltaTime(-Plugin.LyricsPlayer.LRCDeltaTime);
        }
        ImGuiUtil.ToolTip("Delay time(ms) add on top of lyrics.");
    }

}
