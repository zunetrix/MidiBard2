using System;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

using MidiBard.Resources;
using MidiBard.Util;
using MidiBard.Extensions.General;
using MidiBard.Extensions.Dalamud;

namespace MidiBard;

public sealed class PerformanceSettingsWidget : Widget
{
    public override string Title => "Performance";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.SlidersH;

    private static CultureInfo? _labelsCulture;
    private static string[]? _toneModeLabels;
    private static string[]? _toneModeToolTips;
    private static string[]? _antiStackNoteLabels;

    private bool _showInstrumentNameReferenceWindow;

    private static void EnsureLabelsValid()
    {
        if (_labelsCulture == Language.Culture) return;
        _labelsCulture = Language.Culture;
        _toneModeToolTips =
        [
            Language.tone_mode_tooltip_off,
            Language.tone_mode_tooltip_standard,
            Language.tone_mode_tooltip_simple,
            Language.tone_mode_tooltip_override_by_track,
            Language.tone_mode_tooltip_program_electric_guitar_mode,
        ];
        _toneModeLabels =
        [
            Language.tone_mode_option_off,
            Language.tone_mode_option_standard,
            Language.tone_mode_option_simple,
            Language.tone_mode_option_override_by_track,
            Language.tone_mode_option_program_electric_guitar_mode,
        ];
        _antiStackNoteLabels =
        [
            Language.anti_stack_note_option_off,
            Language.anti_stack_note_option_keep_first_note,
            Language.anti_stack_note_option_keep_shortest_note,
            Language.anti_stack_note_option_keep_longest_note,
        ];
    }

    public PerformanceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        EnsureLabelsValid();
        DrawInstrumentNameReferenceWindow();

        var cfg = Context.Plugin.Config;

        //  Instrument switching

