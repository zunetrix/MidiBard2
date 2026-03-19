using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Util;

namespace MidiBard;

public partial class MidiEditorWindow
{
    // GM program numbers (0-based) that map to FFXIV guitar instruments
    // FFXIV guitar instrument indices: 24=Overdriven, 25=Clean, 26=Muted, 27=Harmonic
    private static int GetGuitarInstrumentId(int gmProgram0Based) => gmProgram0Based switch
    {
        29 or 30 => 24,  // Overdriven / Distortion Guitar → ElectricGuitarOverdriven
        24 or 25 or 26 or 27 => 25,  // Acoustic/Electric Clean → ElectricGuitarClean
        28 => 26,        // Electric Guitar (muted) → ElectricGuitarMuted
        31 => 27,        // Guitar harmonics → ElectricGuitarHarmonic
        _ => -1,
    };

    private void DrawProgramChangeMarkers(PianoRenderContext ctx)
    {
        if (_file == null || _previewTempoMap == null || _previewTracks == null) return;

        // Collect deferred icon draws (must happen after draw list setup)
        var iconsToRender = new List<(Vector2 pos, uint iconId)>();
        const float markerAlpha = 0.55f;
        float iconSize = _previewState.NoteMinHeight * 2f;
        iconSize = Math.Clamp(iconSize, 14f, 32f) * ImGuiHelpers.GlobalScale;

        for (int ti = 0; ti < _file.Tracks.Count; ti++)
        {
            var editTrack = _file.Tracks[ti];
            if (editTrack.IsConductorTrack) continue;

            // Derive track color for the marker line
            var displayState = (ti < _previewTracks.Length) ? _previewTracks[ti] : null;
            if (displayState != null && !displayState.Visible) continue;

            var trackColor = displayState?.Color ?? PianoRollWindow.GetTrackColor(ti, _previewTracks.Length);
            uint lineColor = ImGui.ColorConvertFloat4ToU32(trackColor * new Vector4(1f, 1f, 1f, markerAlpha));

            bool firstPcSeen = false;

            // For the currently selected+loaded track, read from in-memory events
            // so edits are reflected before the track is unloaded back to the chunk.
            if (ti == _selectedTrackIndex && CurrentEvents != null)
            {
                foreach (var ev in CurrentEvents)
                {
                    if (ev.Source.Event is not ProgramChangeEvent pc) continue;
                    if (!firstPcSeen) { firstPcSeen = true; continue; }
                    DrawPCMarker(ctx, ev.Tick, pc.ProgramNumber, lineColor, iconSize, iconsToRender);
                }
            }
            else
            {
                foreach (var te in editTrack.Chunk.GetTimedEvents())
                {
                    if (te.Event is not ProgramChangeEvent pc) continue;
                    if (!firstPcSeen) { firstPcSeen = true; continue; }
                    DrawPCMarker(ctx, te.Time, pc.ProgramNumber, lineColor, iconSize, iconsToRender);
                }
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

        int guitarId = GetGuitarInstrumentId(programNumber);
        if (guitarId >= 0 && guitarId < InstrumentHelper.Instruments.Length)
            iconsToRender.Add((new Vector2(x - iconSize * 0.5f, ctx.Y + 2f), InstrumentHelper.Instruments[guitarId].IconId));
    }
}
