using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class EditableMidiFileSafetyTests
{
    [Fact]
    public void GetTrackDisplayNumber_LabelsConductorAsZeroAndPerformanceTracksFromOne()
    {
        var file = CreateEditableFile(
            CreateTrack(Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Lead", Note(60, 0, 120)),
            CreateTrack("Harmony", Note(64, 0, 120)));

        MidiEditorWindow.GetTrackDisplayNumber(file.Tracks, 0).ShouldBe("00");
        MidiEditorWindow.GetTrackDisplayNumber(file.Tracks, 1).ShouldBe("01");
        MidiEditorWindow.GetTrackDisplayNumber(file.Tracks, 2).ShouldBe("02");
    }

    [Fact]
    public void GetTrackDisplayNumber_StartsAtOneWhenNoConductorTrackExists()
    {
        var file = CreateEditableFile(
            CreateTrack("Lead", Note(60, 0, 120)),
            CreateTrack("Harmony", Note(64, 0, 120)));

        MidiEditorWindow.GetTrackDisplayNumber(file.Tracks, 0).ShouldBe("01");
        MidiEditorWindow.GetTrackDisplayNumber(file.Tracks, 1).ShouldBe("02");
    }

    [Fact]
    public void SetDirtyStateForLoad_DoesNotAdvanceVersion()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var beforeVersion = file.Version;

        file.SetDirtyStateForLoad(true);

        file.IsDirty.ShouldBeTrue();
        file.Version.ShouldBe(beforeVersion);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(params object[] objects)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();

        foreach (var item in objects)
        {
            switch (item)
            {
                case TimedEvent timedEvent:
                    manager.Objects.Add(timedEvent);
                    break;
                case Note note:
                    manager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                        note.Time));
                    manager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                        note.EndTime));
                    break;
                case string name:
                    manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(name), 0));
                    break;
            }
        }

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);

    private static Note Note(int noteNumber, long time, long length)
        => new(
            (SevenBitNumber)(byte)noteNumber,
            length,
            time)
        {
            Channel = (FourBitNumber)0,
            Velocity = (SevenBitNumber)100,
            OffVelocity = (SevenBitNumber)0,
        };
}
