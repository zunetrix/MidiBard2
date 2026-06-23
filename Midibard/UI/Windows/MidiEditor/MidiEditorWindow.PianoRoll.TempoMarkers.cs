using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const uint TempoMarkerColor = 0xFFE0A030; // warm amber
    private const uint TimeSigMarkerColor = 0xFF30A0E0; // cool blue

    private void DrawTempoMarkers(PianoRenderContext ctx)
    {
        if (_file == null || _previewTempoMap == null) return;

        if (!ReferenceEquals(_tempoMarkerCacheFile, _file) || _tempoMarkerCacheVersion != _file.Version)
            RebuildTempoMarkerCache();

        if (_tempoMarkers.Count == 0) return;

        double viewStart = ctx.View.StartTime;
        double viewEnd = ctx.View.EndTime;

        foreach (var marker in _tempoMarkers)
        {
            double timeSec = marker.TimeSeconds;
            if (timeSec < viewStart - 1.0 || timeSec > viewEnd + 1.0) continue;

            float x = ctx.GetTimeX(timeSec);
            if (x < ctx.RollX || x > ctx.RollX + ctx.RollWidth) continue;

            ctx.DrawList.AddLine(
                new Vector2(x, ctx.Y),
                new Vector2(x, ctx.Y + ctx.Height),
                TempoMarkerColor,
                2f);

            var label = $"{marker.Bpm} BPM";
            var labelSize = ImGui.CalcTextSize(label);
            var labelPos = new Vector2(x + 3f, ctx.Y + 2f);
            ctx.DrawList.AddRectFilled(
                labelPos - new Vector2(1f, 1f),
                labelPos + labelSize + new Vector2(2f, 1f),
                0xA0000000);
            ctx.DrawList.AddText(labelPos, TempoMarkerColor, label);
        }
    }

    private void DrawTimeSignatureMarkers(PianoRenderContext ctx)
    {
        if (_file == null || _previewTempoMap == null) return;

        if (!ReferenceEquals(_timeSigMarkerCacheFile, _file) || _timeSigMarkerCacheVersion != _file.Version)
            RebuildTimeSigMarkerCache();

        if (_timeSigMarkers.Count == 0) return;

        double viewStart = ctx.View.StartTime;
        double viewEnd = ctx.View.EndTime;

        foreach (var marker in _timeSigMarkers)
        {
            double timeSec = marker.TimeSeconds;
            if (timeSec < viewStart - 1.0 || timeSec > viewEnd + 1.0) continue;

            float x = ctx.GetTimeX(timeSec);
            if (x < ctx.RollX || x > ctx.RollX + ctx.RollWidth) continue;

            ctx.DrawList.AddLine(
                new Vector2(x, ctx.Y),
                new Vector2(x, ctx.Y + ctx.Height),
                TimeSigMarkerColor,
                2f);

            var label = $"{marker.Numerator}/{marker.Denominator}";
            var labelSize = ImGui.CalcTextSize(label);
            var labelPos = new Vector2(x + 3f, ctx.Y + 2f + ImGui.CalcTextSize(" ").Y);
            ctx.DrawList.AddRectFilled(
                labelPos - new Vector2(1f, 1f),
                labelPos + labelSize + new Vector2(2f, 1f),
                0xA0000000);
            ctx.DrawList.AddText(labelPos, TimeSigMarkerColor, label);
        }
    }

    private void RebuildTempoMarkerCache()
    {
        if (_file == null || _previewTempoMap == null)
        {
            _tempoMarkers = Array.Empty<PreviewTempoMarker>();
            _tempoMarkerCacheFile = null;
            _tempoMarkerCacheVersion = -1;
            return;
        }

        var tmap = _previewTempoMap;
        var conductor = _file.Tracks.FirstOrDefault(track => track.IsConductorTrack);

        if (conductor is null)
        {
            _tempoMarkers = Array.Empty<PreviewTempoMarker>();
        }
        else
        {
            conductor.FlushChanges();
            _tempoMarkers = conductor.Chunk.GetTimedEvents()
                .Where(te => te.Event is SetTempoEvent)
                .Select(te => new PreviewTempoMarker(
                    TimeConverter.ConvertTo<MetricTimeSpan>(te.Time, tmap).TotalMicroseconds / 1_000_000.0,
                    (int)(60_000_000.0 / ((SetTempoEvent)te.Event).MicrosecondsPerQuarterNote),
                    te.Time))
                .ToList();
        }

        _tempoMarkerCacheFile = _file;
        _tempoMarkerCacheVersion = _file.Version;
    }

    private void RebuildTimeSigMarkerCache()
    {
        if (_file == null || _previewTempoMap == null)
        {
            _timeSigMarkers = Array.Empty<PreviewTimeSignatureMarker>();
            _timeSigMarkerCacheFile = null;
            _timeSigMarkerCacheVersion = -1;
            return;
        }

        var tmap = _previewTempoMap;
        var conductor = _file.Tracks.FirstOrDefault(track => track.IsConductorTrack);

        if (conductor is null)
        {
            _timeSigMarkers = Array.Empty<PreviewTimeSignatureMarker>();
        }
        else
        {
            conductor.FlushChanges();
            _timeSigMarkers = conductor.Chunk.GetTimedEvents()
                .Where(te => te.Event is TimeSignatureEvent)
                .Select(te =>
                {
                    var ts = (TimeSignatureEvent)te.Event;
                    return new PreviewTimeSignatureMarker(
                        TimeConverter.ConvertTo<MetricTimeSpan>(te.Time, tmap).TotalMicroseconds / 1_000_000.0,
                        ts.Numerator,
                        ts.Denominator,
                        te.Time);
                })
                .ToList();
        }

        _timeSigMarkerCacheFile = _file;
        _timeSigMarkerCacheVersion = _file.Version;
    }
}
