using System;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Conductor;
using MidiBard.Control.MidiControl.Editing.State;

using Shouldly;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ConductorCommandsTests
{
    [Fact]
    public void SetTempo_CreatesConductorTrackWhenNoneExists()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 150));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.CreatedConductorTrack.ShouldBeTrue();

        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        var tempo = file.Tracks[0].Chunk.GetTimedEvents()
            .Single(te => te.Event is SetTempoEvent);
        tempo.Time.ShouldBe(0);
        ((SetTempoEvent)tempo.Event).MicrosecondsPerQuarterNote
            .ShouldBe((long)(60_000_000.0 / 150));
    }

    [Fact]
    public void SetTempo_UsesExistingConductorTrack()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120),
            CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var conductorIndex = file.Tracks.Select((t, i) => (t, i)).First(x => x.t.IsConductorTrack).i;

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 480, Bpm: 140));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.CreatedConductorTrack.ShouldBeFalse();

        file.Tracks.Count.ShouldBe(2);
        var tempos = file.Tracks[conductorIndex].Chunk.GetTimedEvents()
            .Where(te => te.Event is SetTempoEvent)
            .ToList();
        tempos.Count.ShouldBe(2);
        ((SetTempoEvent)tempos.Single(te => te.Time == 480).Event).MicrosecondsPerQuarterNote
            .ShouldBe((long)(60_000_000.0 / 140));
    }

    [Fact]
    public void SetTempo_ReplacesExistingEventAtSameTick()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120),
            CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var conductorIndex = file.Tracks.Select((t, i) => (t, i)).First(x => x.t.IsConductorTrack).i;

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 150));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ReplacedEventIndex.ShouldBe(1);

        var tempos = file.Tracks[conductorIndex].Chunk.GetTimedEvents()
            .Where(te => te.Event is SetTempoEvent)
            .ToList();
        tempos.Count.ShouldBe(1);
        ((SetTempoEvent)tempos[0].Event).MicrosecondsPerQuarterNote
            .ShouldBe((long)(60_000_000.0 / 150));
    }

    [Fact]
    public void SetTempo_NoOpWhenBpmMatches()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120),
            CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 120));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void SetTempo_ValidationRejectsInvalidBpm()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 0));

        result.Succeeded.ShouldBeFalse();

        result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 301));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void SetTempo_ValidationRejectsNegativeTick()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: -1, Bpm: 120));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void SetTempo_SupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 150));

        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count(track => track.IsConductorTrack).ShouldBe(0);
        file.Tracks[0].Chunk.Events.OfType<SetTempoEvent>().Count().ShouldBe(0);
    }

    [Fact]
    public void SetTempo_RefreshHintsIncludePreviewAndMetrics()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTempoAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTempoOptions(Tick: 0, Bpm: 150));

        result.Result!.RefreshHints.ShouldNotBeNull();
        result.Result!.RefreshHints!.RebuildPreview.ShouldBeTrue();
        result.Result!.RefreshHints!.RecalculateMetrics.ShouldBeTrue();
        result.Result!.RefreshHints!.ReloadTrackList.ShouldBeTrue();
    }

    [Fact]
    public void SetTimeSignature_CreatesConductorTrackWhenNoneExists()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 3, Denominator: 4));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.CreatedConductorTrack.ShouldBeTrue();

        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        var ts = file.Tracks[0].Chunk.GetTimedEvents()
            .Single(te => te.Event is TimeSignatureEvent);
        ts.Time.ShouldBe(0);
        var tsEvent = (TimeSignatureEvent)ts.Event;
        tsEvent.Numerator.ShouldBe((byte)3);
        tsEvent.Denominator.ShouldBe((byte)4);
    }

    [Fact]
    public void SetTimeSignature_ReplacesExistingEventAtSameTick()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, numerator: 4, denominator: 2),
            CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var conductorIndex = file.Tracks.Select((t, i) => (t, i)).First(x => x.t.IsConductorTrack).i;

        var result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 6, Denominator: 8));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();

        var timeSigs = file.Tracks[conductorIndex].Chunk.GetTimedEvents()
            .Where(te => te.Event is TimeSignatureEvent)
            .ToList();
        timeSigs.Count.ShouldBe(1);
        var tsEvent = (TimeSignatureEvent)timeSigs[0].Event;
        tsEvent.Numerator.ShouldBe((byte)6);
        tsEvent.Denominator.ShouldBe((byte)8);
    }

    [Fact]
    public void SetTimeSignature_NoOpWhenValuesMatch()
    {
        var file = CreateEditableFile(
            CreateConductorTrack(120, numerator: 4, denominator: 2),
            CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 4, Denominator: 2));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
    }

    [Fact]
    public void SetTimeSignature_ValidationRejectsInvalidNumerator()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 0, Denominator: 4));

        result.Succeeded.ShouldBeFalse();

        result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 33, Denominator: 4));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void SetTimeSignature_ValidationRejectsNonPowerOfTwoDenominator()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 4, Denominator: 3));

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void SetTimeSignature_SupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 3, Denominator: 4));

        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count(track => track.IsConductorTrack).ShouldBe(0);
    }

    [Fact]
    public void SetTimeSignature_RefreshHintsIncludePreviewAndMetrics()
    {
        var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTimeSignatureAtTickCommand(),
            EditorCommandContext.Create(session),
            new SetTimeSignatureOptions(Tick: 0, Numerator: 4, Denominator: 4));

        result.Result!.RefreshHints.ShouldNotBeNull();
        result.Result!.RefreshHints!.RebuildPreview.ShouldBeTrue();
        result.Result!.RefreshHints!.RecalculateMetrics.ShouldBeTrue();
    }

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

    private static TrackChunk CreateConductorTrack(int bpm, int numerator = 4, int denominator = 2)
    {
        var chunk = new TrackChunk();
        chunk.Events.Add(new SetTempoEvent((long)(60_000_000.0 / bpm)));
        chunk.Events.Add(new TimeSignatureEvent((byte)numerator, (byte)denominator));
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
