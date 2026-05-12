using System.Collections.Generic;

using Melanchall.DryWetMidi.Core;

namespace MidiBard.Control.MidiControl.Editing;

public static class MidiEditorSelectionKeys
{
    public static HashSet<(long tick, byte noteNum, byte channel)> FromSelectedEvents(
        IReadOnlyList<EditableEvent>? events,
        IEnumerable<int> selectedEventIndices)
    {
        var keys = new HashSet<(long, byte, byte)>();
        if (events == null)
            return keys;

        foreach (var idx in selectedEventIndices)
        {
            if ((uint)idx >= (uint)events.Count)
                continue;

            var ev = events[idx];
            if (ev.NoteOffSource == null || ev.Source.Event is not NoteOnEvent noteOn)
                continue;

            keys.Add((ev.Tick, (byte)noteOn.NoteNumber, (byte)noteOn.Channel));
        }

        return keys;
    }
}
