using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Util;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawProgramChangeMarkers(PianoRenderContext ctx)
    {
        if (_file == null || _previewTempoMap == null || _previewTracks == null) return;

        // Rebuild cache when file changes
        if (!ReferenceEquals(_pcMarkerCacheFile, _file) || _pcMarkerCacheVersion != _file.Version)
            RebuildPcMarkerCache();

        // Collect deferred icon draws (must happen after draw list setup)
        var iconsToRender = new List<(Vector2 pos, uint iconId)>();
        const float markerAlpha = 0.55f;
        float iconSize = _previewState.NoteMinHeight * 2f;
        iconSize = Math.Clamp(iconSize, 14f, 32f) * ImGuiHelpers.GlobalScale;

        double viewStart = ctx.View.StartTime;
        double viewEnd = ctx.View.EndTime;

        for (int ti = 0; ti < _file.Tracks.Count; ti++)
        {
            var editTrack = _file.Tracks[ti];
            if (editTrack.IsConductorTrack) continue;

            var displayState = (ti < _previewTracks.Length) ? _previewTracks[ti] : null;
            if (displayState != null && !displayState.Visible) continue;

            var trackColor = displayState?.Color ?? PianoRollWindow.GetTrackColor(ti, _previewTracks.Length);
            uint lineColor = ImGui.ColorConvertFloat4ToU32(trackColor * new Vector4(1f, 1f, 1f, markerAlpha));

            if (!_pcMarkersByTrack.TryGetValue(ti, out var markers))
                continue;

            bool firstPcSeen = false;
            foreach (var marker in markers)
            {
                if (!firstPcSeen) { firstPcSeen = true; continue; }
                DrawCachedPCMarker(ctx, marker, lineColor, iconSize, iconsToRender, viewStart, viewEnd);
            }
        }

        // Render queued icons
        foreach (var (pos, iconId) in iconsToRender)
        {
            try
            {
                var handle = DalamudApi.TextureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty().Handle;
                if (handle != nint.Zero)
                    ctx.DrawList.AddImage(handle, pos, pos + new Vector2(iconSize, iconSize));
            }
            catch { /* ignore missing icons */ }
        }
    }

    private void RebuildPcMarkerCache()
    {
        if (_file == null || _previewTempoMap == null)
        {
            _pcMarkersByTrack = new Dictionary<int, IReadOnlyList<PreviewProgramChangeMarker>>();
            _pcMarkerCacheFile = null;
            _pcMarkerCacheVersion = -1;
            return;
        }

        var dict = new Dictionary<int, IReadOnlyList<PreviewProgramChangeMarker>>();
        var tmap = _previewTempoMap;

        for (int ti = 0; ti < _file.Tracks.Count; ti++)
        {
            var track = _file.Tracks[ti];
            if (track.IsConductorTrack) continue;

            var markers = new List<PreviewProgramChangeMarker>();

            // For the currently selected+loaded track, read from in-memory events
            // so edits are reflected before the track is unloaded back to the chunk.
            if (ti == _selectedTrackIndex && CurrentEvents != null)
            {
                foreach (var ev in CurrentEvents)
                {
                    if (ev.Source.Event is not ProgramChangeEvent pc) continue;
                    double timeSec = TimeConverter.ConvertTo<MetricTimeSpan>(ev.Tick, tmap).TotalMicroseconds / 1_000_000.0;
                    uint? iconId = TryGetGuitarProgramIcon((byte)pc.ProgramNumber, out var id) ? id : null;
                    markers.Add(new PreviewProgramChangeMarker(timeSec, (byte)pc.ProgramNumber, iconId));
                }
            }
            else
            {
                foreach (var te in track.Chunk.GetTimedEvents())
                {
                    if (te.Event is not ProgramChangeEvent pc) continue;
                    double timeSec = TimeConverter.ConvertTo<MetricTimeSpan>(te.Time, tmap).TotalMicroseconds / 1_000_000.0;
                    uint? iconId = TryGetGuitarProgramIcon((byte)pc.ProgramNumber, out var id) ? id : null;
                    markers.Add(new PreviewProgramChangeMarker(timeSec, (byte)pc.ProgramNumber, iconId));
                }
            }

            dict[ti] = markers;
        }

        _pcMarkersByTrack = dict;
        _pcMarkerCacheFile = _file;
        _pcMarkerCacheVersion = _file.Version;
    }

    private static void DrawCachedPCMarker(PianoRenderContext ctx, PreviewProgramChangeMarker marker,
        uint lineColor, float iconSize, List<(Vector2 pos, uint iconId)> iconsToRender,
        double viewStart, double viewEnd)
    {
        double timeSec = marker.TimeSeconds;
        if (timeSec < viewStart - 1.0 || timeSec > viewEnd + 1.0) return;

        float x = ctx.GetTimeX(timeSec);
        if (x < ctx.RollX || x > ctx.RollX + ctx.RollWidth) return;

        ctx.DrawList.AddLine(
            new Vector2(x, ctx.Y),
            new Vector2(x, ctx.Y + ctx.Height),
            lineColor,
            2f);

        if (marker.IconId.HasValue)
            iconsToRender.Add((new Vector2(x - iconSize * 0.5f, ctx.Y + 2f), marker.IconId.Value));
    }

    private void DrawPCMarker(PianoRenderContext ctx, long tick, int programNumber,
        uint lineColor, float iconSize, List<(Vector2 pos, uint iconId)> iconsToRender)
    {
        if (_previewTempoMap == null) return;
        double timeSec = TimeConverter.ConvertTo<MetricTimeSpan>(tick, _previewTempoMap).TotalMicroseconds / 1_000_000.0;
        if (timeSec < ctx.View.StartTime - 1.0 || timeSec > ctx.View.EndTime + 1.0) return;

        float x = ctx.GetTimeX(timeSec);
        if (x < ctx.RollX || x > ctx.RollX + ctx.RollWidth) return;

        ctx.DrawList.AddLine(
            new Vector2(x, ctx.Y),
            new Vector2(x, ctx.Y + ctx.Height),
            lineColor,
            2f);

        if (TryGetGuitarProgramIcon(programNumber, out var iconId))
            iconsToRender.Add((new Vector2(x - iconSize * 0.5f, ctx.Y + 2f), iconId));
    }

    private static bool TryGetGuitarProgramIcon(int gmProgram0Based, out uint iconId)
    {
        iconId = 0;
        if (gmProgram0Based is < 0 or > 127 ||
            InstrumentHelper.ProgramInstruments == null ||
            InstrumentHelper.Instruments == null)
            return false;

        if (!InstrumentHelper.ProgramInstruments.TryGetValue((SevenBitNumber)(byte)gmProgram0Based, out var instrumentId))
            return false;

        if (instrumentId >= (uint)InstrumentHelper.Instruments.Length)
            return false;

        var instrument = InstrumentHelper.Instruments[(int)instrumentId];
        if (!instrument.IsGuitar)
            return false;

        iconId = instrument.IconId;
        return true;
    }
}
