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
        file.Tracks[0].Events!
            .Single(editableEvent => editableEvent.Tick == 240 && editableEvent.NoteOffSource != null)
            .DurationTicks.ShouldBe(120);
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeFalse();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
    }

    [Fact]
    public void InsertNote_TrimToFitShortensBeforeNextSamePitchNote()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 300, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertNoteCommand(),
            EditorCommandContext.Create(session),
            new InsertNoteOptions(0, 100, 60, 80, 480, PreventOverlap: true, TrimToFit: true));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Events!
            .Single(editableEvent => editableEvent.Tick == 100 && editableEvent.NoteOffSource != null)
            .DurationTicks.ShouldBe(200);
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void InsertNote_PreventOverlapBlocksWhenTrimToFitIsDisabled()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 300, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new InsertNoteCommand(),
            EditorCommandContext.Create(session),
            new InsertNoteOptions(0, 100, 60, 80, 480, PreventOverlap: true, TrimToFit: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        file.Tracks[0].Events!.Count(editableEvent => editableEvent.NoteOffSource != null).ShouldBe(1);
        session.History.UndoCount.ShouldBe(0);
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
    public void DeleteNote_RemovesOnlyTargetNoteWithoutClearingSelection()
    {
        var file = CreateEditableFile(CreateTrack(
            Note(60, 0, 120),
            Note(64, 240, 120),
            Note(67, 480, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var key = NoteKey(file, eventIndex: 1);

        var result = new EditorCommandExecutor().Execute(
            new DeleteNoteCommand(),
            EditorCommandContext.Create(session),
            new DeleteNoteOptions(0, key));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Events!
            .Where(editableEvent => editableEvent.NoteOffSource != null)
            .Select(editableEvent => (int)(byte)((NoteOnEvent)editableEvent.Source.Event).NoteNumber)
            .ShouldBe(new[] { 60, 67 });
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(1);
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
    public void MoveSelectedNotes_MovesMultipleNotesInsideOneGesture()
    {
        var file = CreateEditableFile(CreateTrack(
            Note(60, 0, 120),
            Note(64, 240, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);
        var executor = new EditorCommandExecutor();

        executor.BeginGesture(context);
        var result = executor.Execute(
            new MoveSelectedNotesCommand(),
            context,
            new MoveSelectedNotesOptions(
                0,
                new[]
                {
                    new NoteEditOperation(
                        NoteKey(file, eventIndex: 0),
                        new NoteEditValues(120, 61, 91, 180)),
                    new NoteEditOperation(
                        NoteKey(file, eventIndex: 1),
                        new NoteEditValues(360, 65, 92, 240))
                }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(0);
        executor.CommitGesture(context).ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);
        file.Tracks[0].Events!
            .Where(editableEvent => editableEvent.NoteOffSource != null)
            .Select(editableEvent => (
                Tick: editableEvent.Tick,
                Note: (int)(byte)((NoteOnEvent)editableEvent.Source.Event).NoteNumber,
                Velocity: (int)(byte)((NoteOnEvent)editableEvent.Source.Event).Velocity,
                Duration: editableEvent.DurationTicks))
            .ShouldBe(new[]
            {
                (Tick: 120L, Note: 61, Velocity: 91, Duration: 180L),
                (Tick: 360L, Note: 65, Velocity: 92, Duration: 240L)
            });
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
        var editedNote = file.Tracks[0].Events!.Single(editableEvent => editableEvent.NoteOffSource != null);
        editedNote.Tick.ShouldBe(0);
        ((NoteOnEvent)editedNote.Source.Event).NoteNumber.ShouldBe((SevenBitNumber)60);
        ((NoteOnEvent)editedNote.Source.Event).Velocity.ShouldBe((SevenBitNumber)100);
        editedNote.DurationTicks.ShouldBe(360);
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

    [Fact]
    public void TransposeSelectedNotes_ClampsAtMidiBounds()
    {
        var file = CreateEditableFile(CreateTrack(
            Note(2, 0, 120),
            Note(125, 240, 120)));
        file.Tracks[0].LoadEvents(file.TempoMap);
        var session = new MidiEditorSessionState { File = file };

        var down = new EditorCommandExecutor().Execute(
            new TransposeSelectedNotesCommand(),
            EditorCommandContext.Create(session),
            new TransposeSelectedNotesOptions(
                0,
                new[] { NoteKey(file, eventIndex: 0) },
                Semitones: -12));

        down.Succeeded.ShouldBeTrue();
        down.Changed.ShouldBeTrue();
        ((NoteOnEvent)file.Tracks[0].Events![0].Source.Event).NoteNumber.ShouldBe((SevenBitNumber)0);

        var up = new EditorCommandExecutor().Execute(
            new TransposeSelectedNotesCommand(),
            EditorCommandContext.Create(session),
            new TransposeSelectedNotesOptions(
                0,
                new[] { NoteKey(file, eventIndex: 1) },
                Semitones: 12));

        up.Succeeded.ShouldBeTrue();
        up.Changed.ShouldBeTrue();
        ((NoteOnEvent)file.Tracks[0].Events![1].Source.Event).NoteNumber.ShouldBe((SevenBitNumber)127);
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
