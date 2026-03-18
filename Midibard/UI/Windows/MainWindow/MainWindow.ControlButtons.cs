using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;
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

    private void DrawButtonPlayPause(bool ensembleRunning)
    {
        var ensembleStartMode = Plugin.Config.PlayButtonShowEnsembleStart && DalamudApi.PartyList.IsInParty();

        bool disabled;
        FontAwesomeIcon icon;
        if (ensembleStartMode)
        {
            disabled = ensembleRunning || !DalamudApi.PartyList.IsPartyLeader()
                       || !Plugin.CurrentBardPlayback.IsLoaded || Plugin.CurrentBardPlayback.IsRunning;
            icon = FontAwesomeIcon.UserCheck;
        }
        else
        {
            disabled = ensembleRunning;
            icon = Plugin.CurrentBardPlayback.IsRunning ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
        }

        using (ImRaii.Disabled(disabled))
        {
            if (ImGuiUtil.IconButton(icon, "##btnPlayPause", size: Style.Dimensions.ButtonLarge))
            {
                if (ensembleStartMode)
                {
                    if (Plugin.Config.UpdateInstrumentBeforeReadyCheck)
                    {
                        Plugin.EnsembleManager.BroadcastEquipInstruments();
                        Plugin.EnsembleManager.BeginEnsembleReadyCheck(Plugin.Config.PreReadyCheckDelayMs);
                    }
                    else
                    {
                        Plugin.EnsembleManager.BeginEnsembleReadyCheck();
                    }
                }
                else
                {
                    DalamudApi.PluginLog.Debug($"PlayPause pressed. was playing: {Plugin.CurrentBardPlayback.IsRunning}");
                    Plugin.MidiPlayerControl.PlayPause();
                }
            }
        }
        ImGui.SameLine();
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

            Plugin.EnsembleManager.BroadcastUnequipInstruments();
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

    private void DrawButtonPlayMode()
    {
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
            Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Plugin.Config.PlayMode = (Plugin.Config.PlayMode + s_playModeCount - 1) % s_playModeCount;
            Plugin.IpcProvider.SyncAllSettings();
        }
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
        ImGui.Separator();
        if (ImGui.Checkbox("Ensemble Panel", ref Plugin.Config.UiShowEnsemblePanel))
            Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Ensemble Start mode", ref Plugin.Config.PlayButtonShowEnsembleStart))
            Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawButtonShowEnsembleWindow(bool disabled)
    {
        ImGui.BeginDisabled(disabled);
        ImGui.SameLine();

        if (Plugin.Config.UiShowEnsemblePanel)
        {
            Vector4? btnColor = _ensemblePanelVisible ? Plugin.Config.themeColor : null;
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Users, "##btnEnsemble", color: btnColor, size: Style.Dimensions.ButtonLarge))
                _ensemblePanelVisible ^= true;
        }
        else
        {
            Vector4? btnColor = Plugin.Ui.EnsembleWindow.IsOpen ? Plugin.Config.themeColor : null;
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Users, "##btnEnsemble", color: btnColor, size: Style.Dimensions.ButtonLarge))
                Plugin.Ui.EnsembleWindow.Toggle();
        }

        ImGui.EndDisabled();
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_ensemble_panel);
    }
}
