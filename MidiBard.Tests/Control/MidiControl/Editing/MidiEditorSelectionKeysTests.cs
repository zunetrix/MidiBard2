using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiEditorSelectionKeysTests
{
    [Fact]
    public void FromSelectedEvents_ReturnsOnlySelectedNoteRows()
    {
        var noteRow = new EditableEvent(
            new TimedEvent(NoteOn(64, channel: 2), 120),
            new TimedEvent(NoteOff(64, channel: 2), 360));
        var programRow = new EditableEvent(new TimedEvent(new ProgramChangeEvent((SevenBitNumber)40), 0));
        var unpairedNoteRow = new EditableEvent(new TimedEvent(NoteOn(65, channel: 3), 240));

        var keys = MidiEditorSelectionKeys.FromSelectedEvents(
            new[] { noteRow, programRow, unpairedNoteRow },
            new[] { 0, 1, 2, 99 });

        keys.Count.ShouldBe(1);
        keys.ShouldContain((120L, (byte)64, (byte)2));
    }

    [Fact]
    public void FromSelectedEvents_HandlesMissingEventList()
    {
        var keys = MidiEditorSelectionKeys.FromSelectedEvents(null, new[] { 0 });

        keys.ShouldBeEmpty();
    }

    private static NoteOnEvent NoteOn(int noteNumber, int channel)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)100)
        {
            Channel = (FourBitNumber)(byte)channel,
        };

    private static NoteOffEvent NoteOff(int noteNumber, int channel)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)0)
        {
            Channel = (FourBitNumber)(byte)channel,
        };
}
