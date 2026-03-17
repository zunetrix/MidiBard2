using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public partial class PianoRollWindow
{
    internal void DrawTimeGrid(PianoRenderContext ctx, TempoMap? tempoMap, PianoRollState state)
    {
        if (tempoMap == null) return;

        DrawBarsAndBeats(ctx, tempoMap, state);

        if (state.ShowSeconds)
            DrawSecondOverlay(ctx, tempoMap);
    }

    private void DrawBarsAndBeats(PianoRenderContext ctx, TempoMap tempoMap, PianoRollState state)
    {
        double viewStart = ctx.View.StartTime;
        double viewEnd = ctx.View.EndTime;

        // Pre-compute beat/subdivision colors once per method call instead of per bar/beat
        uint beatColor = ImGui.ColorConvertFloat4ToU32(state.GridLineColor);
        var gl = state.GridLineColor;
        uint subColor = ImGui.ColorConvertFloat4ToU32(new Vector4(gl.X, gl.Y, gl.Z, 0.35f));

        long startTicks = TimeConverter.ConvertFrom(viewStart.ToMetricTimeSpan(), tempoMap);
        long endTicks = TimeConverter.ConvertFrom(viewEnd.ToMetricTimeSpan(), tempoMap);

        var startBarSpan = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(startTicks, tempoMap);
        int currentBar = (int)startBarSpan.Bars;

        while (true)
        {
            var barTime = new BarBeatTicksTimeSpan(currentBar, 0);
            long barTicks = TimeConverter.ConvertFrom(barTime, tempoMap);

            if (barTicks > endTicks)
                break;

            var barMetric = TimeConverter.ConvertTo<MetricTimeSpan>(barTicks, tempoMap);
            double barSeconds = barMetric.TotalMicroseconds / 1_000_000.0;

            if (barSeconds >= viewStart)
                DrawBar(ctx, barSeconds, currentBar);

            DrawBeats(ctx, tempoMap, currentBar, barMetric, endTicks, beatColor, subColor, state);

            currentBar++;
        }
    }

    private void DrawBar(PianoRenderContext ctx, double seconds, int barIndex)
    {
        float x = ctx.GetTimeX(seconds);

        ctx.DrawList.AddLine(
            new Vector2(x, ctx.Y),
            new Vector2(x, ctx.Y + ctx.Height),
            BarLineU32,
            2f);

        ctx.DrawList.AddText(
            new Vector2(x + 4, ctx.Y + 4),
            0xFFFFFFFF,
            (barIndex + 1).ToString());
    }

    private void DrawBeats(
        PianoRenderContext ctx,
        TempoMap tempoMap,
        int barIndex,
        MetricTimeSpan barMetric,
        long endTicks,
        uint beatColor,
        uint subColor,
        PianoRollState state)
    {
        // bar dont show beats subdivision
        if (state.BeatDivision == BeatSubdivision.Bars)
            return;

        var timeSignature = tempoMap.GetTimeSignatureAtTime(barMetric);
        int beatsPerBar = timeSignature.Numerator;

        int subdivisionFactor = (int)state.BeatDivision;

        for (int beat = 0; beat < beatsPerBar; beat++)
        {
            var beatTime = new BarBeatTicksTimeSpan(barIndex, beat);
            long beatTicks = TimeConverter.ConvertFrom(beatTime, tempoMap);

            if (beatTicks > endTicks)
                break;

            var beatMetric = TimeConverter.ConvertTo<MetricTimeSpan>(beatTicks, tempoMap);
            double beatSeconds = beatMetric.TotalMicroseconds / 1_000_000.0;

            if (beatSeconds < ctx.View.StartTime)
                continue;

            float x = ctx.GetTimeX(beatSeconds);

            // main measure division
            ctx.DrawList.AddLine(
                new Vector2(x, ctx.Y),
                new Vector2(x, ctx.Y + ctx.Height),
                beatColor,
                beat == 0 ? 3f : 1f);

            if (subdivisionFactor > 1)
            {
                DrawSubdivisions(ctx, tempoMap, barIndex, beat, beatsPerBar, beatSeconds, subdivisionFactor, subColor);
            }
        }
    }

    private void DrawSubdivisions(
        PianoRenderContext ctx,
        TempoMap tempoMap,
        int barIndex,
        int beatIndex,
        int beatsPerBar,
        double beatSeconds,
        int subdivisionFactor,
        uint subColor)
    {
        if (subdivisionFactor <= 1)
            return;

        double nextBeatSeconds = GetNextBeatSeconds(tempoMap, barIndex, beatIndex, beatsPerBar);
        double beatDuration = nextBeatSeconds - beatSeconds;
        double step = beatDuration / subdivisionFactor;

        for (int s = 1; s < subdivisionFactor; s++)
        {
            double subSec = beatSeconds + (step * s);

            if (subSec < ctx.View.StartTime || subSec > ctx.View.EndTime)
                continue;

            float x = ctx.GetTimeX(subSec);

            ctx.DrawList.AddLine(
                new Vector2(x, ctx.Y),
                new Vector2(x, ctx.Y + ctx.Height),
                subColor,
                1f);
        }
    }

    private double GetNextBeatSeconds(TempoMap tempoMap, int barIndex, int beatIndex, int beatsPerBar)
    {
        BarBeatTicksTimeSpan nextTime;

        if (beatIndex < beatsPerBar - 1)
            nextTime = new BarBeatTicksTimeSpan(barIndex, beatIndex + 1);
        else
            nextTime = new BarBeatTicksTimeSpan(barIndex + 1, 0);

        long nextTicks = TimeConverter.ConvertFrom(nextTime, tempoMap);
        var nextMetric = TimeConverter.ConvertTo<MetricTimeSpan>(nextTicks, tempoMap);

        return nextMetric.TotalMicroseconds / 1_000_000.0;
    }

    private void DrawSecondOverlay(PianoRenderContext ctx, TempoMap tempoMap)
    {
        int startSec = (int)Math.Floor(ctx.View.StartTime);
        int endSec = (int)Math.Ceiling(ctx.View.EndTime);

        for (int sec = startSec; sec <= endSec; sec++)
        {
            var metric = sec.ToMetricTimeSpan();
            long ticks = TimeConverter.ConvertFrom(metric, tempoMap);
            var metricBack = TimeConverter.ConvertTo<MetricTimeSpan>(ticks, tempoMap);

            double exactSeconds = metricBack.TotalMicroseconds / 1_000_000.0;

            float x = ctx.GetTimeX(exactSeconds);

            ctx.DrawList.AddLine(
                new Vector2(x, ctx.Y),
                new Vector2(x, ctx.Y + ctx.Height),
                SecondLineU32);

            int minutes = sec / 60;
            int seconds = sec % 60;
            ctx.DrawList.AddText(
                new Vector2(x + 3, ctx.Y + ctx.Height - 18),
                0x88FFFFFF,
                $"{minutes:D1}:{seconds:D2}");
        }
    }
}