        if (ImGui.Checkbox(Language.setting_label_auto_switch_instrument_bmp, ref cfg.bmpTrackNames))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_switch_transpose_instrument_bmp_trackname);

        ImGui.SameLine();
        if (ImGuiUtil.InfoIconButton(FontAwesomeIcon.InfoCircle, "sw2BtnInstrumentsNameReference", "Click to show instruments name reference"))
            _showInstrumentNameReferenceWindow ^= true;

        ImGui.Checkbox(Language.setting_label_auto_switch_instrument_by_file_name, ref cfg.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(Language.setting_tooltip_label_auto_switch_instrument_by_file_name);

        ImGui.Checkbox(Language.setting_label_auto_transpose_by_file_name, ref cfg.autoTransposeBySongName);
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_transpose_by_file_name);

        //  MIDI processing

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Checkbox(Language.setting_label_auto_align_loaded_midi, ref cfg.AlignMidi))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_align_loaded_midi);

        if (cfg.AlignMidi)
        {
            ImGui.Spacing();
            ImGui.Indent(ImGui.GetStyle().IndentSpacing * 2);
            ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputDouble("Align start offset", ref cfg.AlignMidiStartOffset, 0.1f, 0.1f,
                    $" {cfg.AlignMidiStartOffset:f2} s", ImGuiInputTextFlags.AutoSelectAll))
            {
                cfg.AlignMidiStartOffset = Math.Clamp(cfg.AlignMidiStartOffset, 0f, 10f);
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                cfg.AlignMidiStartOffset = 0;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("New song start offset, right click to reset");
            ImGui.Unindent(ImGui.GetStyle().IndentSpacing * 2);
        }

        if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref cfg.AdaptNotesOOR))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_auto_adapt_notes);

        ImGui.Text(Language.setting_label_anti_note_stack_loaded_midi);
        if (ImGuiUtil.EnumCombo("##sw2AntiStackNote", ref cfg.AntiStackType, labelsOverride: _antiStackNoteLabels))
            Context.Plugin.IpcProvider.SyncAllSettings();

        //  Playback controls

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(Language.setting_label_tone_mode);
        if (ImGuiUtil.EnumCombo("##sw2GuitarToneMode", ref cfg.GuitarToneMode, labelsOverride: _toneModeLabels, toolTips: _toneModeToolTips))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_tone_mode);

        ImGui.Text(Language.setting_label_set_play_speed);
        if (ImGui.InputFloat("##sw2PlaySpeed", ref cfg.PlaySpeed, 0.1f, 0.5f,
                Context.Plugin.CurrentBardPlayback?.GetBpmLabel(), ImGuiInputTextFlags.AutoSelectAll))
        {
            cfg.PlaySpeed = cfg.PlaySpeed.Clamp(0.1f, 10f);
            Context.Plugin.CurrentBardPlayback.SetSpeed(cfg.PlaySpeed);
        }
        if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            cfg.PlaySpeed = 1;
            Context.Plugin.CurrentBardPlayback.SetSpeed(cfg.PlaySpeed);
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_set_speed);

        ImGui.Text(Language.setting_label_global_transpose);
        if (ImGui.InputInt("##sw2GlobalTranspose", ref cfg.TransposeGlobal, 12))
        {
            cfg.SetTransposeGlobal(cfg.TransposeGlobal, Context.Plugin);
            Context.Plugin.IpcProvider.GlobalTranspose(cfg.TransposeGlobal);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            cfg.SetTransposeGlobal(0, Context.Plugin);
            Context.Plugin.IpcProvider.GlobalTranspose(cfg.TransposeGlobal);
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_transpose_all);

        ImGui.Text(Language.setting_label_delay_between_songs);
        if (ImGui.InputFloat("##sw2SongDelay", ref cfg.SecondsBetweenTracks, 0.5f, 0.5f,
                $" {cfg.SecondsBetweenTracks:f2} s", ImGuiInputTextFlags.AutoSelectAll))
        {
            cfg.SecondsBetweenTracks = Math.Max(0, cfg.SecondsBetweenTracks);
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            cfg.SecondsBetweenTracks = 3;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_song_delay);

        ImGui.Text(Language.setting_label_default_instrument);
        DrawDefaultInstrumentComboBox();
        ImGuiUtil.HelpMarker("Default instrument if the track or file name doesn't contain a recognizable instrument name");

        ImGui.SameLine();
        if (ImGui.Checkbox("Force Default Instrument", ref Context.Plugin.Config.ForceDefaultInstrument))
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Force all tracks to use the default instrument, even if they have a recognizable one");
    }

    private void DrawInstrumentNameReferenceWindow()
    {
        if (!_showInstrumentNameReferenceWindow) return;

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(250, 100) * ImGuiHelpers.GlobalScale,
            ImGuiHelpers.MainViewport.Size);

        if (ImGui.Begin("Track Name References For Auto-Switch Instruments", ref _showInstrumentNameReferenceWindow))
        {
            if (ImGui.BeginTable("###SW2InstrumentReferenceTable", 2,
                    ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Track Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var instrument in InstrumentHelper.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(40, 40));
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGuiUtil.TextCopyable(SanitizeInstrumentName(instrument.FFXIVDisplayName));
                    ImGuiUtil.ToolTip("Click to copy the name");
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }

    private void DrawDefaultInstrumentComboBox()
    {
        if (!ImGui.BeginCombo("##sw2DefaultInstrumentCombo",
                InstrumentHelper.InstrumentStrings[Context.Plugin.Config.DefaultInstrumentId],
                ImGuiComboFlags.HeightLarge))
        {
            return;
        }

        ImGui.GetWindowDrawList().ChannelsSplit(2);
        for (uint i = 0; i < InstrumentHelper.Instruments.Length; i++)
        {
            var instrument = InstrumentHelper.Instruments[i];
            ImGui.GetWindowDrawList().ChannelsSetCurrent(1);
            DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(ImGui.GetTextLineHeightWithSpacing()));

            ImGui.SameLine();
            ImGui.GetWindowDrawList().ChannelsSetCurrent(0);
            ImGui.AlignTextToFramePadding();

            if (ImGui.Selectable($"{SanitizeInstrumentName(instrument.InstrumentString)}####sw2DefaultInstrument_{i}",
                    Context.Plugin.Config.DefaultInstrumentId == i, ImGuiSelectableFlags.SpanAllColumns))
            {
                Context.Plugin.Config.DefaultInstrumentId = i;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
        }
        ImGui.GetWindowDrawList().ChannelsMerge();
        ImGui.EndCombo();
    }

    private static string SanitizeInstrumentName(string input) => Regex.Replace(input, "[^a-zA-Z]", "");
}
