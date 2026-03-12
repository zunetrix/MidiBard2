using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

public partial class PianoRollWindow
{
    private void DrawNoteGrid(PianoRenderContext ctx)
    {
        uint darkColor = ImGui.ColorConvertFloat4ToU32(State.GridDarkColor);
        uint lightColor = ImGui.ColorConvertFloat4ToU32(State.GridLightColor);
        uint lineColor = ImGui.ColorConvertFloat4ToU32(State.GridLineColor);

        // Clamp loop bounds to valid MIDI range - eliminates per-iteration bounds check
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

    private void DrawNotes(PianoRenderContext ctx)
    {
        if (State.Tracks is not { Length: > 0 } || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        // Hoist label size computation once per frame (requires ImGui context)
        if (State.ShowNoteLabel)
            EnsureNoteLabelSizes();

        // Hoist per-frame constant conversions outside the per-track and per-note loops
        uint noteBorderColor = ImGui.ColorConvertFloat4ToU32(State.NoteBorderColor);
        uint noteLabelColor = ImGui.ColorConvertFloat4ToU32(State.NoteLabelColor);

        foreach (var track in State.Tracks)
        {
            if (!track.Visible)
                continue;

            var trackColor = track.Color ?? GetTrackColor(track.TrackInfo.Index, State.Tracks.Length);
            uint noteColorU32 = ImGui.ColorConvertFloat4ToU32(trackColor);

            var notes = track.Notes;
            if (notes.Length == 0) continue;

            // Binary search: skip notes that start after the viewport end (notes sorted by start)
            int lastIdx = BinarySearchNoteUpper(notes, ctx.View.EndTime);

            for (int ni = 0; ni < lastIdx; ni++)
            {
                var (start, end, noteNum) = notes[ni];

                // Skip notes that ended before the viewport - cheaper than full IsNoteVisible
                if (end < ctx.View.StartTime) continue;

                int displayNote = TrackInfo.TranslateNoteNumber(
                    noteNum,
                    track.TrackInfo.TransposeFromTrackName,
                    track.ShowAdaptedNotes) + 48; //revert the normalization C3=0-C6=36

                if (!ctx.IsNoteVisible(start, end, displayNote))
                    continue;

                Vector2 min = ctx.NoteRectMin(start, displayNote);
                Vector2 max = ctx.NoteRectMax(end, displayNote);

                if (max.X - min.X < 2f)
                    max.X = min.X + 2f;

                max.Y -= 2f;

                ctx.DrawList.AddRectFilled(min, max, noteColorU32, 2f);

                if (State.ShowNoteBorder)
                {
                    ctx.DrawList.AddRect(min, max, noteBorderColor, rounding: 2f, thickness: 1f);
                }

                // note label - only show if there's enough space (height and width)
                if (State.ShowNoteLabel)
                {
                    float noteHeight = max.Y - min.Y;
                    if (noteHeight > 15f)
                    {
                        float noteWidth = max.X - min.X;
                        // Use pre-computed label string and size - avoids string alloc + CalcTextSize per note
                        string noteLabel = NoteLabels[displayNote];
                        Vector2 textSize = NoteLabelSizes[displayNote];
                        if (noteWidth > textSize.X + 4f)
                        {
                            ctx.DrawList.AddText(new Vector2(min.X + 2f, min.Y + 1f), noteLabelColor, noteLabel);
                        }
                    }
                }
            }
        }
    }
}
