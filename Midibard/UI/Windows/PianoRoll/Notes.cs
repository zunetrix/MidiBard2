using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{
    private void DrawNoteGrid(PianoRenderContext ctx)
    {
        for (int note = ctx.View.StartNote; note <= ctx.View.EndNote; note++)
        {
            if (note < 0 || note >= 128)
                continue;

            float noteY = ctx.GetNoteTopY(note);

            bool isBlack = BlackKeys.Contains(note % 12);
            Vector4 rowColor = isBlack ? gridDark : gridLight;

            ctx.DrawList.AddRectFilled(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY + ctx.View.NoteHeight),
                ImGui.ColorConvertFloat4ToU32(rowColor));

            ctx.DrawList.AddLine(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY),
                ImGui.ColorConvertFloat4ToU32(gridLine));
        }
    }

    private void DrawNotes(PianoRenderContext ctx)
    {
        if (State.PlotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        foreach (var (trackInfo, notes) in State.PlotData)
        {
            // draw only enabled tracks
            if (State.TrackVisible != null &&
                trackInfo.Index < State.TrackVisible.Length &&
                !State.TrackVisible[trackInfo.Index])
                continue;

            uint noteColorU32 = ImGui.ColorConvertFloat4ToU32(GetTrackColor(trackInfo.Index));

            foreach (var (start, end, note) in notes)
            {
                if (!ctx.IsNoteVisible(start, end, note))
                    continue;

                Vector2 min = ctx.NoteRectMin(start, note);
                Vector2 max = ctx.NoteRectMax(end, note);

                if (max.X - min.X < 2f)
                    max.X = min.X + 2f;

                max.Y -= 2f;

                ctx.DrawList.AddRectFilled(min, max, noteColorU32, 2f);

                if (State.ShowNoteBorder)
                {
                    ctx.DrawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(Style.Colors.Black), rounding: 2f, thickness: 1f);
                }

                // note label
                if (ctx.View.NoteHeight > 15f && State.ShowNoteLabel)
                {
                    uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 1f));
                    ctx.DrawList.AddText(new Vector2(min.X, min.Y), textColor, GetPianoKeyLabel(note));
                }
            }
        }
    }
}
