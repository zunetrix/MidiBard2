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
            Vector4 rowColor = isBlack ? State.GridDarkColor : State.GridLightColor;

            ctx.DrawList.AddRectFilled(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY + ctx.View.NoteHeight),
                ImGui.ColorConvertFloat4ToU32(rowColor));

            ctx.DrawList.AddLine(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY),
                ImGui.ColorConvertFloat4ToU32(State.GridLineColor));
        }
    }

    private void DrawNotes(PianoRenderContext ctx)
    {
        if (State.Tracks?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        foreach (var track in State.Tracks)
        {
            if (!track.Visible)
                continue;

            var trackColor = track.Color ?? GetTrackColor(track.TrackInfo.Index, State.Tracks.Length);
            uint noteColorU32 = ImGui.ColorConvertFloat4ToU32(trackColor);

            foreach (var (start, end, note) in track.Notes)
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
                    ctx.DrawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(State.NoteBorderColor), rounding: 2f, thickness: 1f);
                }

                // note label - only show if there's enough space (height and width)
                if (State.ShowNoteLabel)
                {
                    float noteHeight = max.Y - min.Y;
                    if (noteHeight > 15f)
                    {
                        float noteWidth = max.X - min.X;
                        string noteLabel = GetPianoKeyLabel(note);
                        Vector2 textSize = ImGui.CalcTextSize(noteLabel);
                        var labelFits = noteWidth > textSize.X + 4f;
                        if (labelFits)
                        {
                            uint textColor = ImGui.ColorConvertFloat4ToU32(State.NoteLabelColor);
                            ctx.DrawList.AddText(new Vector2(min.X + 2f, min.Y + 1f), textColor, noteLabel);
                        }
                    }
                }
            }
        }
    }
}
