using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Interface;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Util;

using MidiBard2.Resources;

namespace MidiBard;

public partial class PluginUI
{
    private readonly string[] toneModeToolTips = {
        "Off: Does not take over game's guitar tone control.",
        "Standard: Standard midi channel and ProgramChange handling, each channel will keep it's program state separately.",
        "Simple: Simple ProgramChange handling, ProgramChange event on any channel will change all channels' program state. (This is BardMusicPlayer's default behavior.)",
        "Override by track: Assign guitar tone manually for each track and ignore ProgramChange events.",
    };

    private static readonly List<string> AntiStackOptions = new List<string>() { "Off", "keep first note", "keep shortest note", "keep longest note" };

    private void DrawPerformanceSettings()
    {
        DrawInstrumentNameReferenceWindow();

        ImGuiGroupPanel.BeginGroupPanel(Language.setting_group_label_performance_settings);

        if (ImGui.Checkbox(Language.setting_label_auto_switch_instrument_bmp, ref MidiBard.config.bmpTrackNames))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_switch_transpose_instrument_bmp_trackname);

        ImGui.SameLine();
        // var btnNameReferencesize = ImGuiHelpers.GetButtonSize(btnNameReferenceText);
        // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X);
        ImGui.PushStyleColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Style.Components.ButtonInfoActive);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.InfoCircle, "btnInstrumentsNameReference", "Click to show instruments name reference"))
        {
            showInstrumentNameReferenceWindow ^= true;
        }
        ImGui.PopStyleColor(3);

        //-------------------

        ImGui.Checkbox(Language.setting_label_auto_switch_instrument_by_file_name, ref MidiBard.config.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(Language.setting_tooltip_label_auto_switch_instrument_by_file_name);

        //-------------------

        ImGui.Checkbox(Language.setting_label_auto_transpose_by_file_name, ref MidiBard.config.autoTransposeBySongName);
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_transpose_by_file_name);

        //-------------------

        if (ImGui.Checkbox(Language.setting_label_auto_align_loaded_midi, ref MidiBard.config.AlignMidi))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_align_loaded_midi);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowAutoAlignMidi", "Show/Hide in main window", ref MidiBard.config.UiShowAutoAlignMidi))
        {
            IPCHandles.SyncAllSettings();
        }

        if (MidiBard.config.AlignMidi)
        {
            ImGui.Spacing();
            ImGui.Indent(ImGui.GetStyle().IndentSpacing * 2);
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputDouble($"Align start offset", ref MidiBard.config.AlignMidiStartOffset, 0.1f, 0.1f, $" {MidiBard.config.AlignMidiStartOffset:f2} s", ImGuiInputTextFlags.AutoSelectAll))
            {
                MidiBard.config.AlignMidiStartOffset = Math.Clamp(MidiBard.config.AlignMidiStartOffset, 0f, 10f);
                IPCHandles.SyncAllSettings();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                MidiBard.config.AlignMidiStartOffset = 0;
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("New song start offset, right click to reset");
            ImGui.Unindent(ImGui.GetStyle().IndentSpacing * 2);
        }

        //-------------------

        if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref MidiBard.config.AdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_adapt_notes);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowAdaptNotesOOR", "Show/Hide in main window", ref MidiBard.config.UiShowAdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        ImGui.Text(Language.setting_label_anti_note_stack_loaded_midi);
        if (ImGui.BeginCombo("##combo", AntiStackOptions[MidiBard.config.AntiStackType]))
        {
            for (int n = 0; n < AntiStackOptions.Count; n++)
            {
                bool is_selected = MidiBard.config.AntiStackType == n;
                if (ImGui.Selectable(AntiStackOptions[n], is_selected))
                    MidiBard.config.AntiStackType = n;
                if (is_selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        //-------------------

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(Language.setting_label_tone_mode);
        if (ImGuiUtil.EnumCombo($"##{Language.setting_label_tone_mode}", ref MidiBard.config.GuitarToneMode, toneModeToolTips))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_tone_mode);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowGuitarToneMode", "Show/Hide in main window", ref MidiBard.config.UiShowGuitarToneMode))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        ImGui.TextUnformatted(Language.setting_label_set_play_speed);
        if (ImGui.InputFloat($"##{Language.setting_label_set_play_speed}", ref MidiBard.config.PlaySpeed, 0.1f, 0.5f, GetBpmString(), ImGuiInputTextFlags.AutoSelectAll))
        {
            SetSpeed();
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.PlaySpeed = 1;
            SetSpeed();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_set_speed);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowPlaySpeed", "Show/Hide in main window", ref MidiBard.config.UiShowPlaySpeed))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        // SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        // SetNextItemWidth(itemWidth);
        ImGui.TextUnformatted($"Global transpose");
        if (ImGui.InputInt($"##{Language.setting_label_transpose_all}", ref MidiBard.config.TransposeGlobal, 12))
        {
            MidiBard.config.SetTransposeGlobal(MidiBard.config.TransposeGlobal);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SetTransposeGlobal(0);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_transpose_all);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowTransposeGlobal", "Show/Hide in main window", ref MidiBard.config.UiShowTransposeGlobal))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        // var itemWidth = ImGuiHelpers.GlobalScale * 100;
        // SetNextItemWidth(itemWidth);
        ImGui.TextUnformatted($"Delay between songs (solo only)");
        if (ImGui.InputFloat($"##{Language.setting_label_song_delay}", ref MidiBard.config.SecondsBetweenTracks, 0.5f, 0.5f, $" {MidiBard.config.SecondsBetweenTracks:f2} s", ImGuiInputTextFlags.AutoSelectAll))
        {
            MidiBard.config.SecondsBetweenTracks = Math.Max(0, MidiBard.config.SecondsBetweenTracks);
            IPCHandles.SyncAllSettings();
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SecondsBetweenTracks = 3;
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_song_delay);

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
