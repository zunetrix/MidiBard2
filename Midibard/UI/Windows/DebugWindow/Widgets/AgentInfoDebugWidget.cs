using System;
using System.Numerics;
using System.Reflection;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control;
using MidiBard.Managers;
using MidiBard.Util;

namespace MidiBard;

public sealed class AgentInfoDebugWidget : Widget
{
    public override string Title => "Agents & Offsets";

    public AgentInfoDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        DrawOffsetsSection();
        ImGui.Spacing();

        DrawAgentPerformanceSection();
        ImGui.Spacing();

        DrawAgentMetronomeSection();
        ImGui.Spacing();

        DrawPerformanceStateSection();
        ImGui.Spacing();

        DrawEnvironmentSection();
    }

    private static long BaseAddress => (long)DalamudApi.SigScanner.Module.BaseAddress;

    // --- 1. Memory Offsets ---
    private void DrawOffsetsSection()
    {
        if (!ImGui.CollapsingHeader("Memory Offsets", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(8f);

        try
        {
            var type = typeof(Offsets);
            var properties = type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            using var table = ImRaii.Table("##OffsetsTable", 3, 
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);

            if (table)
            {
                ImGui.TableSetupColumn("Offset Name", ImGuiTableColumnFlags.WidthFixed, 220f);
                ImGui.TableSetupColumn("Value / Relative", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 65f);
                ImGui.TableHeadersRow();

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(null);
                    if (value is IntPtr ptr)
                    {
                        var ptrVal = ptr.ToInt64();
                        var relative = ptrVal - BaseAddress;
                        var displayVal = ptrVal != 0 ? $"0x{ptrVal:X} (+{relative:X})" : "0x0 (null)";

                        DrawTableRow(
                            prop.Name,
                            displayVal,
                            copyValue: ptrVal.ToString("X"),
                            relativeOffset: ptrVal != 0 ? $"HEADER+{relative:X}" : null);
                    }
                    else
                    {
                        var strVal = value?.ToString() ?? "null";
                        DrawTableRow(prop.Name, strVal, copyValue: strVal);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(Style.Colors.RedVivid, $"Error reading offsets: {ex.Message}");
        }
    }

    // --- 2. Agent Performance ---
    private void DrawAgentPerformanceSection()
    {
        if (!ImGui.CollapsingHeader("Agent Performance", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(8f);

        try
        {
            var agent = AgentManager.AgentPerformance;
            if (agent == null)
            {
                ImGui.TextColored(Style.Colors.Yellow, "AgentPerformance is not initialized.");
                return;
            }

            using var table = ImRaii.Table("##AgentPerformanceTable", 3, 
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);

            if (table)
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 200f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 65f);
                ImGui.TableHeadersRow();

                var ptrVal = agent.Pointer.ToInt64();
                var ptrRel = ptrVal - BaseAddress;
                DrawTableRow("Pointer", $"0x{ptrVal:X} (+{ptrRel:X})", ptrVal.ToString("X"), $"HEADER+{ptrRel:X}");

                var vtblVal = agent.VTable.ToInt64();
                var vtblRel = vtblVal - BaseAddress;
                DrawTableRow("VTable", $"0x{vtblVal:X} (+{vtblRel:X})", vtblVal.ToString("X"), $"HEADER+{vtblRel:X}");

                DrawTableRow("InPerformanceMode", agent.InPerformanceMode.ToString());
                DrawTableRow("notePressed", agent.notePressed.ToString());
                DrawTableRow("noteNumber", agent.noteNumber.ToString());
                DrawTableRow("CurrentGroupTone", agent.CurrentGroupTone.ToString());
                DrawTableRow("Timer 1 (ms)", $"{TimeSpan.FromMilliseconds(agent.PerformanceTimer1)} ({agent.PerformanceTimer1} ms)", agent.PerformanceTimer1.ToString());
                DrawTableRow("Timer 2 (ticks)", $"{TimeSpan.FromTicks(agent.PerformanceTimer2 * 10)} ({agent.PerformanceTimer2})", agent.PerformanceTimer2.ToString());
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(Style.Colors.RedVivid, $"Error reading AgentPerformance: {ex.Message}");
        }
    }

    // --- 3. Agent Metronome ---
    private void DrawAgentMetronomeSection()
    {
        if (!ImGui.CollapsingHeader("Agent Metronome", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(8f);

        try
        {
            var metronome = AgentManager.AgentMetronome;
            if (metronome == null)
            {
                ImGui.TextColored(Style.Colors.Yellow, "AgentMetronome is not initialized.");
                return;
            }

            using var table = ImRaii.Table("##AgentMetronomeTable", 3, 
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);

            if (table)
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 200f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 65f);
                ImGui.TableHeadersRow();

                var ptrVal = metronome.Pointer.ToInt64();
                var ptrRel = ptrVal - BaseAddress;
                DrawTableRow("Pointer", $"0x{ptrVal:X} (+{ptrRel:X})", ptrVal.ToString("X"), $"HEADER+{ptrRel:X}");

                var vtblVal = metronome.VTable.ToInt64();
                var vtblRel = vtblVal - BaseAddress;
                DrawTableRow("VTable", $"0x{vtblVal:X} (+{vtblRel:X})", vtblVal.ToString("X"), $"HEADER+{vtblRel:X}");

                DrawTableRow("MetronomeRunning", metronome.MetronomeRunning.ToString());
                DrawTableRow("EnsembleModeRunning", metronome.EnsembleModeRunning.ToString());
                DrawTableRow("BeatsElapsed", metronome.MetronomeBeatsElapsed.ToString());

                var ppqn = metronome.MetronomePPQN;
                var bpmStr = ppqn > 0 ? $"{60_000_000 / (double)ppqn:F3} bpm" : "0 bpm";
                DrawTableRow("PPQN", $"{ppqn} ({bpmStr})", ppqn.ToString());

                DrawTableRow("BeatsPerBar", metronome.MetronomeBeatsPerBar.ToString());
                DrawTableRow("Timer 1 (ms)", $"{TimeSpan.FromMilliseconds(metronome.MetronomeTimer1)} ({metronome.MetronomeTimer1} ms)", metronome.MetronomeTimer1.ToString());
                DrawTableRow("Timer 2 (ticks)", $"{TimeSpan.FromTicks(metronome.MetronomeTimer2 * 10)} ({metronome.MetronomeTimer2})", metronome.MetronomeTimer2.ToString());
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(Style.Colors.RedVivid, $"Error reading AgentMetronome: {ex.Message}");
        }
    }

    // --- 4. Performance State & Instrument ---
    private void DrawPerformanceStateSection()
    {
        if (!ImGui.CollapsingHeader("Performance State & Sheet Info", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        using var indent = ImRaii.PushIndent(8f);

        try
        {
            using var table = ImRaii.Table("##PerformanceStateTable", 3, 
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);

            if (table)
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 200f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 65f);
                ImGui.TableHeadersRow();

                var performInfos = Offsets.PerformanceStructPtr;
                if (performInfos != IntPtr.Zero)
                {
                    var val = performInfos.ToInt64() + 3;
                    var rel = val - BaseAddress;
                    DrawTableRow("PerformInfos (Struct + 3)", $"0x{val:X} (+{rel:X})", val.ToString("X"), $"HEADER+{rel:X}");
                }
                else
                {
                    DrawTableRow("PerformInfos", "0x0 (null)");
                }

                var currentInst = PerformanceState.CurrentInstrument;
                DrawTableRow("CurrentInstrument Key", currentInst.ToString());
                DrawTableRow("Instrument Display Name", InstrumentHelper.GetDisplayName(currentInst));

                var sheetRow = InstrumentHelper.InstrumentSheet.GetRow(currentInst);
                var sheetName = sheetRow.Name.ExtractText();
                DrawTableRow("Sheet Row Name", string.IsNullOrEmpty(sheetName) ? "(empty)" : sheetName);
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(Style.Colors.RedVivid, $"Error reading PerformanceState: {ex.Message}");
        }
    }

    // --- 5. Environment & UI Info ---
    private void DrawEnvironmentSection()
    {
        if (!ImGui.CollapsingHeader("Environment & System"))
            return;

        using var indent = ImRaii.PushIndent(8f);

        using var table = ImRaii.Table("##EnvironmentTable", 3, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);

        if (table)
        {
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 200f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 65f);
            ImGui.TableHeadersRow();

            DrawTableRow("Process ID (PID)", Environment.ProcessId.ToString());
            DrawTableRow("Main Module Base Address", $"0x{BaseAddress:X}", BaseAddress.ToString("X"));
            DrawTableRow("UI Language", DalamudApi.PluginInterface.UiLanguage);
        }
    }

    // --- Helper for rendering table rows with copy icon buttons ---
    private static void DrawTableRow(string name, string displayValue, string? copyValue = null, string? relativeOffset = null)
    {
        ImGui.TableNextRow();

        // Column 0: Name
        ImGui.TableNextColumn();
        ImGui.Text(name);

        // Column 1: Display Value
        ImGui.TableNextColumn();
        ImGui.Text(displayValue);

        // Column 2: Action buttons with icons
        ImGui.TableNextColumn();
        var toCopy = copyValue ?? displayValue;
        var btnSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());

        // Copy value button
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, $"##cp_{name}", "Copy Value", size: btnSize))
        {
            ImGui.SetClipboardText(toCopy);
            ImGuiUtil.AddNotification(NotificationType.Success, $"Copied: {toCopy}");
        }

        // Copy relative offset button (if applicable)
        if (!string.IsNullOrEmpty(relativeOffset))
        {
            ImGui.SameLine(0f, 4f);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FileCode, $"##cpr_{name}", $"Copy Relative ({relativeOffset})", size: btnSize))
            {
                ImGui.SetClipboardText(relativeOffset);
                ImGuiUtil.AddNotification(NotificationType.Success, $"Copied: {relativeOffset}");
            }
        }
    }
}
