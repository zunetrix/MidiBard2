using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public partial class PianoRollWindow
{
    // Time grid line cache (invalidated by tempo map or viewport change)
    private TempoMap? _gridCacheTempoMap;
    private double _gridCacheStartTime;
    private double _gridCacheEndTime;
    private float _gridCachePixelsPerSecond;
    private float _gridCacheRollX;
    private IReadOnlyList<TimeGridLine> _cachedGridLines = Array.Empty<TimeGridLine>();

    private readonly record struct TimeGridLine(double TimeSeconds, bool IsBar, bool IsBeat, bool IsSub, string? Label);

    internal void DrawTimeGrid(PianoRenderContext ctx, TempoMap? tempoMap, PianoRollState state)
    {
        if (tempoMap == null) return;

        var view = ctx.View;
        if (_gridCacheTempoMap != tempoMap
            || Math.Abs(_gridCacheStartTime - view.StartTime) > 0.001
            || Math.Abs(_gridCacheEndTime - view.EndTime) > 0.001
            || Math.Abs(_gridCachePixelsPerSecond - view.PixelsPerSecond) > 0.01f
            || Math.Abs(_gridCacheRollX - ctx.RollX) > 0.5f)
        {
            RebuildGridLineCache(ctx, tempoMap, state);
        }

        DrawCachedGridLines(ctx, state);
    }

    private void RebuildGridLineCache(PianoRenderContext ctx, TempoMap tempoMap, PianoRollState state)
    {
        var lines = new List<TimeGridLine>();
        double viewStart = ctx.View.StartTime;
        double viewEnd = ctx.View.EndTime;

        long startTicks = TimeConverter.ConvertFrom(viewStart.ToMetricTimeSpan(), tempoMap);
        long endTicks = TimeConverter.ConvertFrom(viewEnd.ToMetricTimeSpan(), tempoMap);

        var startBarSpan = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(startTicks, tempoMap);
        int currentBar = (int)startBarSpan.Bars;

        // Collect bars and beats
        while (true)
        {
            var barTime = new BarBeatTicksTimeSpan(currentBar, 0);
            long barTicks = TimeConverter.ConvertFrom(barTime, tempoMap);

            if (barTicks > endTicks)
                break;

            var barMetric = TimeConverter.ConvertTo<MetricTimeSpan>(barTicks, tempoMap);
            double barSeconds = barMetric.TotalMicroseconds / 1_000_000.0;

            if (barSeconds >= viewStart)
                lines.Add(new TimeGridLine(barSeconds, true, false, false, (currentBar + 1).ToString()));

            if (state.BeatDivision != BeatSubdivision.Bars)
            {
                var timeSignature = tempoMap.GetTimeSignatureAtTime(barMetric);
                int beatsPerBar = timeSignature.Numerator;
                int subdivisionFactor = (int)state.BeatDivision;

                for (int beat = 0; beat < beatsPerBar; beat++)
                {
                    var beatTime = new BarBeatTicksTimeSpan(currentBar, beat);
                    long beatTicks = TimeConverter.ConvertFrom(beatTime, tempoMap);

                    if (beatTicks > endTicks)
                        break;

                    var beatMetric = TimeConverter.ConvertTo<MetricTimeSpan>(beatTicks, tempoMap);
                    double beatSeconds = beatMetric.TotalMicroseconds / 1_000_000.0;

                    if (beatSeconds < viewStart)
                        continue;

                    lines.Add(new TimeGridLine(beatSeconds, false, true, false, null));

                    if (subdivisionFactor > 1)
                    {
                        double nextBeatSeconds = GetNextBeatSeconds(tempoMap, currentBar, beat, beatsPerBar);
                        double beatDuration = nextBeatSeconds - beatSeconds;
                        double step = beatDuration / subdivisionFactor;

                        for (int s = 1; s < subdivisionFactor; s++)
                        {
                            double subSec = beatSeconds + (step * s);
                            if (subSec < viewStart || subSec > viewEnd)
                                continue;
                            lines.Add(new TimeGridLine(subSec, false, false, true, null));
                        }
                    }
                }
            }

            currentBar++;
        }

        // Collect second overlay
        if (state.ShowSeconds)
        {
            int startSec = (int)Math.Floor(viewStart);
            int endSec = (int)Math.Ceiling(viewEnd);

            for (int sec = startSec; sec <= endSec; sec++)
            {
                var metric = sec.ToMetricTimeSpan();
                long ticks = TimeConverter.ConvertFrom(metric, tempoMap);
                var metricBack = TimeConverter.ConvertTo<MetricTimeSpan>(ticks, tempoMap);
                double exactSeconds = metricBack.TotalMicroseconds / 1_000_000.0;

                int minutes = sec / 60;
                int seconds = sec % 60;
                lines.Add(new TimeGridLine(exactSeconds, false, false, false, $"{minutes:D1}:{seconds:D2}"));
            }
        }

        _cachedGridLines = lines;
        _gridCacheTempoMap = tempoMap;
        _gridCacheStartTime = viewStart;
        _gridCacheEndTime = viewEnd;
        _gridCachePixelsPerSecond = ctx.View.PixelsPerSecond;
        _gridCacheRollX = ctx.RollX;
    }

    private void DrawCachedGridLines(PianoRenderContext ctx, PianoRollState state)
    {
        uint beatColor = state.GridLineColorU32;
        uint subColor = state.GridSubColorU32;

        foreach (var line in _cachedGridLines)
        {
            float x = ctx.GetTimeX(line.TimeSeconds);

            if (line.IsBar)
            {
                ctx.DrawList.AddLine(
                    new Vector2(x, ctx.Y),
                    new Vector2(x, ctx.Y + ctx.Height),
                    BarLineU32,
                    2f);

                if (line.Label != null)
                    ctx.DrawList.AddText(new Vector2(x + 4, ctx.Y + 4), 0xFFFFFFFF, line.Label);
            }
            else if (line.IsBeat)
            {
                ctx.DrawList.AddLine(
                    new Vector2(x, ctx.Y),
                    new Vector2(x, ctx.Y + ctx.Height),
                    beatColor,
                    line.IsSub ? 1f : 3f);
            }
            else if (line.IsSub)
            {
                ctx.DrawList.AddLine(
                    new Vector2(x, ctx.Y),
                    new Vector2(x, ctx.Y + ctx.Height),
                    subColor,
                    1f);
            }
            else // second
            {
                ctx.DrawList.AddLine(
                    new Vector2(x, ctx.Y),
                    new Vector2(x, ctx.Y + ctx.Height),
                    SecondLineU32);

                if (line.Label != null)
                    ctx.DrawList.AddText(new Vector2(x + 3, ctx.Y + ctx.Height - 18), 0x88FFFFFF, line.Label);
            }
        }
    }

    private static double GetNextBeatSeconds(TempoMap tempoMap, int barIndex, int beatIndex, int beatsPerBar)
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
}
