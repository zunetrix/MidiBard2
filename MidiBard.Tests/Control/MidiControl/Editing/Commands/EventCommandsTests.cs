using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Event;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class EventCommandsTests
{
    [Fact]
    public void DeleteEvents_RemovesSelectedEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(
            Note(60, 0, 120),
            Note(64, 240, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedEvents = file.Tracks[0].Events!
            .Select((editableEvent, index) => (editableEvent, index))
            .Where(item => item.editableEvent.NoteOffSource != null)
            .Select(item => EventSelectionKey.FromEvent(item.index, item.editableEvent))
            .ToArray();

        var result = new EditorCommandExecutor().Execute(
            new DeleteEventsCommand(),
            EditorCommandContext.Create(session),
            new DeleteEventsOptions(0, selectedEvents));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(2);
        file.Tracks[0].Events!.Where(editableEvent => editableEvent.NoteOffSource != null)
            .ShouldBeEmpty();
        file.IsDirty.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(2);
    }

    [Fact]
    public void DeleteEvent_UsesDeleteEventsCommandThroughInvoker()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var editableEvent = file.Tracks[0].Events!.Single(item => item.NoteOffSource != null);
        var eventIndex = file.Tracks[0].Events!.IndexOf(editableEvent);

        var result = new EditorCommandExecutor().Execute(
            new DeleteEventCommand(),
            EditorCommandContext.Create(session),
            new DeleteEventOptions(0, EventSelectionKey.FromEvent(eventIndex, editableEvent)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);
        file.Tracks[0].Events!.Where(item => item.NoteOffSource != null).ShouldBeEmpty();
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void EditEvent_UpdatesProgramChangeAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(
            Timed(new ProgramChangeEvent((SevenBitNumber)0), 0),
            Note(60, 120, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var editableEvent = file.Tracks[0].Events!
            .Single(item => item.Source.Event is ProgramChangeEvent);
        var eventIndex = file.Tracks[0].Events!.IndexOf(editableEvent);

        var result = new EditorCommandExecutor().Execute(
            new EditEventCommand(),
            EditorCommandContext.Create(session),
            new EditEventOptions(
                0,
                EventSelectionKey.FromEvent(eventIndex, editableEvent),
                new EventEditValues(24, 40, 0, 0)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);
        editableEvent.Tick.ShouldBe(24);
        ((ProgramChangeEvent)editableEvent.Source.Event).ProgramNumber.ShouldBe((SevenBitNumber)40);
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetTimedEvents()
            .Single(item => item.Event is ProgramChangeEvent)
            .Time.ShouldBe(0);
        ((ProgramChangeEvent)file.Tracks[0].Chunk.GetTimedEvents()
                .Single(item => item.Event is ProgramChangeEvent)
                .Event)
            .ProgramNumber.ShouldBe((SevenBitNumber)0);
    }

    [Fact]
    public void EditEvent_NoOpDoesNotDirtyOrCaptureHistory()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;
        var editableEvent = file.Tracks[0].Events!.Single(item => item.NoteOffSource != null);
        var eventIndex = file.Tracks[0].Events!.IndexOf(editableEvent);

        var result = new EditorCommandExecutor().Execute(
            new EditEventCommand(),
            EditorCommandContext.Create(session),
            new EditEventOptions(
                0,
                EventSelectionKey.FromEvent(eventIndex, editableEvent),
                new EventEditValues(0, 60, 100, 120)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(0);
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
            }
        }

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);

    private static Note Note(int noteNumber, long time, long length, int channel = 0)
        => new(
            (SevenBitNumber)(byte)noteNumber,
            length,
            time)
        {
            Channel = (FourBitNumber)(byte)channel,
            Velocity = (SevenBitNumber)100,
            OffVelocity = (SevenBitNumber)0,
        };
}
