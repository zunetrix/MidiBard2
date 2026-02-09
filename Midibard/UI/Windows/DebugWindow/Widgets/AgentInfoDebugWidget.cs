using System;
using System.Diagnostics;

using Dalamud.Bindings.ImGui;

using MidiBard.Managers;

namespace MidiBard;

public sealed class AgentInfoDebugWidget : Widget
{
    public override string Title => "Agent Info";

    public AgentInfoDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        try
        {
            // ImGui.TextUnformatted($"AgentModule: {(long)AgentManager.Instance:X}");
            //ImGui.SameLine();
            // if (ImGui.SmallButton("C##AgentModule")) ImGui.SetClipboardText($"{(long)AgentManager.AgentModule:X}");
            // TextUnformatted($"AgentCount:{AgentManager.Instance.AgentTable.Count}");
        }
        catch (Exception e)
        {
            ImGui.TextUnformatted(e.ToString());
        }

        ImGui.Separator();
        try
        {
            ImGui.TextUnformatted($"AgentPerformance: {Plugin.AgentPerformance.Pointer.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentPerformance")) ImGui.SetClipboardText($"{Plugin.AgentPerformance.Pointer.ToInt64():X}");

            ImGui.TextUnformatted(
                $"vtbl: {Plugin.AgentPerformance.VTable.ToInt64():X} +{Plugin.AgentPerformance.VTable.ToInt64() - Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentPerformancev")) ImGui.SetClipboardText($"{Plugin.AgentPerformance.VTable.ToInt64():X}");

            // TextUnformatted($"AgentID: {MidiBard.AgentPerformance.Id}");

            ImGui.TextUnformatted($"notePressed: {Plugin.AgentPerformance.notePressed}");
            ImGui.TextUnformatted($"noteNumber: {Plugin.AgentPerformance.noteNumber}");
            ImGui.TextUnformatted($"InPerformanceMode: {Plugin.AgentPerformance.InPerformanceMode}");
            ImGui.TextUnformatted(
                $"Timer1: {TimeSpan.FromMilliseconds(Plugin.AgentPerformance.PerformanceTimer1)}");
            ImGui.TextUnformatted(
                $"Timer2: {TimeSpan.FromTicks(Plugin.AgentPerformance.PerformanceTimer2 * 10)}");
        }
        catch (Exception e)
        {
            ImGui.TextUnformatted(e.ToString());
        }

        ImGui.Separator();

        try
        {
            ImGui.TextUnformatted($"AgentMetronome: {Plugin.AgentMetronome.Pointer.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentMetronome")) ImGui.SetClipboardText($"{Plugin.AgentMetronome.Pointer.ToInt64():X}");

            ImGui.TextUnformatted(
                $"vtbl: {Plugin.AgentMetronome.VTable.ToInt64():X} +{Plugin.AgentMetronome.VTable.ToInt64() - Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentMetronomev")) ImGui.SetClipboardText($"{Plugin.AgentMetronome.VTable.ToInt64():X}");

            ImGui.TextUnformatted($"Running: {Plugin.AgentMetronome.MetronomeRunning}");
            ImGui.TextUnformatted($"Ensemble: {Plugin.AgentMetronome.EnsembleModeRunning}");
            ImGui.TextUnformatted($"BeatsElapsed: {Plugin.AgentMetronome.MetronomeBeatsElapsed}");
            ImGui.TextUnformatted(
                $"PPQN: {Plugin.AgentMetronome.MetronomePPQN} ({60_000_000 / (double)Plugin.AgentMetronome.MetronomePPQN:F3}bpm)");
            ImGui.TextUnformatted($"BeatsPerBar: {Plugin.AgentMetronome.MetronomeBeatsPerBar}");
            ImGui.TextUnformatted(
                $"Timer1: {TimeSpan.FromMilliseconds(Plugin.AgentMetronome.MetronomeTimer1)}");
            ImGui.TextUnformatted(
                $"Timer2: {TimeSpan.FromTicks(Plugin.AgentMetronome.MetronomeTimer2 * 10)}");
        }
        catch (Exception e)
        {
            ImGui.TextUnformatted(e.ToString());
        }

        ImGui.Separator();

        try
        {
            var performInfos = Offsets.PerformanceStructPtr;
            ImGui.TextUnformatted($"PerformInfos: {performInfos.ToInt64() + 3:X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##PerformInfos")) ImGui.SetClipboardText($"{performInfos.ToInt64() + 3:X}");
            ImGui.TextUnformatted($"CurrentInstrumentKey: {Plugin.CurrentInstrument}");
            ImGui.TextUnformatted(
                $"Instrument: {Plugin.InstrumentSheet.GetRow(Plugin.CurrentInstrument).Instrument}");
            ImGui.TextUnformatted(
                $"Name: {Plugin.InstrumentSheet.GetRow(Plugin.CurrentInstrument).Name.ExtractText()}");
            ImGui.TextUnformatted($"Tone: {Plugin.AgentPerformance.CurrentGroupTone}");
            //ImGui.Text($"unkFloat: {UnkFloat}");
            ////ImGui.Text($"unkByte: {UnkByte1}");
        }
        catch (Exception e)
        {
            ImGui.TextUnformatted(e.ToString());
        }

        ImGui.Separator();
        ImGui.TextUnformatted($"currentPlaying: {Context.Plugin.PlaylistManager.CurrentSongIndex}");
        ImGui.TextUnformatted($"FilelistCount: {Context.Plugin.PlaylistManager.FilePathList.Count}");
        ImGui.TextUnformatted($"currentUILanguage: {DalamudApi.PluginInterface.UiLanguage}");

    }
}

