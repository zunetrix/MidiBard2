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

            // Batch all note bodies per track — replaces N AddRectFilled calls
            // with 1 PrimReserve + N PrimRect (managed-memory writes, no P/Invoke per rect)
            var dl = ctx.DrawList;
            dl.PrimReserve(6 * batchCount, 4 * batchCount);
            for (int i = 0; i < batchCount; i++)
            {
                var (min, max, _) = _batchNoteRects[i];
                dl.PrimRect(min, max, noteColorU32);
            }

            // Borders and labels use regular draw list API (smaller batch, less frequent)
            if (state.ShowNoteBorder || state.ShowNoteLabel)
            {
                for (int i = 0; i < batchCount; i++)
                {
                    var (min, max, displayNote) = _batchNoteRects[i];

                    if (state.ShowNoteBorder)
                    {
                        if (max.X - min.X >= 3f)
                            dl.AddRect(min, max, noteBorderColor);
                    }

                    if (state.ShowNoteLabel)
                    {
                        float noteHeight = max.Y - min.Y;
                        if (noteHeight > 15f)
                        {
                            float labelWidth = max.X - min.X;
                            string noteLabel = NoteLabels[displayNote];
                            Vector2 textSize = NoteLabelSizes[displayNote];
                            if (labelWidth > textSize.X + 4f)
                                dl.AddText(new Vector2(min.X + 2f, min.Y + 1f), noteLabelColor, noteLabel);
                        }
                    }
                }
            }
        }
    }
}
