using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Util;

using MidiBard.Control;
using MidiBard.Resources;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MainWindow
{
    readonly uint[] toneColors =
    [
        0xee_6666bb,
        0xee_bbbb66,
        0xee_66bb66,
        0xee_66bbbb,
        0xee_bb6666
    ];

    readonly string[] toneStrings =
    [
        "I", "II", "III", "IV", "V",
    ];

    private void DrawTrackSelection()
    {
        if (!Plugin.Config.ShowTrackSelection) return;

        if (Plugin.CurrentBardPlayback?.TrackInfos?.Any() ?? false)
        {
            if (ImGui.BeginChild("TrackTrunkSelection",
                    new Vector2(
                        ImGuiUtil.GetWindowContentRegionWidth() - 1,
                        Math.Min(Plugin.CurrentBardPlayback.TrackInfos.Length, (float)Plugin.Config.TrackSelectionMaxVisibleRows + 0.5f) * ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y),
                    false, ImGuiWindowFlags.NoDecoration))
            {
                DrawTrackSelectionList();
                ImGui.EndChild();
            }

            ImGui.Separator();
        }
    }

    private void DrawTrackSelectionList()
    {
        ImGui.PushStyleColor(ImGuiCol.Separator, Style.Colors.Black);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2 * ImGuiHelpers.GlobalScale, ImGui.GetStyle().ItemSpacing.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.6f, 0));

        if (PerformanceState.PlayingGuitar && Plugin.Config.GuitarToneMode == GuitarToneMode.OverrideByTrack)
        {
            ImGui.Columns(2);
            ImGui.SetColumnWidth(0, ImGuiUtil.GetWindowContentRegionWidth() - 6 * (2 * ImGuiHelpers.GlobalScale) - 5 * (ImGui.GetFrameHeight() * 0.8f));
        }

        bool soloing = Plugin.Config.SoloedTrack is not null;
        int? soloingTrack = Plugin.Config.SoloedTrack;

        for (int i = 0; i < Plugin.CurrentBardPlayback.TrackInfos.Length; i++)
        {
            DrawTrackLine(i, soloing, soloingTrack);
        }

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor();
    }

    private void DrawTrackLine(int i, bool soloing, int? soloingTrack)
    {
        ImGui.PushID($"tracks{i}");
        try
        {
            ImGui.SetCursorPosX(0);
            var isEnabled = Plugin.Config.TrackStatus[i].Enabled;
            var isSolo = soloingTrack == i;
            var textColor = isEnabled ? ThemeManager.CurrentTheme.Text : ThemeManager.CurrentTheme.TextDisabled;
            var checkmarkColor = isEnabled ? ThemeManager.CurrentTheme.CheckMark : ThemeManager.CurrentTheme.TextDisabled;
            if (soloing) textColor = isSolo ? Plugin.Config.themeColor : ThemeManager.CurrentTheme.TextDisabled;

            using (
                ImRaii.PushColor(ImGuiCol.Text, textColor)
                .Push(ImGuiCol.CheckMark, checkmarkColor))
            {
                if (ImGui.Checkbox("##trackCheckbox", ref Plugin.Config.TrackStatus[i].Enabled))
                    JudgeSwitchInstrument();

                ImGui.SameLine();
                ImGui.Dummy(Vector2.Zero);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFrameHeightWithSpacing() * 3);
                ImGui.InputInt($"##TransposeByTrack", ref Plugin.Config.TrackStatus[i].Transpose, 12);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    Plugin.Config.TrackStatus[i].Transpose = 0;
                }

                ImGui.SameLine();
                ImGui.Dummy(Vector2.Zero);
                ImGui.SameLine();
                ImGui.Text((isSolo ? "[Solo]" : $"[{i + 1:00}]") + $" {Plugin.CurrentBardPlayback.TrackInfos[i]}");

                if (ImGui.IsItemClicked())
                {
                    Plugin.Config.TrackStatus[i].Enabled ^= true;
                    JudgeSwitchInstrument();
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    HandleSoloTrackClick(i, isSolo);
                }

                ImGuiUtil.ToolTip(Plugin.CurrentBardPlayback.TrackInfos[i].ToLongString() + "\n\n" + Language.window_tooltip_track_selection);

                if (PerformanceState.PlayingGuitar && Plugin.Config.GuitarToneMode == GuitarToneMode.OverrideByTrack)
                {
                    ImGui.NextColumn();
                    for (int toneId = 0; toneId < 5; toneId++)
                    {
                        if (toneId != 0) ImGui.SameLine();
                        DrawToneSelectButton(toneId, ref Plugin.Config.TrackStatus[i].Tone);
                    }
                    ImGui.NextColumn();
                }
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e.ToString());
        }
        ImGui.PopID();

    }

    private void HandleSoloTrackClick(int index, bool wasSolo)
    {
        Plugin.Config.SoloedTrack = wasSolo ? null : index;

        if (!wasSolo)
            Chat.SendMessage("/echo [MidiBard] Track SOLO mode activated <se.9>");

        if (Plugin.Config.bmpTrackNames && !Plugin.CurrentBardPlayback.IsRunning &&
            Plugin.Config.SoloedTrack is int solo &&
            Plugin.Config.TrackStatus[solo].Enabled &&
            Plugin.CurrentBardPlayback.TrackInfos[solo].InstrumentIdFromTrackName((ushort)Plugin.Config.DefaultInstrumentId, Plugin.Config.ForceDefaultInstrument) is uint inst)
        {
            Plugin.InstrumentSwitcher.SwitchToAsync(inst);
        }
    }

    private bool DrawToneSelectButton(int toneID, ref int selected)
    {
        var buttonSize = new Vector2(ImGui.GetFrameHeight() * 0.8f, ImGui.GetFrameHeight());
        var toneColor = toneColors[toneID];
        var toneName = toneStrings[toneID];
        var isToneSelected = selected == toneID;
        var ret = false;
        if (isToneSelected)
            ImGui.PushStyleColor(ImGuiCol.Button, toneColor);

        ImGui.PushStyleColor(ImGuiCol.ButtonActive, toneColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, toneColor);
        if (ImGui.Button($"{toneName}##toneSwitchButton", buttonSize))
        {
            selected = toneID;
            ret = true;
        }
        ImGui.PopStyleColor(2);

        if (isToneSelected)
            ImGui.PopStyleColor();

        return ret;
    }

    private void JudgeSwitchInstrument()
    {
        if (Plugin.Config.bmpTrackNames && !Plugin.CurrentBardPlayback.IsRunning)
        {
            var firstEnabledTrack = Plugin.CurrentBardPlayback.TrackInfos.FirstOrDefault(trackInfo => trackInfo.IsEnabled(Plugin.Config.TrackStatus));
            var firstInstrumentId = firstEnabledTrack?.InstrumentIdFromTrackName((ushort)Plugin.Config.DefaultInstrumentId, Plugin.Config.ForceDefaultInstrument);
            if (firstInstrumentId != null)
            {
                Plugin.InstrumentSwitcher.SwitchToAsync((uint)firstInstrumentId);
            }
            else
            {
                Plugin.InstrumentSwitcher.SwitchToAsync(0);
            }
        }
    }
}
