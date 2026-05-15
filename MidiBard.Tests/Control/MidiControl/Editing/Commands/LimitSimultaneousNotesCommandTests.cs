using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class LimitSimultaneousNotesCommandTests
{
    [Fact]
    public void Execute_SameStartKeepsHighestNote()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new LimitSimultaneousNotesCommand(),
            EditorCommandContext.Create(session),
            new LimitSimultaneousNotesCommandOptions(
                new[] { 0 },
                new MidiForgeLimitSimultaneousNotesOptions(
                    LimitMode: MidiForgeSimultaneousLimitMode.SameStartChordsOnly,
                    MaximumActiveNotes: 1,
                    KeepPolicy: MidiForgeNoteKeepPolicy.Highest)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.RemovedNotes.ShouldBe(3);
        result.Result.UserMessage.ShouldContain("removed 3 note(s)");
        file.Tracks[1].Name.ShouldBe("Piano (Limited Max 1)");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 72 });
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Execute_SameStartCanKeepLowestNote()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new LimitSimultaneousNotesCommand(),
            EditorCommandContext.Create(session),
            new LimitSimultaneousNotesCommandOptions(
                new[] { 0 },
                new MidiForgeLimitSimultaneousNotesOptions(
                    LimitMode: MidiForgeSimultaneousLimitMode.SameStartChordsOnly,
                    MaximumActiveNotes: 1,
                    KeepPolicy: MidiForgeNoteKeepPolicy.Lowest)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60 });
    }

    [Fact]
    public void Execute_ActiveOverlapRemovesSustainedNotesWhoseStartsDiffer()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(48, 0, 480),
            Note(60, 120, 120),
            Note(64, 120, 120),
            Note(67, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new LimitSimultaneousNotesCommand(),
            EditorCommandContext.Create(session),
            new LimitSimultaneousNotesCommandOptions(
                new[] { 0 },
                new MidiForgeLimitSimultaneousNotesOptions(
                    LimitMode: MidiForgeSimultaneousLimitMode.ActiveOverlaps,
                    MaximumActiveNotes: 2,
                    KeepPolicy: MidiForgeNoteKeepPolicy.Highest)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.RemovedNotes.ShouldBe(2);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 64, 67 });
    }

    [Fact]
    public void Execute_MiddlePolicyKeepsCenterPitches()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new LimitSimultaneousNotesCommand(),
            EditorCommandContext.Create(session),
            new LimitSimultaneousNotesCommandOptions(
                new[] { 0 },
                new MidiForgeLimitSimultaneousNotesOptions(
                    LimitMode: MidiForgeSimultaneousLimitMode.SameStartChordsOnly,
                    MaximumActiveNotes: 2,
                    KeepPolicy: MidiForgeNoteKeepPolicy.Middle)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 64, 67 });
    }

    [Fact]
    public void Execute_NonOverlappingNotesDoNotDirtyFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 120, 120),
            Note(67, 240, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new LimitSimultaneousNotesCommand(),
            EditorCommandContext.Create(session),
            new LimitSimultaneousNotesCommandOptions(
                new[] { 0 },
                new MidiForgeLimitSimultaneousNotesOptions(
                    LimitMode: MidiForgeSimultaneousLimitMode.ActiveOverlaps,
                    MaximumActiveNotes: 1)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.Tracks.Count.ShouldBe(1);
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
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
