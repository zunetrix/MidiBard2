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
        try
        {
            // ImGui.Text($"AgentModule: {(long)AgentManager.Instance:X}");
            //ImGui.SameLine();
            // if (ImGui.SmallButton("C##AgentModule")) ImGui.SetClipboardText($"{(long)AgentManager.AgentModule:X}");
            // Text($"AgentCount:{AgentManager.Instance.AgentTable.Count}");
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }

        ImGui.Separator();
        try
        {
            ImGui.Text($"AgentPerformance: {Plugin.AgentPerformance.Pointer.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentPerformance")) ImGui.SetClipboardText($"{Plugin.AgentPerformance.Pointer.ToInt64():X}");

            ImGui.Text(
                $"vtbl: {Plugin.AgentPerformance.VTable.ToInt64():X} +{Plugin.AgentPerformance.VTable.ToInt64() - Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentPerformancev")) ImGui.SetClipboardText($"{Plugin.AgentPerformance.VTable.ToInt64():X}");

            // Text($"AgentID: {MidiBard.AgentPerformance.Id}");

            ImGui.Text($"notePressed: {Plugin.AgentPerformance.notePressed}");
            ImGui.Text($"noteNumber: {Plugin.AgentPerformance.noteNumber}");
            ImGui.Text($"InPerformanceMode: {Plugin.AgentPerformance.InPerformanceMode}");
            ImGui.Text(
                $"Timer1: {TimeSpan.FromMilliseconds(Plugin.AgentPerformance.PerformanceTimer1)}");
            ImGui.Text(
                $"Timer2: {TimeSpan.FromTicks(Plugin.AgentPerformance.PerformanceTimer2 * 10)}");
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }

        ImGui.Separator();

        try
        {
            ImGui.Text($"AgentMetronome: {Plugin.AgentMetronome.Pointer.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentMetronome")) ImGui.SetClipboardText($"{Plugin.AgentMetronome.Pointer.ToInt64():X}");

            ImGui.Text(
                $"vtbl: {Plugin.AgentMetronome.VTable.ToInt64():X} +{Plugin.AgentMetronome.VTable.ToInt64() - Process.GetCurrentProcess().MainModule.BaseAddress.ToInt64():X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##AgentMetronomev")) ImGui.SetClipboardText($"{Plugin.AgentMetronome.VTable.ToInt64():X}");

            ImGui.Text($"Running: {Plugin.AgentMetronome.MetronomeRunning}");
            ImGui.Text($"Ensemble: {Plugin.AgentMetronome.EnsembleModeRunning}");
            ImGui.Text($"BeatsElapsed: {Plugin.AgentMetronome.MetronomeBeatsElapsed}");
            ImGui.Text(
                $"PPQN: {Plugin.AgentMetronome.MetronomePPQN} ({60_000_000 / (double)Plugin.AgentMetronome.MetronomePPQN:F3}bpm)");
            ImGui.Text($"BeatsPerBar: {Plugin.AgentMetronome.MetronomeBeatsPerBar}");
            ImGui.Text(
                $"Timer1: {TimeSpan.FromMilliseconds(Plugin.AgentMetronome.MetronomeTimer1)}");
            ImGui.Text(
                $"Timer2: {TimeSpan.FromTicks(Plugin.AgentMetronome.MetronomeTimer2 * 10)}");
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }

        ImGui.Separator();

        try
        {
            var performInfos = Offsets.PerformanceStructPtr;
            ImGui.Text($"PerformInfos: {performInfos.ToInt64() + 3:X}");
            ImGui.SameLine();
            if (ImGui.SmallButton("C##PerformInfos")) ImGui.SetClipboardText($"{performInfos.ToInt64() + 3:X}");
            ImGui.Text($"CurrentInstrumentKey: {PerformanceState.CurrentInstrument}");
            ImGui.Text(
                $"Instrument: {InstrumentHelper.GetDisplayName(PerformanceState.CurrentInstrument)}");
            ImGui.Text(
                $"Name: {InstrumentHelper.InstrumentSheet.GetRow(PerformanceState.CurrentInstrument).Name.ExtractText()}");
            ImGui.Text($"Tone: {Plugin.AgentPerformance.CurrentGroupTone}");
            //ImGui.Text($"unkFloat: {UnkFloat}");
            ////ImGui.Text($"unkByte: {UnkByte1}");
        }
        catch (Exception e)
        {
            ImGui.Text(e.ToString());
        }

        ImGui.Separator();
        ImGui.Text($"currentPlaying: {Context.Plugin.PlaylistManager.CurrentSongIndex}");
        ImGui.Text($"FilelistCount: {Context.Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0}");
        ImGui.Text($"currentUILanguage: {DalamudApi.PluginInterface.UiLanguage}");

    }
}

