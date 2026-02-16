using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{

    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;
    }

    private void FollowPlaybackCursor(float width, float pixelsPerSecond, double timelinePos)
    {
        if (_autoFollowPlayback)
        {
            double visibleTime = width / pixelsPerSecond;
            _cameraTime = timelinePos - visibleTime * 0.3; // offset cursor left

            if (_cameraTime < 0)
                _cameraTime = 0;
        }
    }

    private unsafe Vector4 GetTrackColor(int index)
    {
        Vector4 c = Vector4.One;
        try
        {
            var denom = Plugin.CurrentBardPlayback?.TrackInfos?.Length ?? 1;
            var safeDenom = Math.Max(1, denom);
            float hue = index / (float)safeDenom;
            ImGui.ColorConvertHSVtoRGB(hue, 0.8f, 1, &c.X, &c.Y, &c.Z);
        }
        catch
        {
            // fallback to white
            c = Vector4.One;
        }

        return c;
    }

    private void DrawTrackMenu()
    {
        ImGui.Text("Tracks");
        if (_plotData == null) return;

        // ensure visibility array length
        var maxIndex = Math.Max(_plotData.Length, Plugin.CurrentBardPlayback?.TrackInfos?.Length ?? 0);
        if (_trackVisible == null || _trackVisible.Length < maxIndex)
        {
            _trackVisible = new bool[maxIndex];
            for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = false;

            // initialize based on MidiFileConfig when available
            try
            {
                var cfgTracks = Plugin.CurrentBardPlayback?.MidiFileConfig?.Tracks;
                if (cfgTracks != null && cfgTracks.Count > 0)
                {
                    // default to false, only enable tracks explicitly enabled in config
                    foreach (var cfgTrack in cfgTracks)
                    {
                        if (cfgTrack == null) continue;
                        var idx = cfgTrack.Index;
                        if (idx >= 0 && idx < _trackVisible.Length)
                            _trackVisible[idx] = cfgTrack.Enabled && cfgTrack.AssignedCids.Count >= 1;
                    }
                }
                else
                {
                    // no config: show all tracks
                    for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = true;
                }
            }
            catch
            {
                // ignore and fallback to show all
                for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = true;
            }
        }

        for (int i = 0; i < _plotData.Length; i++)
        {
            var tinfo = _plotData[i].trackInfo;
            bool visible = (tinfo.Index < _trackVisible.Length) ? _trackVisible[tinfo.Index] : true;
            var color = GetTrackColor(tinfo.Index);
            ImGui.ColorButton($"##col{tinfo.Index}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16));
            ImGui.SameLine();
            if (ImGui.Checkbox($"[{tinfo.Index + 1:00}] {tinfo.TrackName}", ref visible))
            {
                if (tinfo.Index < _trackVisible.Length) _trackVisible[tinfo.Index] = visible;
            }
        }
    }

    private enum GridSubdivision
    {
        Beat,        // 1/4
        Eighth,      // 1/8
        Sixteenth    // 1/16
    }


    // private void DrawTimeGrid(PianoRenderContext ctx)
    // {
    //     if (!Plugin.CurrentBardPlayback.IsLoaded)
    //         return;

    //     var tempoMap = Plugin.CurrentBardPlayback.TempoMap;

    //     // Converter início/fim visível para MetricTimeSpan
    //     var startMetric = MetricTimeSpan.FromSeconds(ctx.View.StartTime);
    //     var endMetric = MetricTimeSpan.FromSeconds(ctx.View.EndTime);

    //     // Converter para tempo musical (Beats)
    //     var startBeat = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(startMetric, tempoMap);
    //     var endBeat = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(endMetric, tempoMap);

    //     int subdivisionDivider = _gridSubdivision switch
    //     {
    //         GridSubdivision.Beat => 1,
    //         GridSubdivision.Eighth => 2,
    //         GridSubdivision.Sixteenth => 4,
    //         _ => 1
    //     };

    //     // Começa do primeiro beat inteiro visível
    //     int currentBar = startBeat.Bars;
    //     int currentBeat = startBeat.Beats;

    //     // Vamos iterar manualmente
    //     for (var musical = startBeat;
    //          musical < endBeat;
    //          musical = musical.Add(new BarBeatTicksTimeSpan(0, 1, 0)))
    //     {
    //         // Subdivisão
    //         for (int s = 0; s < subdivisionDivider; s++)
    //         {
    //             var subdivBeat = musical.Add(
    //                 new BarBeatTicksTimeSpan(
    //                     0,
    //                     0,
    //                     tempoMap.TimeDivision is TicksPerQuarterNoteTimeDivision tpq
    //                         ? tpq.TicksPerQuarterNote / subdivisionDivider * s
    //                         : 0));

    //             // Converter de volta para segundos
    //             var metric = TimeConverter.ConvertTo<MetricTimeSpan>(subdivBeat, tempoMap);
    //             double seconds = metric.GetTotalSeconds();

    //             if (seconds < ctx.View.StartTime || seconds > ctx.View.EndTime)
    //                 continue;

    //             float lineX = ctx.X +
    //                 (float)((seconds - ctx.View.StartTime) * ctx.View.PixelsPerSecond);

    //             bool isBarStart = subdivBeat.Beats == 0 && subdivBeat.Ticks == 0;
    //             bool isBeatStart = subdivBeat.Ticks == 0;

    //             uint color = ImGui.ColorConvertFloat4ToU32(
    //                 isBarStart
    //                     ? gridLine * 1.5f
    //                     : isBeatStart
    //                         ? gridLine * 1.2f
    //                         : gridLine);

    //             float thickness = isBarStart ? 3f :
    //                               isBeatStart ? 2f : 1f;

    //             ctx.DrawList.AddLine(
    //                 new Vector2(lineX, ctx.Y),
    //                 new Vector2(lineX, ctx.Y + ctx.Height),
    //                 color,
    //                 thickness);

    //             // Mostrar número do compasso
    //             if (isBarStart)
    //             {
    //                 ctx.DrawList.AddText(
    //                     new Vector2(lineX + 4, ctx.Y + 4),
    //                     ImGui.ColorConvertFloat4ToU32(Vector4.One),
    //                     $"Bar {subdivBeat.Bars + 1}");
    //             }
    //         }
    //     }
    // }

}
