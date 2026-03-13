using Dalamud.Bindings.ImGui;

using MidiBard.Resources;
using MidiBard.Extensions.General;
using MidiBard.Extensions.DryWetMidi;
using Dalamud.Interface.Utility;

using MidiBard.Control;
using MidiBard.Util;

namespace MidiBard;

public partial class MainWindow
{
    private static uint UIcurrentInstrument;

    private void RefreshUICurrentInstrument()
    {
        UIcurrentInstrument = PerformanceState.CurrentInstrument;
        if (PerformanceState.PlayingGuitar)
        {
            UIcurrentInstrument = (uint)(Plugin.AgentPerformance.CurrentGroupTone + InstrumentHelper.GuitarGroup[0]);
        }
    }

    // Rebuilt whenever Language.Culture changes so labels stay in sync with runtime language switches
    private static System.Globalization.CultureInfo? s_toneLabelsCulture;
    private static string[]? s_toneModeToolTips;
    private static string[]? s_toneModeLabels;

    private static void EnsureToneModeCacheValid()
    {
        if (s_toneLabelsCulture == Language.Culture) return;
        s_toneLabelsCulture = Language.Culture;
        s_toneModeToolTips =
        [
            Language.tone_mode_tooltip_off,
            Language.tone_mode_tooltip_standard,
            Language.tone_mode_tooltip_simple,
            Language.tone_mode_tooltip_override_by_track,
            Language.tone_mode_tooltip_program_electric_guitar_mode,
        ];
        s_toneModeLabels =
        [
            Language.tone_mode_option_off,
            Language.tone_mode_option_standard,
            Language.tone_mode_option_simple,
            Language.tone_mode_option_override_by_track,
            Language.tone_mode_option_program_electric_guitar_mode,
        ];
    }

    private void DrawMusicControlPanel()
    {
        if (Plugin.LyricsPlayer.LyricsLoaded())
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
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
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
            EnsureToneModeCacheValid();
            if (ImGuiUtil.EnumCombo(Language.setting_label_tone_mode, ref Plugin.Config.GuitarToneMode, labelsOverride: s_toneModeLabels, toolTips: s_toneModeToolTips))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_tone_mode);
        }

        if (Plugin.Config.UiShowPlaySpeed)
        {
            if (ImGui.InputFloat(Language.setting_label_set_play_speed, ref Plugin.Config.PlaySpeed, 0.1f, 0.5f, Plugin.CurrentBardPlayback?.GetBpmLabel(), ImGuiInputTextFlags.AutoSelectAll))
            {
                Plugin.Config.PlaySpeed = Plugin.Config.PlaySpeed.Clamp(0.1f, 10f);
                Plugin.CurrentBardPlayback.SetSpeed(Plugin.Config.PlaySpeed);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                Plugin.Config.PlaySpeed = 1;
                Plugin.CurrentBardPlayback.SetSpeed(Plugin.Config.PlaySpeed);
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_set_speed);
        }

        if (Plugin.Config.UiShowTransposeGlobal)
        {
            if (ImGui.InputInt(Language.setting_label_transpose_all, ref Plugin.Config.TransposeGlobal, 12))
            {
                ApplyTransposeGlobal(Plugin.Config.TransposeGlobal);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                ApplyTransposeGlobal(0);
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_transpose_all);
        }

        ImGui.BeginGroup();

        if (Plugin.Config.UiShowAdaptNotesOOR)
        {
            if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref Plugin.Config.AdaptNotesOOR))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_auto_adapt_notes);

            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20, 0);
            ImGui.SameLine();
        }

        if (Plugin.Config.UiShowAutoAlignMidi)
        {
            if (ImGui.Checkbox(Language.setting_label_auto_align_loaded_midi, ref Plugin.Config.AlignMidi))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_tooltip_auto_align_loaded_midi);
        }

        ImGui.EndGroup();
    }

    private void ApplyTransposeGlobal(int value)
    {
        Plugin.Config.SetTransposeGlobal(value, Plugin);
        Plugin.IpcProvider.GlobalTranspose(Plugin.Config.TransposeGlobal);
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
        ImGuiUtil.ToolTip("Delay time(ms) add on top of lyrics.");
    }
}
