using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{
    private void DrawPianoKeys(PianoRenderContext ctx)
    {
        int startNote = ctx.FirstVisibleNote;
        int endNote = ctx.LastVisibleNote;

        float noteHeight = ctx.View.NoteHeight;

        for (int note = startNote; note <= endNote; note++)
        {
            if (note < 0 || note >= 128)
                continue;

            int noteInOctave = note % 12;
            bool isBlack = BlackKeys.Contains(noteInOctave);

            float top = ctx.GetNoteTopY(note);
            float bottom = top + noteHeight;

            Vector4 keyColor = isBlack ? BlackKeyColor : WhiteKeyColor;

            ctx.DrawList.AddRectFilled(
                new Vector2(ctx.PianoKeysX, top),
                new Vector2(ctx.PianoKeysX + ctx.PianoKeyWidth, bottom),
                ImGui.ColorConvertFloat4ToU32(keyColor));

            ctx.DrawList.AddRect(
                new Vector2(ctx.PianoKeysX, top),
                new Vector2(ctx.PianoKeysX + ctx.PianoKeyWidth, bottom),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.4f)));

            DrawPianoKeyLabel(ctx, note, top);
        }
    }

    private string GetPianoKeyLabel(int note)
    {
        int noteInOctave = note % 12;
        int octave = note / 12 - 1;

        return $"{PianoRollState.NoteNames[noteInOctave]}{octave}";
    }

    private void DrawPianoKeyLabel(PianoRenderContext ctx, int note, float noteTop)
    {
        float zoom = ctx.View.NoteHeight;

        // small zoom dont show key label
        if (zoom < 10f)
            return;

        int noteInOctave = note % 12;

        // medium zoom show C
        if (zoom <= 15f && noteInOctave != 0)
            return;

        string label = GetPianoKeyLabel(note);

        Vector2 textSize = ImGui.CalcTextSize(label);

        float paddingRight = 6f;
        float textX = ctx.PianoKeysX + ctx.PianoKeyWidth - textSize.X - paddingRight;
        float textY = noteTop + (zoom - textSize.Y) * 0.5f;

        bool isBlack = BlackKeys.Contains(noteInOctave);

        uint textColor = ImGui.ColorConvertFloat4ToU32(
            isBlack ? Vector4.One : new Vector4(0f, 0f, 0f, 1f));

        ctx.DrawList.AddText(
            new Vector2(textX, textY),
            textColor,
            label);
    }

    private static unsafe Vector4 GetTrackColor(int index, int maxTracks)
    {
        float h = index / (float)Math.Max(1, maxTracks);
        Vector4 color = Vector4.One;
        ImGui.ColorConvertHSVtoRGB(h, 0.8f, 1f, &color.X, &color.Y, &color.Z);
        return color;
    }
}
