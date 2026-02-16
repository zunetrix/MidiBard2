using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

struct PianoRenderContext
{
    public ImDrawListPtr DrawList;
    public PianoViewport View;

    public float X;
    public float Y;
    public float Width;
    public float Height;
    public Vector2 CanvasMin;
    public Vector2 CanvasMax;

    public float PianoKeyWidth;
    public float PianoKeysX;
    public float RollX => X + PianoKeyWidth;
    public float RollWidth => Width - PianoKeyWidth;

    public float GetTimeX(double time)
    {
        return X + (float)((time - View.StartTime) * View.PixelsPerSecond);
    }

    public float GetNoteTopY(int note)
    {
        return Y + (View.TopNote - note) * View.NoteHeight;
    }

    public float GetNoteBottomY(int note)
    {
        return GetNoteTopY(note) + View.NoteHeight;
    }

    public Vector2 NoteRectMin(double start, int note)
    {
        return new Vector2(
            GetTimeX(start),
            GetNoteTopY(note));
    }

    public Vector2 NoteRectMax(double end, int note)
    {
        return new Vector2(
            GetTimeX(end),
            GetNoteBottomY(note));
    }

    public bool IsNoteVisible(double start, double end, int note)
    {
        if (end < View.StartTime || start > View.EndTime)
            return false;

        float top = GetNoteTopY(note);
        float bottom = top + View.NoteHeight;

        if (bottom < Y || top > Y + Height)
            return false;

        return true;
    }

    public int FirstVisibleNote =>
    (int)Math.Floor(View.TopNote - VisibleNoteCount);

    public int LastVisibleNote =>
        (int)Math.Ceiling(View.TopNote);

    public float VisibleNoteCount =>
        Height / View.NoteHeight;
}
