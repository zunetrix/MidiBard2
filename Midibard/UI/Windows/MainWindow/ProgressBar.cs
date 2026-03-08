using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard;

public partial class MainWindow
{
    private const float ProgressBarHeight = 3f;

    private void ProgressBar()
    {
        MetricTimeSpan currentTime = new MetricTimeSpan(0);
        MetricTimeSpan duration = new MetricTimeSpan(0);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Plugin.FilePlayback.IsWaiting ? Style.Colors.White : Plugin.Config.themeColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Plugin.Config.themeColorDark);

        if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            ImGui.ProgressBar(0, new Vector2(-1, ProgressBarHeight));

            DrawTimeLabels(currentTime, duration);
            ImGui.PopStyleColor(2);
            return;
        }

        currentTime = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>();
        duration = Plugin.CurrentBardPlayback.GetDuration<MetricTimeSpan>();
        var progress = Plugin.CurrentBardPlayback.GetPlaybackProgress();
        ImGui.ProgressBar(progress, ImGuiHelpers.ScaledVector2(-1, ProgressBarHeight));

        ImGui.PopStyleColor(2);

        DrawTimeLabels(currentTime, duration);

        if (Plugin.AgentMetronome.EnsembleModeRunning)
        {
            DrawEnsembleLabel();
        }
        else
        {
            DrawInstrumentLabel();
        }
    }
}
