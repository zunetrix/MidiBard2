using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class StrumNotesCommandTests
{
    [Fact]
    public void Execute_TrackModeStrumsLowToHighAndPreservesEnds()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 0, 100),
            Note(67, 0, 100),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new StrumNotesCommand(),
            EditorCommandContext.Create(session),
            new StrumNotesCommandOptions(
                new[] { 0 },
                -1,
                Array.Empty<NoteSelectionKey>(),
                new MidiForgeStrumNotesOptions(
                    Direction: MidiForgeStrumDirection.LowToHigh,
                    StepTicks: 5,
                    PreserveNoteEnds: true)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.StrummedChordGroups.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("Piano (Strummed)");
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time, note.Length))
            .ShouldBe(new[] { (60, 0L, 100L), (64, 5L, 95L), (67, 10L, 90L), (72, 240L, 120L) });
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Execute_TrackModeHighToLowCanMoveEnds()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 0, 100),
            Note(67, 0, 100)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new StrumNotesCommand(),
            EditorCommandContext.Create(session),
            new StrumNotesCommandOptions(
                new[] { 0 },
                -1,
                Array.Empty<NoteSelectionKey>(),
                new MidiForgeStrumNotesOptions(
                    Direction: MidiForgeStrumDirection.HighToLow,
                    StepTicks: 5,
                    PreserveNoteEnds: false)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time, note.Length))
            .ShouldBe(new[] { (67, 0L, 100L), (64, 5L, 100L), (60, 10L, 100L) });
    }

    [Fact]
    public void Execute_TolerantGroupingStrumsMisalignedChord()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 8, 100),
            Note(67, 12, 100)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new StrumNotesCommand(),
            EditorCommandContext.Create(session),
            new StrumNotesCommandOptions(
                new[] { 0 },
                -1,
                Array.Empty<NoteSelectionKey>(),
                new MidiForgeStrumNotesOptions(
                    StepTicks: 5,
                    PreserveNoteEnds: false,
                    ChordTimingTolerance: new MidiForgeChordTimingToleranceOptions(
                        MidiForgeChordTimingToleranceMode.OneOver128Note))));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.StrummedChordGroups.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time))
            .ShouldBe(new[] { (60, 0L), (64, 13L), (67, 22L) });
    }

    [Fact]
    public void Execute_SelectedNotesEditsCurrentTrackWithoutCreatingTrack()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 100),
            Note(64, 0, 100),
            Note(67, 0, 100)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var selectedNotes = new[]
        {
            NoteKey(file, 0),
            NoteKey(file, 1),
            NoteKey(file, 2),
        };

        var result = new EditorCommandExecutor().Execute(
            new StrumNotesCommand(),
            EditorCommandContext.Create(session),
            new StrumNotesCommandOptions(
                Array.Empty<int>(),
                0,
                selectedNotes,
                new MidiForgeStrumNotesOptions(
                    StepTicks: 5,
                    PreserveNoteEnds: true)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedNotes.ShouldBe(2);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Events!
            .Where(editableEvent => editableEvent.NoteOffSource != null)
            .Select(editableEvent => (
                (int)(byte)((NoteOnEvent)editableEvent.Source.Event).NoteNumber,
                editableEvent.Tick,
                editableEvent.DurationTicks))
            .ShouldBe(new[] { (60, 0L, 100L), (64, 5L, 95L), (67, 10L, 90L) });
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
