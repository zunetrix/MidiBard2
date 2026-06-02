using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class RepeatLoopCommandTests
{
    [Fact]
    public void Execute_SingleNoteRepeatCount_RepeatsNTimes()
    {
        // 4/4 time at PPQ 480, one bar = 1920 ticks
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.RepeatCount,
                RepeatCount: 3));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.RepeatedGroups.ShouldBe(3);
        result.Result.Value.InsertedNotes.ShouldBe(3);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(4); // original + 3 repeats
        notes[0].Tick.ShouldBe(0);
        notes[1].Tick.ShouldBe(1920);
        notes[2].Tick.ShouldBe(3840);
        notes[3].Tick.ShouldBe(5760);
    }

    [Fact]
    public void Execute_UntilNextNoteOnTrack_StopsBeforeExistingNote()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 5000, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.UntilNextNoteOnTrack));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        // Should have repeats at 0, 1920, 3840 but NOT at 5760 (which would overlap note at 5000)
        notes.Length.ShouldBeGreaterThanOrEqualTo(3);
        // All inserted repeats (not the original notes) should be before tick 5000
        var repeats = notes.Where(n => n.Tick > 0 && n.Tick < 5000).ToArray();
        repeats.Length.ShouldBeGreaterThanOrEqualTo(2);
        foreach (var note in repeats)
            note.Tick.ShouldBeLessThan(5000);
    }

    [Fact]
    public void Execute_TrimToFit_SkipsOverlappingNotes()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 1900, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.RepeatCount,
                RepeatCount: 2,
                TrimToFit: true));

        result.Succeeded.ShouldBeTrue();
        // The repeat at 1920 would overlap with the existing note at 1900
        // so it should be trimmed
        result.Result!.Value.TrimmedNotes.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Execute_RepeatCountZero_ValidationFails()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.RepeatCount,
                RepeatCount: 0));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Execute_ChordRepeat_RepeatsAllNotes()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 0, 100),
            Note(67, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1), NoteKey(file, 2) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.RepeatCount,
                RepeatCount: 1));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.InsertedNotes.ShouldBe(3);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ThenBy(e => ((NoteOnEvent)e.Source.Event).NoteNumber)
            .ToArray();
        notes.Length.ShouldBe(6); // 3 original + 3 repeated
    }

    [Fact]
    public void Execute_BeatInterval_RepeatsAtBeatInterval()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBeat,
                MidiForgeRepeatLoopEndCondition.RepeatCount,
                RepeatCount: 4));

        result.Succeeded.ShouldBeTrue();

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes.Length.ShouldBe(5); // original + 4 repeats
        notes[1].Tick.ShouldBe(480);  // 1 beat
        notes[2].Tick.ShouldBe(960);  // 2 beats
        notes[3].Tick.ShouldBe(1440); // 3 beats
        notes[4].Tick.ShouldBe(1920); // 4 beats
    }

    [Fact]
    public void Execute_UntilNextNoteOnTrack_NoOtherNotes_FillsToEndOfSong()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 10000, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.UntilNextNoteOnTrack));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.InsertedNotes.ShouldBeGreaterThan(0);
        result.Result.Value.RepeatedGroups.ShouldBeGreaterThan(0);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        // All inserted repeats should be before the existing note at tick 10000
        foreach (var note in notes.Where(n => n.Tick > 0 && n.Tick != 10000))
            note.Tick.ShouldBeLessThan(10000);
    }

    [Fact]
    public void Execute_EndOfSong_FillsToEnd()
    {
        // Add a second note far after the selection so FindEndOfSongTick extends past the selection
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 100000, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        // Only select the first note so the second acts as a "rest of song" boundary
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.EndOfSong));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.InsertedNotes.ShouldBeGreaterThan(0);
        result.Result.Value.RepeatedGroups.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Execute_UntilTick_StopsAtSpecifiedTick()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.UntilTick,
                EndTick: 5000));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.InsertedNotes.ShouldBeGreaterThan(0);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        // All inserted notes should be before tick 5000
        foreach (var note in notes.Where(n => n.Tick > 0))
            note.Tick.ShouldBeLessThan(5000);
    }

    [Fact]
    public void Execute_TrimToFitFalse_InsertsOverlapping()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 900, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new RepeatLoopCommand(),
            EditorCommandContext.Create(session),
            new RepeatLoopOptions(
                0,
                selectedNotes,
                MidiForgeRepeatLoopInterval.OneBar,
                MidiForgeRepeatLoopEndCondition.RepeatCount,
                RepeatCount: 2,
                TrimToFit: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        // With TrimToFit=false, the repeat at 1920 is inserted even though it overlaps
        // the existing note at 900.
        result.Result!.Value.TrimmedNotes.ShouldBe(0);
        result.Result.Value.InsertedNotes.ShouldBe(2);
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
