
using System.Runtime.InteropServices;

using MidiBard.Managers;

#if DEBUG
[StructLayout(LayoutKind.Explicit)]
unsafe struct PerformanceStruct
{
    public static PerformanceStruct* Instance => (PerformanceStruct*)(Offsets.PerformanceStructPtr + 3);
    [FieldOffset(8)] public float UnkFloat;
    [FieldOffset(16)] public byte Instrument;
    [FieldOffset(17)] public byte Tone;
    [FieldOffset(0xBE0)] public void** UnkPerformanceVtbl;
    [FieldOffset(0xBF0)] public fixed byte soloNotes[10];
    [FieldOffset(0xBF0 + 10)] public fixed byte soloTones[10];
    [FieldOffset(0x1E18)] public EnsembleStruct EnsembleStructStart;

    [StructLayout(LayoutKind.Explicit)]
    public struct EnsembleStruct
    {
        [FieldOffset(0)] public void** EnsembleStructVtbl;

    }

    public byte PlayingNoteNoteNumber
    {
        get
        {
            var currentNoteIndex = CurrentNoteIndex - 1;
            if (currentNoteIndex < 0) currentNoteIndex += 8;
            return NoteTonePairEntry[currentNoteIndex * 2];
        }
    }

    public byte PlayingNoteTone
    {
        get
        {
            var currentNoteIndex = CurrentNoteIndex - 1;
            if (currentNoteIndex < 0) currentNoteIndex += 8;
            return NoteTonePairEntry[currentNoteIndex * 2 + 1];
        }
    }

    [FieldOffset(0x2CF8)] public fixed byte NoteTonePairEntry[16];
    [FieldOffset(0x2D1C)] public byte CurrentNoteIndex;
    [FieldOffset(0x2D1E)] public byte CurrentTone;

    //public struct NoteTonePair
    //{
    //    public byte Note;
    //    public byte Tone;
    //}
}
#endif
