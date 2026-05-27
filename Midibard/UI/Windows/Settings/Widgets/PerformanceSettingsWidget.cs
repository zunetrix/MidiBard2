using System;
using System.Globalization;
using System.Numerics;

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
            Language.perf_tone_mode_off_tooltip,
            Language.perf_tone_mode_standard_tooltip,
            Language.perf_tone_mode_simple_tooltip,
            Language.perf_tone_mode_override_by_track_tooltip,
            Language.perf_tone_mode_program_electric_guitar_tooltip,
        ];
        _toneModeLabels =
        [
            Language.perf_tone_mode_off,
            Language.perf_tone_mode_standard,
            Language.perf_tone_mode_simple,
            Language.perf_tone_mode_override_by_track,
            Language.perf_tone_mode_program_electric_guitar,
        ];
        _antiStackNoteLabels =
        [
            Language.perf_anti_stack_off,
            Language.perf_anti_stack_keep_first,
            Language.perf_anti_stack_keep_shortest,
            Language.perf_anti_stack_keep_longest,
        ];
    }

    public PerformanceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        EnsureLabelsValid();
        DrawInstrumentNameReferenceWindow();

        var cfg = Context.Plugin.Config;

        //  Instrument switching

        if (ImGui.Checkbox(Language.setting_perf_auto_switch_instrument_trackname, ref cfg.bmpTrackNames))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_perf_auto_switch_instrument_trackname_tooltip);

        ImGui.SameLine();
        if (ImGuiUtil.InfoIconButton(FontAwesomeIcon.InfoCircle, "BtnInstrumentsNameReference", "Click to show instruments name reference"))
            _showInstrumentNameReferenceWindow ^= true;

        ImGui.Checkbox(Language.setting_perf_auto_switch_instrument_filename, ref cfg.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(Language.setting_perf_auto_switch_instrument_filename_tooltip);

        ImGui.Checkbox(Language.setting_perf_auto_transpose_filename, ref cfg.autoTransposeBySongName);
        ImGuiUtil.ToolTip(Language.setting_perf_auto_transpose_filename_tooltip);

        //  MIDI processing

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Checkbox(Language.setting_perf_auto_align_midi, ref cfg.AlignMidi))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_perf_auto_align_midi_tooltip);

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

        if (ImGui.Checkbox(Language.setting_perf_auto_adapt_notes, ref cfg.AdaptNotesOOR))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_perf_auto_adapt_notes_tooltip);

        ImGui.Text(Language.setting_perf_anti_note_stack);
        if (ImGuiUtil.EnumCombo("##AntiStackNote", ref cfg.AntiStackType, labelsOverride: _antiStackNoteLabels))
            Context.Plugin.IpcProvider.SyncAllSettings();

        //  Playback controls

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(Language.setting_perf_tone_mode);
        if (ImGuiUtil.EnumCombo("##GuitarToneMode", ref cfg.GuitarToneMode, labelsOverride: _toneModeLabels, toolTips: _toneModeToolTips))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_perf_tone_mode_tooltip);

        ImGui.Text(Language.setting_perf_play_speed);
        if (ImGui.InputFloat("##PlaySpeed", ref cfg.PlaySpeed, 0.1f, 0.5f,
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
        ImGuiUtil.ToolTip(Language.setting_perf_play_speed_tooltip);

        ImGui.Text(Language.setting_perf_global_transpose);
        if (ImGui.InputInt("##GlobalTranspose", ref cfg.TransposeGlobal, 12))
        {
            cfg.SetTransposeGlobal(cfg.TransposeGlobal, Context.Plugin);
            Context.Plugin.IpcProvider.GlobalTranspose(cfg.TransposeGlobal);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            cfg.SetTransposeGlobal(0, Context.Plugin);
            Context.Plugin.IpcProvider.GlobalTranspose(cfg.TransposeGlobal);
        }
        ImGuiUtil.ToolTip(Language.setting_perf_transpose_tooltip);

        ImGui.Text(Language.setting_perf_delay_between_songs);
        if (ImGui.InputFloat("##SecondsBetweenTracks", ref cfg.SecondsBetweenTracks, 0.5f, 0.5f,
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
        ImGuiUtil.ToolTip(Language.setting_perf_song_delay_tooltip);

        ImGui.Text(Language.setting_perf_default_instrument);
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
            if (ImGui.BeginTable("###InstrumentReferenceTable", 2,
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
                    ImGuiUtil.TextCopyable(instrument.FFXIVDisplayName);
                    ImGuiUtil.ToolTip("Click to copy the name");
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }

    private void DrawDefaultInstrumentComboBox()
    {
        if (!ImGui.BeginCombo("##DefaultInstrumentCombo",
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
            if (ImGui.Selectable($"{instrument.InstrumentString}####DefaultInstrument_{i}",
                    Context.Plugin.Config.DefaultInstrumentId == i, ImGuiSelectableFlags.SpanAllColumns))
            {
                Context.Plugin.Config.DefaultInstrumentId = i;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
        }
        ImGui.GetWindowDrawList().ChannelsMerge();
        ImGui.EndCombo();
    }
}
