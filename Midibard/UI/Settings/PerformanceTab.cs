using System;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Interface;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Util;

using static MidiBard2.Resources.Language;

namespace MidiBard;

public partial class PluginUI
{
    private readonly string[] toneModeToolTips = {
        "Off: Does not take over game's guitar tone control.",
        "Standard: Standard midi channel and ProgramChange handling, each channel will keep it's program state separately.",
        "Simple: Simple ProgramChange handling, ProgramChange event on any channel will change all channels' program state. (This is BardMusicPlayer's default behavior.)",
        "Override by track: Assign guitar tone manually for each track and ignore ProgramChange events.",
    };

    private void DrawPerformanceSettings()
    {
        DrawInstrumentNameReferenceWindow();

        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_performance_settings);

        if (ImGui.Checkbox(setting_label_auto_switch_instrument_bmp, ref MidiBard.config.bmpTrackNames))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_switch_transpose_instrument_bmp_trackname);

        ImGui.SameLine();
        // var btnNameReferencesize = ImGuiHelpers.GetButtonSize(btnNameReferenceText);
        // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Current.Button.InfoNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Current.Button.InfoHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Current.Button.InfoActive);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.InfoCircle, "btnInstrumentsNameReference", "Click to show instruments name reference"))
        {
            showInstrumentNameReferenceWindow ^= true;
        }
        ImGui.PopStyleColor(3);

        //-------------------

        ImGui.Checkbox(setting_label_auto_switch_instrument_by_file_name, ref MidiBard.config.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_label_auto_switch_instrument_by_file_name);

        //-------------------

        ImGui.Checkbox(setting_label_auto_transpose_by_file_name, ref MidiBard.config.autoTransposeBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_auto_transpose_by_file_name);

        //-------------------

        if (ImGui.Checkbox(setting_label_auto_align_loaded_midi, ref MidiBard.config.AlignMidi))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_align_loaded_midi);

        //-------------------

        if (ImGui.Checkbox(setting_label_auto_adapt_notes, ref MidiBard.config.AdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_adapt_notes);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowAdaptNotesOOR", "Show/Hide in main window", ref MidiBard.config.UiShowAdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(setting_label_tone_mode);
        if (ImGuiUtil.EnumCombo($"##{setting_label_tone_mode}", ref MidiBard.config.GuitarToneMode, toneModeToolTips))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_tone_mode);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowGuitarToneMode", "Show/Hide in main window", ref MidiBard.config.UiShowGuitarToneMode))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        ImGui.TextUnformatted(setting_label_set_play_speed);
        if (ImGui.InputFloat($"##{setting_label_set_play_speed}", ref MidiBard.config.PlaySpeed, 0.1f, 0.5f, GetBpmString(), ImGuiInputTextFlags.AutoSelectAll))
        {
            SetSpeed();
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.PlaySpeed = 1;
            SetSpeed();
        }
        ImGuiUtil.ToolTip(setting_tooltip_set_speed);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowPlaySpeed", "Show/Hide in main window", ref MidiBard.config.UiShowPlaySpeed))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        // SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        // SetNextItemWidth(itemWidth);
        ImGui.TextUnformatted($"Global transpose");
        if (ImGui.InputInt($"##{setting_label_transpose_all}", ref MidiBard.config.TransposeGlobal, 12))
        {
            MidiBard.config.SetTransposeGlobal(MidiBard.config.TransposeGlobal);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SetTransposeGlobal(0);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        ImGuiUtil.ToolTip(setting_tooltip_transpose_all);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowTransposeGlobal", "Show/Hide in main window", ref MidiBard.config.UiShowTransposeGlobal))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        // var itemWidth = ImGuiHelpers.GlobalScale * 100;
        // SetNextItemWidth(itemWidth);
        ImGui.TextUnformatted($"Delay between songs (solo only)");
        if (ImGui.InputFloat($"##{setting_label_song_delay}", ref MidiBard.config.SecondsBetweenTracks, 0.5f, 0.5f, $" {MidiBard.config.SecondsBetweenTracks:f2} s", ImGuiInputTextFlags.AutoSelectAll))
        {
            MidiBard.config.SecondsBetweenTracks = Math.Max(0, MidiBard.config.SecondsBetweenTracks);
            IPCHandles.SyncAllSettings();
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SecondsBetweenTracks = 3;
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_song_delay);

        //-------------------

        ImGuiGroupPanel.EndGroupPanel();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        DrawLyricsOptions();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();

        DrawPostSongOptions();

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawInstrumentNameReferenceWindow()
    {
        if (!showInstrumentNameReferenceWindow) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(250, 100) * ImGuiHelpers.GlobalScale, ImGuiHelpers.MainViewport.Size);
        if (ImGui.Begin("Track Name References For Auto-Switch Instruments", ref showInstrumentNameReferenceWindow))
        {
            if (ImGui.BeginTable("ins", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Track Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var instrument in MidiBard.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    // Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(ImGui.GetFrameHeight()));
                    ImGui.Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(40, 40));

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGuiUtil.TextCopyable(SanitizeIntrumentName(instrument.FFXIVDisplayName));
                    ImGuiUtil.ToolTip("Click to copy the name");
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }

    private static string SanitizeIntrumentName(string input)
    {
        return Regex.Replace(input, "[^a-zA-Z]", "");
    }

}
