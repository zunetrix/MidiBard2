using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class GlueNotesCommandTests
{
    [Fact]
    public void Execute_TwoSamePitchOverlappingNotes_GluesIntoOne()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 50, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1) };

        var result = new EditorCommandExecutor().Execute(
            new GlueNotesCommand(),
            EditorCommandContext.Create(session),
            new GlueNotesOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.GluedGroups.ShouldBe(1);
        result.Result.Value.OutputNotes.ShouldBe(1);
        result.Result.Value.InputNotes.ShouldBe(2);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        notes.Length.ShouldBe(1);
        notes[0].Tick.ShouldBe(0);
        notes[0].DurationTicks.ShouldBe(150);
    }

    [Fact]
    public void Execute_TwoSamePitchWithGap_GluesAcrossGap()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 200, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1) };

        var result = new EditorCommandExecutor().Execute(
            new GlueNotesCommand(),
            EditorCommandContext.Create(session),
            new GlueNotesOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.GluedGroups.ShouldBe(1);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        notes.Length.ShouldBe(1);
        notes[0].Tick.ShouldBe(0);
        notes[0].DurationTicks.ShouldBe(300);
    }

    [Fact]
    public void Execute_ThreeSamePitch_GluesIntoOne()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 50),
            Note(60, 30, 50),
            Note(60, 60, 50)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1), NoteKey(file, 2) };

        var result = new EditorCommandExecutor().Execute(
            new GlueNotesCommand(),
            EditorCommandContext.Create(session),
            new GlueNotesOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.GluedGroups.ShouldBe(1);
        result.Result.Value.OutputNotes.ShouldBe(1);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        notes.Length.ShouldBe(1);
        notes[0].Tick.ShouldBe(0);
        notes[0].DurationTicks.ShouldBe(110);
    }

    [Fact]
    public void Execute_DifferentPitches_NoChange()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1) };

        var result = new EditorCommandExecutor().Execute(
            new GlueNotesCommand(),
            EditorCommandContext.Create(session),
            new GlueNotesOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void Execute_SingleNoteSelected_ValidationFails()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0) };

        var result = new EditorCommandExecutor().Execute(
            new GlueNotesCommand(),
            EditorCommandContext.Create(session),
            new GlueNotesOptions(0, selectedNotes));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Execute_MultiplePitchGroups_GluesEachGroup()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(60, 50, 100),
            Note(64, 0, 100),
            Note(64, 50, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[] { NoteKey(file, 0), NoteKey(file, 1), NoteKey(file, 2), NoteKey(file, 3) };

        var result = new EditorCommandExecutor().Execute(
            new GlueNotesCommand(),
            EditorCommandContext.Create(session),
            new GlueNotesOptions(0, selectedNotes));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.GluedGroups.ShouldBe(2);
        result.Result.Value.OutputNotes.ShouldBe(2);

        var notes = file.Tracks[0].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => ((NoteOnEvent)e.Source.Event).NoteNumber)
            .ToArray();
        notes.Length.ShouldBe(2);
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
