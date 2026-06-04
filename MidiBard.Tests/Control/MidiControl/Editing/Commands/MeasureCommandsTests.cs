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
        conductor.Events.Add(new SetTempoEvent(500000));
        var file = CreateEditableFile(conductor, CreateTrack("Piano", Note(60, 0, 100)));
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

    [Fact]
    public void InsertMeasures_NoValidTracksSelected_ReturnsNoChange()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([], 0, 2));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void InsertMeasures_NoEventsAtPosition_ReturnsNoChange()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        // Insert after measure 10 when the only note is at tick 0 (measure 1)
        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([1], 10, 2));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void InsertMeasures_ConductorTrackAlwaysProcessed()
    {
        var conductor = CreateConductorTrack(120, 4, 4);
        var file = CreateEditableFile(
            conductor,
            CreateTrack("Piano", Note(60, 1920, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        // Select only the performance track (index 1); conductor (index 0) should still be shifted
        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([1], 0, 1));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();

        // Piano note shifted from tick 1920 to 1920 + 1920 = 3840
        var pianoNotes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        pianoNotes[0].Tick.ShouldBe(3840);

        // Conductor tempo event (from CreateConductorTrack at tick 0) shifted to 1920
        var conductorEvents = file.Tracks[0].Events!
            .Where(e => e.Source.Event is SetTempoEvent)
            .ToArray();
        conductorEvents.Length.ShouldBe(1);
        conductorEvents[0].Tick.ShouldBe(1920);
    }

    [Fact]
    public void InsertMeasures_ShiftTempoEventsFalse_TempoNotShifted()
    {
        var conductor = CreateConductorTrack(120, 4, 4);
        var file = CreateEditableFile(
            conductor,
            CreateTrack("Piano", Note(60, 1920, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([1], 0, 1, ShiftTempoEvents: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();

        // Piano note still shifted
        var pianoNotes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        pianoNotes[0].Tick.ShouldBe(3840);

        // Conductor tempo event NOT shifted when ShiftTempoEvents=false
        var tempoEvents = file.Tracks[0].Events!
            .Where(e => e.Source.Event is SetTempoEvent)
            .ToArray();
        tempoEvents.Length.ShouldBe(1);
        tempoEvents[0].Tick.ShouldBe(0);
    }

    [Fact]
    public void InsertMeasures_MultiplePerformanceTracks_AllShifted()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100)),
            CreateTrack("Guitar", Note(64, 0, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([1, 2], 0, 1));

        result.Succeeded.ShouldBeTrue();
        // Each track has 1 note; Events list contains NoteOn only (NoteOff is paired)
        result.Result!.Value.ShiftedNoteEvents.ShouldBe(2);

        var pianoNotes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        pianoNotes[0].Tick.ShouldBe(1920);

        var guitarNotes = file.Tracks[2].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        guitarNotes[0].Tick.ShouldBe(1920);
    }

    [Fact]
    public void InsertMeasures_NoteOffPairedWithNoteOn()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([1], 0, 1));

        result.Succeeded.ShouldBeTrue();

        var noteOn = file.Tracks[1].Events!
            .First(e => e.Source.Event is NoteOnEvent);
        var noteOff = noteOn.NoteOffSource!;

        noteOn.Tick.ShouldBe(1920);
        noteOff.Time.ShouldBe(1920 + 100);
    }

    [Fact]
    public void InsertMeasures_UserMessageAndRefreshHints()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new InsertMeasuresCommand(),
            EditorCommandContext.Create(session),
            new InsertMeasuresOptions([1], 0, 1));

        result.Succeeded.ShouldBeTrue();
        result.Result.ShouldNotBeNull();
        result.Result.UserMessage.ShouldNotBeNullOrWhiteSpace();
        result.Result.UserMessage.ShouldContain("Inserted");
        result.Result.UserMessage.ShouldContain("1");
        result.Result.UserMessage.ShouldContain("track");
        result.Result.RefreshHints.ShouldNotBeNull();
        result.Result.RefreshHints!.ReloadSelectedTrack.ShouldBeTrue();
        result.Result.RefreshHints.RebuildPreview.ShouldBeTrue();
    }

    [Fact]
    public void DeleteMeasures_NoEventsInRange_ReturnsNoChange()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        // Delete measures 3-4 when the only note is at tick 0 (measure 1)
        var result = new EditorCommandExecutor().Execute(
            new DeleteMeasuresCommand(),
            EditorCommandContext.Create(session),
            new DeleteMeasuresOptions([1], 3, 2));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void DeleteMeasures_ConductorTrackAlwaysProcessed()
    {
        var conductor = CreateConductorTrack(120, 4, 4);
        conductor.Events.Add(new SetTempoEvent(500000));
        var file = CreateEditableFile(
            conductor,
            CreateTrack("Piano", Note(60, 1920, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        // Select only performance track; conductor should still be processed
        var result = new EditorCommandExecutor().Execute(
            new DeleteMeasuresCommand(),
            EditorCommandContext.Create(session),
            new DeleteMeasuresOptions([1], 1, 1));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();

        // Piano note shifted from 1920 to 0
        var pianoNotes = file.Tracks[1].Events!
            .Where(e => e.NoteOffSource != null)
            .ToArray();
        pianoNotes[0].Tick.ShouldBe(0);

        // Conductor tempo event removed from measure 1
        var conductorEvents = file.Tracks[0].Events!
            .Where(e => e.Source.Event is SetTempoEvent)
            .ToArray();
        conductorEvents.Length.ShouldBe(0);
    }

    [Fact]
    public void DeleteMeasures_UserMessageAndRefreshHints()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, 4, 4),
            CreateTrack("Piano", Note(60, 0, 100), Note(64, 3840, 100)));
        LoadAllTrackEvents(file);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DeleteMeasuresCommand(),
            EditorCommandContext.Create(session),
            new DeleteMeasuresOptions([1], 1, 1));

        result.Succeeded.ShouldBeTrue();
        result.Result.ShouldNotBeNull();
        result.Result.UserMessage.ShouldNotBeNullOrWhiteSpace();
        result.Result.UserMessage.ShouldContain("Deleted");
        result.Result.UserMessage.ShouldContain("Removed");
        result.Result.UserMessage.ShouldContain("Shifted");
        result.Result.RefreshHints.ShouldNotBeNull();
        result.Result.RefreshHints!.ReloadSelectedTrack.ShouldBeTrue();
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
