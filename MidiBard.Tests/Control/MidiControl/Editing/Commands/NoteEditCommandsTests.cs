using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class NoteEditCommandsTests
{
    [Fact]
    public void InsertNote_AddsNoteAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertNoteCommand(),
            EditorCommandContext.Create(session),
            new InsertNoteOptions(
                TrackIndex: 0,
                Tick: 240,
                NoteNumber: 64,
                Velocity: 80,
                DurationTicks: 120,
                PreventOverlap: true,
                TrimToFit: true));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);
        result.Result.Value.InsertedEventIndex.ShouldBeGreaterThanOrEqualTo(0);
        file.Tracks[0].Events!.Count(editableEvent => editableEvent.NoteOffSource != null).ShouldBe(2);
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeFalse();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
    }

    [Fact]
    public void InsertNote_PreventOverlapBlocksWhenClickIsInsideExistingNote()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 240)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new InsertNoteCommand(),
            EditorCommandContext.Create(session),
            new InsertNoteOptions(0, 120, 60, 80, 120, PreventOverlap: true, TrimToFit: true));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void DeleteSelectedNotes_ComposesEventDeletionWithoutClearingSelection()
    {
        var file = CreateEditableFile(CreateTrack(
            Note(60, 0, 120),
            Note(64, 240, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var key = NoteKey(file, eventIndex: 0);

        var result = new EditorCommandExecutor().Execute(
            new DeleteSelectedNotesCommand(),
            EditorCommandContext.Create(session),
            new DeleteSelectedNotesOptions(0, new[] { key }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);
        file.Tracks[0].Events!.Count(editableEvent => editableEvent.NoteOffSource != null).ShouldBe(1);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void MoveSelectedNotes_GestureCommitsOneUndoSnapshot()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);
        var executor = new EditorCommandExecutor();

        executor.BeginGesture(context);
        executor.Execute(
            new MoveSelectedNotesCommand(),
            context,
            new MoveSelectedNotesOptions(
                0,
                new[]
                {
                    new NoteEditOperation(
                        NoteKey(file, eventIndex: 0),
                        new NoteEditValues(120, 62, 90, 180))
                })).Succeeded.ShouldBeTrue();
        executor.Execute(
            new MoveSelectedNotesCommand(),
            context,
            new MoveSelectedNotesOptions(
                0,
                new[]
                {
                    new NoteEditOperation(
                        NoteKey(file, eventIndex: 0),
                        new NoteEditValues(240, 64, 90, 180))
                })).Succeeded.ShouldBeTrue();

        session.History.UndoCount.ShouldBe(0);
        executor.CommitGesture(context).ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);
        var editedNote = file.Tracks[0].Events!.Single(editableEvent => editableEvent.NoteOffSource != null);
        editedNote.Tick.ShouldBe(240);
        ((NoteOnEvent)editedNote.Source.Event).NoteNumber.ShouldBe((SevenBitNumber)64);
        editedNote.DurationTicks.ShouldBe(180);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Single().Time.ShouldBe(0);
    }

    [Fact]
    public void ResizeSelectedNotes_ChangesDuration()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ResizeSelectedNotesCommand(),
            EditorCommandContext.Create(session),
            new ResizeSelectedNotesOptions(
                0,
                new[]
                {
                    new NoteEditOperation(
                        NoteKey(file, eventIndex: 0),
                        new NoteEditValues(0, 60, 100, 360))
                }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Events!.Single(editableEvent => editableEvent.NoteOffSource != null)
            .DurationTicks.ShouldBe(360);
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void TransposeSelectedNotes_UsesNoteMoveCommandAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TransposeSelectedNotesCommand(),
            EditorCommandContext.Create(session),
            new TransposeSelectedNotesOptions(
                0,
                new[] { NoteKey(file, eventIndex: 0) },
                Semitones: 12));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);
        ((NoteOnEvent)file.Tracks[0].Events!.Single(editableEvent => editableEvent.NoteOffSource != null).Source.Event)
            .NoteNumber.ShouldBe((SevenBitNumber)72);
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    private static NoteSelectionKey NoteKey(EditableMidiFile file, int eventIndex)
        => NoteSelectionKey.FromEvent(eventIndex, file.Tracks[0].Events![eventIndex]);

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(params Note[] notes)
    {
        var chunk = new TrackChunk();
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
