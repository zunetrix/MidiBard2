using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitAtPositionCommandTests
{
    [Fact]
    public void Execute_NoteSpanningSplitPoint_SplitsIntoTwo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 100, 200)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitAtPositionCommand(),
            EditorCommandContext.Create(session),
            new SplitAtPositionOptions(0, 150));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SplitNotes.ShouldBe(1);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(2);
        notes[0].Tick.ShouldBe(100);
        notes[0].DurationTicks.ShouldBe(50);
        notes[1].Tick.ShouldBe(150);
        notes[1].DurationTicks.ShouldBe(150);
    }

    [Fact]
    public void Execute_NoteNotSpanning_Unchanged()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 100, 50)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitAtPositionCommand(),
            EditorCommandContext.Create(session),
            new SplitAtPositionOptions(0, 200));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Execute_NoteStartingAtSplit_Unchanged()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 150, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitAtPositionCommand(),
            EditorCommandContext.Create(session),
            new SplitAtPositionOptions(0, 150));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Execute_NoteEndingAtSplit_Unchanged()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 100, 50)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitAtPositionCommand(),
            EditorCommandContext.Create(session),
            new SplitAtPositionOptions(0, 150));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Execute_MultipleNotesSpanning_SplitsAll()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 100, 200),
            Note(64, 120, 150)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitAtPositionCommand(),
            EditorCommandContext.Create(session),
            new SplitAtPositionOptions(0, 180));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SplitNotes.ShouldBe(2);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(4);
    }

    [Fact]
    public void Execute_SplitAtZero_ValidationFails()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 200)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitAtPositionCommand(),
            EditorCommandContext.Create(session),
            new SplitAtPositionOptions(0, 0));

        result.Succeeded.ShouldBeFalse();
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
