using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeHistoryTests
{
    [Fact]
    public void UndoRedo_RestoresTrackChunksAndDirtyState()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var history = new MidiForgeHistory();

        history.Capture(file);
        file.TransposeTracks(new[] { 0 }, 12);

        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)72);
        file.IsDirty.ShouldBeTrue();

        history.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
        file.IsDirty.ShouldBeFalse();
        history.CanRedo.ShouldBeTrue();

        history.Redo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)72);
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void Capture_PreservesLoadedEventManagerStateWithoutFlushing()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var history = new MidiForgeHistory();
        var track = file.Tracks[0];
        track.LoadEvents(file.TempoMap);
        var noteEvent = track.Events!.Single(e => e.NoteOffSource != null);

        history.Capture(file);
        noteEvent.EditValue1 = 64;
        noteEvent.ApplyEditValues();
        file.IsDirty = true;

        history.Undo(file).ShouldBeTrue();

        var restoredNote = file.Tracks[0].Chunk.GetNotes().Single();
        restoredNote.NoteNumber.ShouldBe((SevenBitNumber)60);
        restoredNote.Length.ShouldBe(120);
    }

    [Fact]
    public void Capture_ClearsRedoAfterNewMutation()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var history = new MidiForgeHistory();

        history.Capture(file);
        file.TransposeTracks(new[] { 0 }, 12);
        history.Undo(file).ShouldBeTrue();
        history.CanRedo.ShouldBeTrue();

        history.Capture(file);
        file.TransposeTracks(new[] { 0 }, -12);

        history.CanRedo.ShouldBeFalse();
    }

    private static EditableMidiFile CreateEditableFile(params Note[] notes)
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            foreach (var note in notes)
            {
                manager.Objects.Add(new TimedEvent(
                    new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                    note.Time));
                manager.Objects.Add(new TimedEvent(
                    new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                    note.EndTime));
            }
        }

        return new EditableMidiFile(new MidiFile(chunk));
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
