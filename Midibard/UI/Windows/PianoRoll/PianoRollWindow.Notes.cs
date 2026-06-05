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

            // Collect visible note rects for this track.
            // Two-pass batching: collect + batch-draw note bodies via PrimReserve/PrimRect,
            // then draw borders and labels in a second pass (fewer P/Invoke calls).
            _batchNoteRects.Clear();
            for (int ni = firstIdx; ni < lastIdx; ni++)
            {
                var (start, end, noteNum) = notes[ni];

                // Skip notes that ended before the viewport - cheaper than full IsNoteVisible
                if (end < ctx.View.StartTime) continue;

                int displayNote = TrackInfo.TranslateNoteNumber(
                    noteNum,
                    track.TrackInfo.TransposeFromTrackName,
                    track.ShowAdaptedNotes) + 48;

                if (!ctx.IsNoteVisible(start, end, displayNote))
                    continue;

                Vector2 min = ctx.NoteRectMin(start, displayNote);
                Vector2 max = ctx.NoteRectMax(end, displayNote);

                float noteWidth = max.X - min.X;
                if (noteWidth < 1f) continue;
                if (noteWidth < 2f) max.X = min.X + 2f;
                max.Y -= 2f;

                _batchNoteRects.Add((min, max, displayNote));
            }

            int batchCount = _batchNoteRects.Count;
            if (batchCount == 0) continue;

            // Draw each note's body, border, and label in per-note sequence so a later
            // overlapping note's body covers the earlier note's label (pre-batch behavior).
            // PrimReserve + PrimRect per note instead of AddRectFilled to avoid rounding
            // vertex overhead; PrimReserve is ~3× cheaper than AddRectFilled.
            var dl = ctx.DrawList;
            for (int i = 0; i < batchCount; i++)
            {
                var (min, max, displayNote) = _batchNoteRects[i];
                float noteWidth = max.X - min.X;

                // Body
                dl.PrimReserve(6, 4);
                dl.PrimRect(min, max, noteColorU32);

                // Border (drawn as 4 thin filled rects at the edges)
                if (state.ShowNoteBorder && noteWidth >= 3f)
                {
                    dl.PrimReserve(24, 16);
                    dl.PrimRect(new Vector2(min.X, min.Y), new Vector2(max.X, min.Y + 1f), noteBorderColor);
                    dl.PrimRect(new Vector2(min.X, max.Y - 1f), new Vector2(max.X, max.Y), noteBorderColor);
                    dl.PrimRect(new Vector2(min.X, min.Y), new Vector2(min.X + 1f, max.Y), noteBorderColor);
                    dl.PrimRect(new Vector2(max.X - 1f, min.Y), new Vector2(max.X, max.Y), noteBorderColor);
                }

                // Label (AddText requires font atlas state)
                if (state.ShowNoteLabel)
                {
                    float noteHeight = max.Y - min.Y;
                    if (noteHeight > 15f && noteWidth > NoteLabelSizes[displayNote].X + 4f)
                        dl.AddText(new Vector2(min.X + 2f, min.Y + 1f), noteLabelColor, NoteLabels[displayNote]);
                }
            }
        }
    }
}
