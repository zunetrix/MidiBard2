using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Util;

namespace MidiBard;

public sealed class PerformanceSampleProbeWidget : Widget
{
    public override string Title => "Performance Samples";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.VolumeUp;

    private bool autoScroll = true;

    public PerformanceSampleProbeWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var probe = Context.Plugin.PerformanceSampleProbe;
        var entries = probe.Entries;

        var toggleLabel = probe.IsEnabled ? "Disable Probe##PerfSampleProbeToggle" : "Enable Probe##PerfSampleProbeToggle";
        if (ImGui.Button(toggleLabel))
        {
            if (probe.IsEnabled)
                probe.Disable();
            else
                probe.Enable();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll##PerfSampleAutoScroll", ref autoScroll);

        ImGui.SameLine();
        using (ImRaii.Disabled(entries.Count == 0))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##PerfSampleClear", "Clear captures"))
                probe.Clear();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(entries.Count == 0))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Clipboard, "##PerfSampleCopyRows", "Copy source rows"))
                ImGui.SetClipboardText(probe.BuildSourceRows());
        }

        ImGui.SameLine();
        var statusColor = probe.IsEnabled
            ? new Vector4(0.2f, 1f, 0.2f, 1f)
            : new Vector4(0.6f, 0.6f, 0.6f, 1f);
        ImGui.TextColored(statusColor, $"{entries.Count} / 500 captures");

        if (!string.IsNullOrWhiteSpace(probe.LastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.25f, 1f), probe.LastError);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (entries.Count == 0)
        {
            ImGui.TextDisabled(probe.IsEnabled
                ? "Play performance notes to capture SoundManager.PlaySound parameters."
                : "Enable the probe, enter performance mode, and play notes to capture sample parameters.");
            return;
        }

        if (ImGui.BeginTable("##PerfSampleProbeTable", 8,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
            new Vector2(-1, ImGui.GetContentRegionAvail().Y)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Instrument", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("SCD Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Sound", ImGuiTableColumnFlags.WidthFixed, 55 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Midi", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Vol", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var entry in entries.OrderByDescending(entry => entry.TimestampUtc).Take(200).Reverse())
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"));

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(GetInstrumentName(entry.InstrumentId));

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(entry.Path);

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(entry.SoundNumber.ToString());

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(entry.MidiNote.ToString());

                ImGui.TableSetColumnIndex(5);
                ImGui.TextUnformatted(entry.Volume.ToString("0.###"));

                ImGui.TableSetColumnIndex(6);
                ImGui.TextUnformatted(entry.VolumeCategory.ToString());

                ImGui.TableSetColumnIndex(7);
                ImGui.TextUnformatted($"{(entry.AutoRelease ? "A" : "-")}{(entry.DefaultFadeOut ? "F" : "-")}{(entry.IsPositional ? "P" : "-")}");
            }

            if (autoScroll && probe.IsEnabled)
                ImGui.SetScrollHereY(1.0f);

            ImGui.EndTable();
        }
    }

    private static string GetInstrumentName(uint instrumentId)
    {
        if (instrumentId > 0 && instrumentId < InstrumentHelper.InstrumentStrings.Length)
            return $"{instrumentId} - {InstrumentHelper.InstrumentStrings[instrumentId]}";

        return instrumentId == 0 ? "(none)" : instrumentId.ToString();
    }
}
