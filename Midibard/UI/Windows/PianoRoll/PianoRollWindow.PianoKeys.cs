using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{
    internal void DrawPianoKeys(PianoRenderContext ctx)
    {
        int startNote = ctx.FirstVisibleNote;
        int endNote = ctx.LastVisibleNote;

        float noteHeight = ctx.View.NoteHeight;

        for (int note = startNote; note <= endNote; note++)
        {
            if (note < 0 || note >= 128)
                continue;

            bool isBlack = IsBlackKey[note % 12];

            float top = ctx.GetNoteTopY(note);
            float bottom = top + noteHeight;

            // Use pre-computed U32 colors - avoids ColorConvertFloat4ToU32 per key
            ctx.DrawList.AddRectFilled(
                new Vector2(ctx.PianoKeysX, top),
                new Vector2(ctx.PianoKeysX + ctx.PianoKeyWidth, bottom),
                isBlack ? BlackKeyColorU32 : WhiteKeyColorU32);

            ctx.DrawList.AddRect(
                new Vector2(ctx.PianoKeysX, top),
                new Vector2(ctx.PianoKeysX + ctx.PianoKeyWidth, bottom),
                PianoKeyBorderU32);

            DrawPianoKeyLabel(ctx, note, top, isBlack);
        }
    }

    private string GetPianoKeyLabel(int note) => NoteLabels[note];

    private void DrawPianoKeyLabel(PianoRenderContext ctx, int note, float noteTop, bool isBlack)
    {
        float zoom = ctx.View.NoteHeight;

        // small zoom don't show key label
        if (zoom < 10f)
            return;

        int noteInOctave = note % 12;

        // medium zoom show C only
        if (zoom <= 15f && noteInOctave != 0)
            return;

        // Use pre-computed label string and size - avoids string alloc + CalcTextSize per key
        EnsureNoteLabelSizes();
        string label = NoteLabels[note];
        Vector2 textSize = NoteLabelSizes[note];

        float paddingRight = 6f;
        float textX = ctx.PianoKeysX + ctx.PianoKeyWidth - textSize.X - paddingRight;
        float textY = noteTop + (zoom - textSize.Y) * 0.5f;

        ctx.DrawList.AddText(
            new Vector2(textX, textY),
            isBlack ? BlackKeyTextU32 : WhiteKeyTextU32,
            label);
    }

    internal static unsafe Vector4 GetTrackColor(int index, int maxTracks)
    {
        float h = index / (float)System.Math.Max(1, maxTracks);
        Vector4 color = Vector4.One;
        ImGui.ColorConvertHSVtoRGB(h, 0.8f, 1f, &color.X, &color.Y, &color.Z);
        return color;
    }
}
