using System;

namespace MidiBard;

struct PianoViewport
{
    public float NoteHeight;
    public float PixelsPerSecond;

    public float VisibleNotes;
    public int StartNote;
    public int EndNote;

    public float TopNote;

    public double StartTime;
    public double EndTime;
}
