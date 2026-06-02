using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class MeasureCommandsTests
{
    [Fact]
    public void InsertMeasures_InsertsAtStart_ShiftsAllEventsRight()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100), Note(64, 960, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([0, 1], 0, 2));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.InsertedMeasures.ShouldBe(2);
        // 2 measures of 4/4 at PPQ 480 = 2 * 4 * 480 = 3840 ticks
        result.Result.Value.ShiftedTickDelta.ShouldBe(3840);

        var notes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes[0].Tick.ShouldBe(3840);
        notes[1].Tick.ShouldBe(3840 + 960);
    }

    [Fact]
    public void InsertMeasures_InsertsInMiddle_ShiftsOnlyDownstream()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100), Note(64, 3840, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([0, 1], 1, 1));

        result.Succeeded.ShouldBeTrue();

        var notes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        notes[0].Tick.ShouldBe(0);
        notes[1].Tick.ShouldBe(3840 + 1920);
    }

    [Fact]
    public void DeleteMeasures_RemovesFirstMeasure_ShiftsRestLeft()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100), Note(64, 3840, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DeleteMeasuresCommand(),
            EditorCommandContext.Create(session),
            new DeleteMeasuresOptions([0, 1], 1, 1));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.DeletedMeasures.ShouldBe(1);

        var notes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .OrderBy(e => e.Tick)
            .ToArray();
        // First note (tick 0) was in measure 1, should be removed
        // Second note (tick 3840) was in measure 3, should shift left by 1920
        notes.Length.ShouldBe(1);
        notes[0].Tick.ShouldBe(3840 - 1920);
    }

    [Fact]
    public void InsertMeasures_InvalidCount_ValidationFails()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano"));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([0, 1], 0, 0));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void DeleteMeasures_RemovesTempoEventsInsideRange()
    {
        var conductor = CreateConductorTrack(120, 4, 4);
        var piano = CreateTrack("Piano", Note(60, 0, 100));
        piano.Events.Add(new SetTempoEvent(500000));
        var file = CreateEditableFile(conductor, piano);
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DeleteMeasuresCommand(),
            EditorCommandContext.Create(session),
            new DeleteMeasuresOptions([0, 1], 1, 1));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.DeletedMeasures.ShouldBe(1);
        result.Result.Value.RemovedMetaEvents.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void DeleteMeasures_InvalidStart_ValidationFails()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano"));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DeleteMeasuresCommand(),
            EditorCommandContext.Create(session),
            new DeleteMeasuresOptions([0, 1], 0, 1));

        result.Succeeded.ShouldBeFalse();
    }

    private static void LoadAllTrackEvents(EditableMidiFile file)
    {
        foreach (var track in file.Tracks)
            track.LoadEvents(file.TempoMap);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateConductorTrack(int bpm, int numerator, int denominator)
    {
        var chunk = new TrackChunk();
        chunk.Events.Add(new SetTempoEvent((long)(60_000_000.0 / bpm)));
        chunk.Events.Add(new TimeSignatureEvent((byte)numerator, (byte)(int)Math.Log2(denominator)));
        return chunk;
    }

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
