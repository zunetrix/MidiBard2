using System;
using System.Diagnostics;

using Dalamud.Bindings.ImGui;

using MidiBard.Managers;
using MidiBard.Control;
using MidiBard.Util;

namespace MidiBard;

public sealed class AgentInfoDebugWidget : Widget
{
    public override string Title => "Agent Info";

    public AgentInfoDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        DrawAgentPerformanceInfo();
        DrawAgentMetronomeInfo();
        DrawPerformInfo();

        ImGui.Separator();

        ImGui.Text($"currentUILanguage: {DalamudApi.PluginInterface.UiLanguage}");
    }

    public void DrawPerformInfo()
    {
        try
        {
            var performInfos = Offsets.PerformanceStructPtr;
            ImGui.Text($"PerformInfos: {performInfos.ToInt64() + 3:X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##PerformInfos")) ImGui.SetClipboardText($"{performInfos.ToInt64() + 3:X}");
            ImGui.Text($"CurrentInstrumentKey: {PerformanceState.CurrentInstrument}");
            ImGui.Text($"Instrument: {InstrumentHelper.GetDisplayName(PerformanceState.CurrentInstrument)}");
            ImGui.Text($"Name: {InstrumentHelper.InstrumentSheet.GetRow(PerformanceState.CurrentInstrument).Name.ExtractText()}");
            ImGui.Text($"Tone: {AgentManager.AgentPerformance.CurrentGroupTone}");

            ImGui.Separator();
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }
    }

    public void DrawAgentMetronomeInfo()
    {
        try
        {
            ImGui.Text($"AgentMetronome: {AgentManager.AgentMetronome.Pointer.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentMetronome")) ImGui.SetClipboardText($"{AgentManager.AgentMetronome.Pointer.ToInt64():X}");

            ImGui.Text($"vtbl: {AgentManager.AgentMetronome.VTable.ToInt64():X} +{AgentManager.AgentMetronome.VTable.ToInt64() - Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentMetronomev")) ImGui.SetClipboardText($"{AgentManager.AgentMetronome.VTable.ToInt64():X}");

            ImGui.Text($"Running: {AgentManager.AgentMetronome.MetronomeRunning}");
            ImGui.Text($"Ensemble: {AgentManager.AgentMetronome.EnsembleModeRunning}");
            ImGui.Text($"BeatsElapsed: {AgentManager.AgentMetronome.MetronomeBeatsElapsed}");
            ImGui.Text($"PPQN: {AgentManager.AgentMetronome.MetronomePPQN} ({60_000_000 / (double)AgentManager.AgentMetronome.MetronomePPQN:F3}bpm)");
            ImGui.Text($"BeatsPerBar: {AgentManager.AgentMetronome.MetronomeBeatsPerBar}");
            ImGui.Text($"Timer1: {TimeSpan.FromMilliseconds(AgentManager.AgentMetronome.MetronomeTimer1)}");
            ImGui.Text($"Timer2: {TimeSpan.FromTicks(AgentManager.AgentMetronome.MetronomeTimer2 * 10)}");

            ImGui.Separator();
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }
    }

    public void DrawAgentPerformanceInfo()
    {
        try
        {
            ImGui.Text($"AgentPerformance: {AgentManager.AgentPerformance.Pointer.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentPerformance")) ImGui.SetClipboardText($"{AgentManager.AgentPerformance.Pointer.ToInt64():X}");

            ImGui.Text($"vtbl: {AgentManager.AgentPerformance.VTable.ToInt64():X} +{AgentManager.AgentPerformance.VTable.ToInt64() - Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentPerformancev")) ImGui.SetClipboardText($"{AgentManager.AgentPerformance.VTable.ToInt64():X}");

            ImGui.Text($"notePressed: {AgentManager.AgentPerformance.notePressed}");
            ImGui.Text($"noteNumber: {AgentManager.AgentPerformance.noteNumber}");
            ImGui.Text($"InPerformanceMode: {AgentManager.AgentPerformance.InPerformanceMode}");
            ImGui.Text($"Timer1: {TimeSpan.FromMilliseconds(AgentManager.AgentPerformance.PerformanceTimer1)}");
            ImGui.Text($"Timer2: {TimeSpan.FromTicks(AgentManager.AgentPerformance.PerformanceTimer2 * 10)}");

            ImGui.Separator();
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }
    }
}

