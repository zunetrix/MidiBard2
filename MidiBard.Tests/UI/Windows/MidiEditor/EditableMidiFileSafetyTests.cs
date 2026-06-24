using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Event;
using MidiBard.Control.MidiControl.Editing.State;

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

    [Fact]
    public void EnumerateCurrentTimedEvents_WhenConductorEventsAreLoaded_DoesNotBreakTempoDelete()
    {
        var file = CreateEditableFile(CreateTrack(
            Timed(new SetTempoEvent(500000), 0),
            Timed(new TimeSignatureEvent(4, 2), 0)));
        var conductor = file.Tracks[0];
        conductor.LoadEvents(file.TempoMap);
        var tempoEvent = conductor.Events!
            .Select((editableEvent, index) => (editableEvent, index))
            .Single(item => item.editableEvent.Source.Event is SetTempoEvent);
        var eventKey = EventSelectionKey.FromEvent(tempoEvent.index, tempoEvent.editableEvent);

        conductor.EnumerateCurrentTimedEvents()
            .Count(timedEvent => timedEvent.Event is SetTempoEvent)
            .ShouldBe(1);

        var session = new MidiEditorSessionState { File = file };
        var result = new EditorCommandExecutor().Execute(
            new DeleteEventCommand(),
            EditorCommandContext.Create(session),
            new DeleteEventOptions(0, eventKey));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);

        conductor.FlushChanges();
        conductor.Chunk.GetTimedEvents()
            .Count(timedEvent => timedEvent.Event is SetTempoEvent)
            .ShouldBe(0);
    }

    [Fact]
    public void EnumerateCurrentTimedEvents_WhenConductorEventsAreLoaded_DoesNotBreakTimeSignatureDelete()
    {
        var file = CreateEditableFile(CreateTrack(
            Timed(new SetTempoEvent(500000), 0),
            Timed(new TimeSignatureEvent(4, 2), 0)));
        var conductor = file.Tracks[0];
        conductor.LoadEvents(file.TempoMap);
        var timeSignatureEvent = conductor.Events!
            .Select((editableEvent, index) => (editableEvent, index))
            .Single(item => item.editableEvent.Source.Event is TimeSignatureEvent);
        var eventKey = EventSelectionKey.FromEvent(timeSignatureEvent.index, timeSignatureEvent.editableEvent);

        conductor.EnumerateCurrentTimedEvents()
            .Count(timedEvent => timedEvent.Event is TimeSignatureEvent)
            .ShouldBe(1);

        var session = new MidiEditorSessionState { File = file };
        var result = new EditorCommandExecutor().Execute(
            new DeleteEventCommand(),
            EditorCommandContext.Create(session),
            new DeleteEventOptions(0, eventKey));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);

        conductor.FlushChanges();
        conductor.Chunk.GetTimedEvents()
            .Count(timedEvent => timedEvent.Event is TimeSignatureEvent)
            .ShouldBe(0);
    }

    [Fact]
    public void EnumerateCurrentTimedEvents_WhenEventsAreLoaded_ReturnsLiveEditedValues()
    {
        var file = CreateEditableFile(CreateTrack(Timed(new SetTempoEvent(500000), 0)));
        var conductor = file.Tracks[0];
        conductor.LoadEvents(file.TempoMap);
        var tempoEvent = conductor.Events!.Single(editableEvent => editableEvent.Source.Event is SetTempoEvent);
        tempoEvent.EditValue1 = 150;
        tempoEvent.ApplyEditValues();

        var currentTempo = conductor.EnumerateCurrentTimedEvents()
            .Single(timedEvent => timedEvent.Event is SetTempoEvent);

        ((SetTempoEvent)currentTempo.Event).MicrosecondsPerQuarterNote
            .ShouldBe((long)(60_000_000.0 / 150));
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
