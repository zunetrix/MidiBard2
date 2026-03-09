using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;

namespace MidiBard;

public partial class MainWindow
{
    private static readonly int s_playModeCount = Enum.GetValues<PlayMode>().Length;

    // Read Language.* directly each call so label updates when culture changes.
    private static string GetPlayModeLabel(int index) => index switch
    {
        (int)PlayMode.Single => Language.play_mode_single,
        (int)PlayMode.SingleRepeat => Language.play_mode_single_repeat,
        (int)PlayMode.ListOrdered => Language.play_mode_list_ordered,
        (int)PlayMode.ListRepeat => Language.play_mode_list_repeat,
        (int)PlayMode.Random => Language.play_mode_random,
        _ => string.Empty,
    };

    private void DrawButtonPlayPause(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        var PlayPauseIcon = Plugin.CurrentBardPlayback.IsRunning ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        if (ImGuiUtil.IconButton(PlayPauseIcon, "##btnPlayPause", size: Style.Dimensions.ButtonLarge))
        {
            DalamudApi.PluginLog.Debug($"PlayPause pressed. was playing: {Plugin.CurrentBardPlayback.IsRunning}");
            Plugin.MidiPlayerControl.PlayPause();
        }
        ImGui.SameLine();
        ImGui.EndDisabled();
    }

    private void DrawButtonStop()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##btnStop", "Stop", size: Style.Dimensions.ButtonLarge))
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
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FastForward, "##btnFastForward", "Fast forward", size: Style.Dimensions.ButtonLarge))
        {
            Plugin.MidiPlayerControl.Next();
        }
        if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
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
            _ => FontAwesomeIcon.Times
        };

        if (ImGuiUtil.IconButton(icon, "##btnPlayMode", size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Config.PlayMode = (Plugin.Config.PlayMode + 1) % s_playModeCount;
        }

        if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Plugin.Config.PlayMode = (Plugin.Config.PlayMode + s_playModeCount - 1) % s_playModeCount;
        }
        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(GetPlayModeLabel(Plugin.Config.PlayMode));
    }

    private void DrawButtonShowSettingsWindow()
    {
        ImGui.SameLine();
        Vector4? btnColor = Plugin.Ui.SettingsWindow.IsOpen ? Plugin.Config.themeColor : null;

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Cog, "##btnSettingsWindow", color: btnColor, size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Ui.SettingsWindow.Toggle();
        }
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_settings_panel);
    }


    private void DrawButtonPianoRollVisualization()
    {
        ImGui.SameLine();
        Vector4? color = Plugin.Ui.PianoRollWindow.IsOpen ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Film, "##btnPianoRollVisualizerToggle", Language.icon_button_tooltip_visualization, color, size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Ui.PianoRollWindow.Toggle();
        }
    }

    private void DrawButtonShowElements()
    {
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.LayerGroup, "##btnShowTrackSelection", "Show/Hide Elements", size: Style.Dimensions.ButtonLarge))
        {
            ImGui.OpenPopup("ShowHideControlElementsPopup");
        }

        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("ShowHideControlElementsPopup");
        if (!popUp) return;

        ImGui.Text(Language.setting_label_show_hide_in_main_window);
        ImGui.Separator();
        if (ImGui.Checkbox("Track Selection", ref Plugin.Config.ShowTrackSelection))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.Checkbox(Language.setting_label_auto_align_loaded_midi, ref Plugin.Config.UiShowAutoAlignMidi))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref Plugin.Config.UiShowAdaptNotesOOR))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.Checkbox(Language.setting_label_tone_mode, ref Plugin.Config.UiShowGuitarToneMode))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.Checkbox(Language.setting_label_set_play_speed, ref Plugin.Config.UiShowPlaySpeed))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.Checkbox(Language.setting_label_global_transpose, ref Plugin.Config.UiShowTransposeGlobal))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.Checkbox("Show Ads Links", ref Plugin.Config.UiShowAdsLinks))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
    }

    private void DrawButtonShowEnsembleWindow(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();
        Vector4? btnColor = Plugin.Ui.EnsembleWindow.IsOpen ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Users, "##btnEnsemble", color: btnColor, size: Style.Dimensions.ButtonLarge))
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
            Plugin.ChatWatcher.SendClose();
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
