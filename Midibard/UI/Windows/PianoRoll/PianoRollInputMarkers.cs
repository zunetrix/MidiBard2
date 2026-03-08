using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{
    private void DrawPlaybackCursor(PianoRenderContext ctx, double timelinePos)
    {
        float cursorX = ctx.X + (float)((timelinePos - State.CameraTime) * ctx.View.PixelsPerSecond);

        if (cursorX >= ctx.X && cursorX <= ctx.X + ctx.Width)
        {
            ctx.DrawList.AddLine(
                new Vector2(cursorX, ctx.Y),
                new Vector2(cursorX, ctx.Y + ctx.Height),
                ImGui.ColorConvertFloat4ToU32(Style.Colors.RedVivid), 3f);
        }
    }

    private void DrawRangeMarkers(PianoRenderContext ctx)
    {
        if (!State.ShowC3C6Range)
            return;

        const int C3 = 48;
        const int C6 = 84;

        DrawHorizontalMarker(ctx, C3, alignBottom: true);
        DrawHorizontalMarker(ctx, C6, alignBottom: false);
    }

    private void DrawHorizontalMarker(PianoRenderContext ctx, int note, bool alignBottom)
    {
        float noteY = alignBottom
            ? ctx.GetNoteBottomY(note)
            : ctx.GetNoteTopY(note);

        if (noteY < ctx.Y || noteY > ctx.Y + ctx.Height)
            return;

        ctx.DrawList.AddLine(
            new Vector2(ctx.X, noteY),
            new Vector2(ctx.X + ctx.Width, noteY),
            ImGui.ColorConvertFloat4ToU32(Style.Colors.Yellow),
            3f);
    }

    private void DrawVerticalMarker(
        PianoRenderContext ctx,
        double start,
        double end,
        uint color,
        float thickness = 1f)
    {
        if (end < ctx.View.StartTime || start > ctx.View.EndTime)
            return;

        float x1 = ctx.GetTimeX(start);
        float x2 = ctx.GetTimeX(end);

        float minX = ctx.RollX;
        float maxX = ctx.RollX + ctx.RollWidth;

        x1 = Math.Max(x1, minX);
        x2 = Math.Min(x2, maxX);

        if (x2 <= x1)
            return;

        float top = ctx.Y;
        float bottom = ctx.Y + ctx.Height;

        ctx.DrawList.AddRectFilled(
            new Vector2(x1, top),
            new Vector2(x2, bottom),
            color);

        ctx.DrawList.AddRect(
            new Vector2(x1, top),
            new Vector2(x2, bottom),
            color,
            0f,
            ImDrawFlags.None,
            thickness);
    }

    private void DrawVoiceLimitRegions(PianoRenderContext ctx)
    {
        if (State.Tracks?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        var voiceLimitRegions = State.VoiceLimitRegions;
        uint markerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 0f, 0.15f));

        foreach (var voiceLimitRegion in voiceLimitRegions)
        {
            DrawVerticalMarker(ctx, voiceLimitRegion.start, voiceLimitRegion.end, markerColor);
        }
    }
}
