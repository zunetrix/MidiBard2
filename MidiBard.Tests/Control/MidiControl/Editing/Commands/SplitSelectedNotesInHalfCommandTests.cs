using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitSelectedNotesInHalfCommandTests
{
    [Fact]
    public void Execute_SingleNote_SplitsIntoTwoEqualHalves()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new SplitSelectedNotesInHalfCommand(),
            EditorCommandContext.Create(session),
            new SplitSelectedNotesInHalfOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SplitCount.ShouldBe(1);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(2);
        notes[0].Tick.ShouldBe(0);
        notes[0].DurationTicks.ShouldBe(50);
        notes[1].Tick.ShouldBe(50);
        notes[1].DurationTicks.ShouldBe(50);
    }

    [Fact]
    public void Execute_OddDuration_RoundsFirstHalf()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 101)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new SplitSelectedNotesInHalfCommand(),
            EditorCommandContext.Create(session),
            new SplitSelectedNotesInHalfOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(2);
        notes[0].DurationTicks.ShouldBe(50);
        notes[1].DurationTicks.ShouldBe(51);
        (notes[0].DurationTicks + notes[1].DurationTicks).ShouldBe(101);
    }

    [Fact]
    public void Execute_VeryShortNote_Skips()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 1)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new SplitSelectedNotesInHalfCommand(),
            EditorCommandContext.Create(session),
            new SplitSelectedNotesInHalfOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SplitCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_MultipleNotes_SplitsAll()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 200, 200)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1) };

        var result = new EditorCommandExecutor().Execute(
            new SplitSelectedNotesInHalfCommand(),
            EditorCommandContext.Create(session),
            new SplitSelectedNotesInHalfOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SplitCount.ShouldBe(2);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(4);
    }

    [Fact]
    public void Execute_PreservesPitchAndVelocity()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(72, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new SplitSelectedNotesInHalfCommand(),
            EditorCommandContext.Create(session),
            new SplitSelectedNotesInHalfOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        foreach (var note in notes)
        {
            var noteOn = (NoteOnEvent)note.Source.Event;
            ((int)(byte)noteOn.NoteNumber).ShouldBe(72);
            ((int)(byte)noteOn.Velocity).ShouldBe(100);
        }
    }

    private static NoteSelectionKey NoteKey(EditableMidiFile file, int noteIndex)
    {
        var events = file.Tracks[0].Events!;
        var eventIndex = events
            .Select((editableEvent, index) => (editableEvent, index))
            .Where(item => item.editableEvent.NoteOffSource != null)
            .ElementAt(noteIndex)
            .index;

        return NoteSelectionKey.FromEvent(eventIndex, events[eventIndex]);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string name, params Note[] notes)
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent(name));
        using var manager = chunk.ManageTimedEvents();

        foreach (var note in notes)
        {
            manager.Objects.Add(new TimedEvent(
                new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                note.Time));
            manager.Objects.Add(new TimedEvent(
                new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                note.EndTime));
        }

        return chunk;
    }

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
