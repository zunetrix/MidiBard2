using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Resources;
using Dalamud.Interface;
using MidiBard.Extensions.Time;
using MidiBard.Playlist;

namespace MidiBard;

public class TrackVisualizerWindow : Window
{
    private Plugin Plugin { get; }
    private bool setNextLimit;
    private (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] _plotData;

    private readonly string[] noteNames = Enumerable.Range(0, 128)
        .Select(i => i % 12 == 0 ? new Note(new SevenBitNumber((byte)i)).ToString() : string.Empty)
        .ToArray();

    public TrackVisualizerWindow(Plugin plugin) : base($"{Language.window_title_visualizor}###TrackVisualizerWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;

        ImPlot.SetImGuiContext(ImGui.GetCurrentContext());
        var _context = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(_context);

        UpdateWindowConfig();
    }

    public override void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Style.Components.FrameBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Style.Components.FrameBg);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, -Vector2.One);
        ImGui.SetNextWindowBgAlpha(0);
        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(640, 480), ImGuiCond.FirstUseEver);
        ImGui.PopStyleVar();

        DrawMidiPlot();

        ImGui.PopStyleColor(2);
    }

    public void ResetPosition()
    {
        // var WindowSizeConstraints = new WindowSizeConstraints();
        // WindowSizeConstraints.MinimumSize = new Vector2(ImGuiHelpers.GlobalScale * 640, 0);
        // WindowSizeConstraints.MaximumSize = new Vector2(ImGuiHelpers.GlobalScale * 480, float.MaxValue);
        // SizeConstraints = WindowSizeConstraints;

        // ImGui.SetNextWindowPos(new Vector2(100), ImGuiCond.Always);
        // ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(640, 480), ImGuiCond.Always);
    }

    private void DrawMidiPlot()
    {
        if (IsOpen)
        {
            RefreshPlotData();
        }

        double timelinePos = 0;
        double? ensembleTimelinePos = null;

        try
        {
            if (Plugin.CurrentBardPlayback.IsLoaded)
            {
                timelinePos = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>().GetTotalSeconds();
                if (Plugin.Config.UseEnsembleIndicator && Plugin.EnsembleManager.EnsembleRunning)
                    ensembleTimelinePos = timelinePos + Plugin.Config.EnsembleIndicatorDelay - Plugin.EnsembleManager.GetCompensationNew(Plugin.CurrentInstrumentWithTone, -1) * 0.001d;
            }
        }
        catch
        {
            // ignored
        }

        string songName = string.Empty;
        try
        {
            songName = Plugin.PlaylistManager.CurrentPlayingSong?.GetFileName() ?? string.Empty;
        }
        catch
        {
            // ignored
        }

        //ImGui.SetCursorPos(ImGui.GetWindowContentRegionMin());
        if (ImPlot.BeginPlot(songName + "###midiTrackPlot", ImGuiUtil.GetWindowContentRegion(), ImPlotFlags.NoTitle))
        {
            ImPlot.SetupAxisLimits(ImAxis.X1, 0, 20, ImPlotCond.Once);
            ImPlot.SetupAxisLimits(ImAxis.Y1, 42, 91, ImPlotCond.Once);
            ImPlot.SetupAxisTicks(ImAxis.Y1, 0, 127, 128, noteNames, false);

            if (setNextLimit)
            {
                try
                {
                    if (!Plugin.Config.LockPlot)
                        ImPlot.SetupAxisLimits(ImAxis.X1, 0, _plotData.Max(i => i.trackInfo.DurationMetric.GetTotalSeconds()), ImPlotCond.Always);

                    setNextLimit = false;
                }
                catch (Exception e)
                {
                    DalamudApi.PluginLog.Error(e, "error when try set next plot limit");
                }
            }

            if (Plugin.Config.LockPlot)
            {
                var imPlotRange = ImPlot.GetPlotLimits(ImAxis.X1).X;
                var d = (imPlotRange.Max - imPlotRange.Min) / 2;
                if (ensembleTimelinePos is not null)
                {
                    ImPlot.SetupAxisLimits(ImAxis.X1, (double)ensembleTimelinePos - d, (double)ensembleTimelinePos + d, ImPlotCond.Always);
                }
                else
                {
                    ImPlot.SetupAxisLimits(ImAxis.X1, timelinePos - d, timelinePos + d, ImPlotCond.Always);
                }
            }

            var drawList = ImPlot.GetPlotDrawList();
            var xMin = ImPlot.GetPlotLimits().X.Min;
            var xMax = ImPlot.GetPlotLimits().X.Max;

            //if (!MidiBard.Plugin.Config.LockPlot) timeWindow = (xMax - xMin) / 2;

            ImPlot.PushPlotClipRect();

            var cp = ImGuiColors.ParsedBlue;
            cp.W = 0.05f;
            drawList.AddRectFilled(ImPlot.PlotToPixels(xMin, 48 + 37), ImPlot.PlotToPixels(xMax, 48), ImGui.ColorConvertFloat4ToU32(cp));

            if (_plotData?.Any() == true && Plugin.CurrentBardPlayback.IsLoaded)
            {
                var legendInfoList = new List<(string trackName, Vector4 color, int index)>();

                foreach (var (trackInfo, notes) in _plotData.OrderBy(i => i.trackInfo.IsPlaying))
                {
                    var noteColor = GetTrackColor(trackInfo.Index);
                    var noteColorRgb = ImGui.ColorConvertFloat4ToU32(noteColor);

                    legendInfoList.Add(($"[{trackInfo.Index + 1:00}] {trackInfo.TrackName}", noteColor, trackInfo.Index));


                    foreach (var (start, end, noteNumber) in notes.Where(i => i.end > xMin && i.start < xMax))
                    {
                        var translatedNoteNum =
                            Plugin.BardPlayDevice.GetNoteNumberTranslatedByTrack(noteNumber, trackInfo.Index) + 48;
                        drawList.AddRectFilled(
                            ImPlot.PlotToPixels(start, translatedNoteNum + 1),
                            ImPlot.PlotToPixels(end, translatedNoteNum),
                            noteColorRgb, 4);
                    }
                }

                foreach (var (trackName, color, _) in legendInfoList.OrderBy(i => i.index))
                {
                    ImPlot.SetNextLineStyle(color);
                    var f = double.NegativeInfinity;
                    ImPlot.PlotLine(trackName, ref f, 1);
                }
            }

            DrawCurrentPlayTime(drawList, timelinePos);
            if (ensembleTimelinePos is not null)
            {
                DrawEnsemblePlayTime(drawList, (double)ensembleTimelinePos);
            }
            ImPlot.PopPlotClipRect();

            ImPlot.EndPlot();
        }
    }

    private unsafe Vector4 GetTrackColor(int index)
    {
        int total = Plugin.CurrentBardPlayback?.TrackInfos?.Length ?? 1;
        Vector4 c = Vector4.One;
        ImGui.ColorConvertHSVtoRGB(index / (float)Math.Max(1, total), 0.8f, 1f, &c.X, &c.Y, &c.Z);
        return c;
    }

    private static void DrawCurrentPlayTime(ImDrawListPtr drawList, double timelinePos)
    {
        drawList.AddLine(
            ImPlot.PlotToPixels(timelinePos, ImPlot.GetPlotLimits().Y.Min),
            ImPlot.PlotToPixels(timelinePos, ImPlot.GetPlotLimits().Y.Max),
            ImGui.ColorConvertFloat4ToU32(ImGuiColors.DalamudRed),
            ImGuiHelpers.GlobalScale);
    }

    private static void DrawEnsemblePlayTime(ImDrawListPtr drawList, double timelinePos)
    {
        drawList.AddLine(
            ImPlot.PlotToPixels(timelinePos, ImPlot.GetPlotLimits().Y.Min),
            ImPlot.PlotToPixels(timelinePos, ImPlot.GetPlotLimits().Y.Max),
            ImGui.ColorConvertFloat4ToU32(ImGuiColors.DalamudYellow),
            ImGuiHelpers.GlobalScale);
    }

    public void RefreshPlotData()
    {
        Task.Run(() =>
        {
            try
            {
                if (Plugin.CurrentBardPlayback?.TrackInfos == null)
                {
                    DalamudApi.PluginLog.Debug("try RefreshPlotData but CurrentTracks is null");
                    return;
                }

                var tmap = Plugin.CurrentBardPlayback.TempoMap;

                _plotData = Plugin.CurrentBardPlayback.TrackChunks.Select((trackChunk, index) =>
                    {
                        var trackNotes = trackChunk.GetNotes()
                            .Select(j => (j.TimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(),
                                j.EndTimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(), (int)j.NoteNumber))
                            .ToArray();

                        return (Plugin.CurrentBardPlayback.TrackInfos[index], notes: trackNotes);
                    })
                    .ToArray();

                setNextLimit = true;
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error when refreshing plot data");
            }
        });
    }

    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;

        TitleBarButtons.Clear();
        TitleBarButtons.Add(new TitleBarButton()
        {
            AvailableClickthrough = false,
            Icon = Plugin.Config.LockPlot ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            ShowTooltip = () => ImGuiUtil.ToolTip(Language.icon_button_tooltip_visualizer_follow_playback_tooltip),
            Click = _ =>
            {
                Plugin.Config.LockPlot ^= true;
                this.UpdateWindowConfig();
            }
        });
    }
}
