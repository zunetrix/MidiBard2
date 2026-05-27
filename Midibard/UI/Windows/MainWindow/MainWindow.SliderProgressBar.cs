using System;
using System.Linq;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;
using MidiBard.Resources;

using MidiBard.Control;
using MidiBard.Util;

namespace MidiBard;

public partial class MainWindow
{
    private void SliderProgressBar()
    {
        MetricTimeSpan currentTime = new MetricTimeSpan(0);
        MetricTimeSpan duration = new MetricTimeSpan(0);

        if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            float zero = 0;

            InstrumentPickerSolo();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderFloat("##SetSliderProgressBar", ref zero, 0, 1, "0:00 / 0:00", ImGuiSliderFlags.NoInput);
            ImGuiUtil.ToolTip(Language.setting_perf_progress_tooltip);

            DrawTimeLabels(currentTime, duration);
            return;
        }

        currentTime = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
        duration = Plugin.CurrentBardPlayback.GetDuration<MetricTimeSpan>();

        float progress = currentTime.SafeDivideMetricTimeSpan(duration);

        InstrumentPickerSolo();
        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##SliderProgressBar", ref progress, 0, 1,
        $"{(currentTime.Hours != 0 ? currentTime.Hours + ":" : "")}{currentTime.Minutes:00}:{currentTime.Seconds:00} / {(duration.Hours != 0 ? duration.Hours + ":" : "")}{duration.Minutes:00}:{duration.Seconds:00}",
        ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.NoRoundToFormat))
        {
            var newTime = duration.Multiply(progress);
            Plugin.MidiPlayerControl.SetTime(newTime);
            Plugin.IpcProvider.SetPlaybackTime((MetricTimeSpan)newTime);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Plugin.MidiPlayerControl.SetTime(duration.Multiply(0));
            Plugin.IpcProvider.SetPlaybackTime(TimeSpan.Zero);
        }
        ImGuiUtil.ToolTip(Language.setting_perf_progress_tooltip);

        DrawTimeLabels(currentTime, duration);

        if (AgentManager.AgentMetronome.EnsembleModeRunning)
        {
            DrawEnsembleLabel();
        }
        else
        {
            DrawInstrumentLabel();
        }
    }

    private void DrawTimeLabels(MetricTimeSpan current, MetricTimeSpan total)
    {
        ImGui.Text($"{current.Hours}:{current.Minutes:00}:{current.Seconds:00}");

        string durationText = $"{total.Hours}:{total.Minutes:00}:{total.Seconds:00}";
        ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.CalcTextSize(durationText).X + ImGui.GetCursorPosX());
        ImGui.Text(durationText);
    }

    private void DrawInstrumentLabel()
    {
        var isAuto = PerformanceState.PlayingGuitar && Plugin.Config.GuitarToneMode != GuitarToneMode.OverrideByTrack;
        var instrumentId = isAuto
            ? (uint)(InstrumentHelper.GuitarGroup[0] + AgentManager.AgentPerformance.CurrentGroupTone)
            : PerformanceState.CurrentInstrument;

        if (instrumentId == 0)
            return;

        var instrumentName = InstrumentHelper.GetDisplayName(instrumentId);
        if (isAuto)
            instrumentName = instrumentName.Split(':', '：').First() + ": Auto";

        ImGui.SameLine((ImGuiUtil.GetWindowContentRegionWidth() - ImGui.CalcTextSize(instrumentName).X) / 2);
        ImGui.Text(instrumentName);
    }

    private void DrawEnsembleLabel()
    {
        var ensembleText = $"{Language.main_status_ensemble_running} {Plugin.EnsembleManager.EnsembleTimer.Elapsed:mm\\:ss\\:ff}";
        ImGui.SameLine((ImGuiUtil.GetWindowContentRegionWidth() - ImGui.CalcTextSize(ensembleText).X) / 2);
        ImGui.TextColored(Plugin.Config.themeColor, ensembleText);
    }
}
