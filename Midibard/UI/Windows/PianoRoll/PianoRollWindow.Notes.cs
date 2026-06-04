using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{
    internal void DrawNoteGrid(PianoRenderContext ctx, PianoRollState state)
    {
        uint darkColor = state.GridDarkColorU32;
        uint lightColor = state.GridLightColorU32;
        uint lineColor = state.GridLineColorU32;

        int lo = ctx.View.StartNote < 0 ? 0 : ctx.View.StartNote;
        int hi = ctx.View.EndNote > 127 ? 127 : ctx.View.EndNote;

        for (int note = lo; note <= hi; note++)
        {
            float noteY = ctx.GetNoteTopY(note);
            uint rowColor = IsBlackKey[note % 12] ? darkColor : lightColor;

            ctx.DrawList.AddRectFilled(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY + ctx.View.NoteHeight),
                rowColor);

            ctx.DrawList.AddLine(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY),
                lineColor);
        }
    }

    internal void DrawNotes(PianoRenderContext ctx, TrackDisplayState[] tracks, PianoRollState state)
    {
        if (tracks is not { Length: > 0 })
            return;

        if (state.ShowNoteLabel)
            EnsureNoteLabelSizes();

        uint noteBorderColor = state.NoteBorderColorU32;
        uint noteLabelColor = state.NoteLabelColorU32;

        foreach (var track in tracks)
        {
            if (!track.Visible)
                continue;

            uint noteColorU32 = track.AutoColorU32;

            var notes = track.Notes;
            if (notes.Length == 0) continue;

            // Binary search: skip notes that start after the viewport end (notes sorted by start)
            int lastIdx = BinarySearchNoteUpper(notes, ctx.View.EndTime);

            // Binary search: jump to the first note that could be visible.
            // Step back 200 notes to catch long notes that started before the viewport.
            int firstIdx = BinarySearchNoteLower(notes, ctx.View.StartTime);
            firstIdx = Math.Max(0, firstIdx - 200);

            for (int ni = firstIdx; ni < lastIdx; ni++)
            {
                var (start, end, noteNum) = notes[ni];

                // Skip notes that ended before the viewport - cheaper than full IsNoteVisible
                if (end < ctx.View.StartTime) continue;

                int displayNote = TrackInfo.TranslateNoteNumber(
                    noteNum,
                    track.TrackInfo.TransposeFromTrackName,
                    //revert the normalization C3=0-C6=36
                    track.ShowAdaptedNotes) + 48;

                if (!ctx.IsNoteVisible(start, end, displayNote))
                    continue;

                Vector2 min = ctx.NoteRectMin(start, displayNote);
                Vector2 max = ctx.NoteRectMax(end, displayNote);

                float noteWidth = max.X - min.X;
                if (noteWidth < 1f) continue;
                if (noteWidth < 2f) max.X = min.X + 2f;
                max.Y -= 2f;

                ctx.DrawList.AddRectFilled(min, max, noteColorU32, 2f);

                if (state.ShowNoteBorder)
                    ctx.DrawList.AddRect(min, max, noteBorderColor, rounding: 2f, thickness: 1f);

                if (state.ShowNoteLabel)
                {
                    float noteHeight = max.Y - min.Y;
                    if (noteHeight > 15f)
                    {
                        float labelWidth = max.X - min.X;
                        string noteLabel = NoteLabels[displayNote];
                        Vector2 textSize = NoteLabelSizes[displayNote];
                        if (labelWidth > textSize.X + 4f)
                            ctx.DrawList.AddText(new Vector2(min.X + 2f, min.Y + 1f), noteLabelColor, noteLabel);
                    }
                }
            }
        }
    }
}
