using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SanitizeFileTrimTests
{
    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk NoteTrack(params Note[] notes)
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent("Track"));
        using var manager = chunk.ManageNotes();
        foreach (var note in notes)
            manager.Objects.Add(note);
        return chunk;
    }

    private static TrackChunk ConductorTrack(params TimedEvent[] events)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();
        foreach (var te in events)
            manager.Objects.Add(te);
        return chunk;
    }

    private static Note Note(int noteNumber, long time, long length, int channel = 0)
        => new((SevenBitNumber)(byte)noteNumber, length, time)
        {
            Channel = (FourBitNumber)(byte)channel,
            Velocity = (SevenBitNumber)100,
        };

    private static TimedEvent TempoEvent(long tick, int bpm)
        => new(new SetTempoEvent((long)(60_000_000.0 / bpm)), tick);

    private static TimedEvent TimeSigEvent(long tick, byte num, byte den)
        => new(new TimeSignatureEvent(num, den), tick);

    private static TimedEvent KeySigEvent(long tick, sbyte key, byte scale)
        => new(new KeySignatureEvent(key, scale), tick);

    // =================================================================
    // TrimSilenceAtStart — synthetic gapped files
    // =================================================================

    [Fact]
    public void TrimSilenceAtStart_RemovesSilenceFromGappedFile()
    {
        var note = Note(60, 480, 240);
        var file = CreateEditableFile(NoteTrack(note));

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();
        var notes = file.Tracks[0].Chunk.GetNotes().ToList();
        notes.Count.ShouldBe(1);
        notes[0].Time.ShouldBe(0);
        notes[0].Length.ShouldBe(240);
    }

    [Fact]
    public void TrimSilenceAtStart_TrimsToEarliestNoteAcrossTracks()
    {
        var file = CreateEditableFile(
            NoteTrack(Note(60, 960, 120)),
            NoteTrack(Note(64, 480, 120)));

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();
        var notes = file.Tracks[1].Chunk.GetNotes().ToList();
        notes[0].Time.ShouldBe(0);
        var notes0 = file.Tracks[0].Chunk.GetNotes().ToList();
        notes0[0].Time.ShouldBe(480);
    }

    [Fact]
    public void TrimSilenceAtStart_IgnoresConductorTrackWhenFindingTrimTick()
    {
        var conductor = ConductorTrack(
            TempoEvent(0, 120),
            TempoEvent(480, 86));
        var noteTrack = NoteTrack(Note(60, 960, 240));
        var file = CreateEditableFile(conductor, noteTrack);

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        // Trim point should be 960 (first note in track 1), ignoring conductor events
        changed.ShouldBeTrue();
        var notes = file.Tracks[1].Chunk.GetNotes().ToList();
        notes[0].Time.ShouldBe(0);
    }

    [Fact]
    public void TrimSilenceAtStart_ConsolidatesConductorTempo()
    {
        var conductor = ConductorTrack(
            TempoEvent(0, 120),
            TempoEvent(480, 86));
        var noteTrack = NoteTrack(Note(60, 960, 240));
        var file = CreateEditableFile(conductor, noteTrack);

        // Debug: check conductor chunk before trim
        var conductorChunkBefore = file.Tracks[0].Chunk;
        conductorChunkBefore.Events.Count.ShouldBe(2, "conductor should have 2 events before trim");
        conductorChunkBefore.Events.OfType<SetTempoEvent>().Count().ShouldBe(2);

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();

        // Debug: inspect conductor chunk after trim
        var conductorChunk = file.Tracks[0].Chunk;
        conductorChunk.Events.Count.ShouldBeGreaterThan(0, "conductor chunk should have events after trim");
        conductorChunk.Events.OfType<SetTempoEvent>().Count().ShouldBe(1, "conductor should have 1 tempo after trim");

        var conductorTrack = file.Tracks[0];
        conductorTrack.IsConductorTrack.ShouldBeTrue();
        var tempos = conductorTrack.Chunk.GetTimedEvents()
            .Where(te => te.Event is SetTempoEvent)
            .ToList();
        tempos.Count.ShouldBe(1);
        tempos[0].Time.ShouldBe(0);
        ((SetTempoEvent)tempos[0].Event).MicrosecondsPerQuarterNote.ShouldBe(
            (long)(60_000_000.0 / 86));
    }

    [Fact]
    public void TrimSilenceAtStart_ConsolidatesConductorTimeSig()
    {
        var conductor = ConductorTrack(
            TimeSigEvent(0, 4, 2),
            TimeSigEvent(480, 6, 2));
        var noteTrack = NoteTrack(Note(60, 960, 240));
        var file = CreateEditableFile(conductor, noteTrack);

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();
        var conductorTrack = file.Tracks[0];
        var timeSigs = conductorTrack.Chunk.GetTimedEvents()
            .Where(te => te.Event is TimeSignatureEvent)
            .ToList();
        timeSigs.Count.ShouldBe(1);
        timeSigs[0].Time.ShouldBe(0);
        var sig = (TimeSignatureEvent)timeSigs[0].Event;
        ((int)sig.Numerator).ShouldBe(6);
        ((int)sig.Denominator).ShouldBe(2);
    }

    [Fact]
    public void TrimSilenceAtStart_ConsolidatesConductorKeySig()
    {
        var conductor = ConductorTrack(
            KeySigEvent(0, 0, 0),
            KeySigEvent(480, -1, 0));
        var noteTrack = NoteTrack(Note(60, 960, 240));
        var file = CreateEditableFile(conductor, noteTrack);

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();
        var conductorTrack = file.Tracks[0];
        var keySigs = conductorTrack.Chunk.GetTimedEvents()
            .Where(te => te.Event is KeySignatureEvent)
            .ToList();
        keySigs.Count.ShouldBe(1);
        keySigs[0].Time.ShouldBe(0);
        var ks = (KeySignatureEvent)keySigs[0].Event;
        ((int)ks.Key).ShouldBe(-1);
    }

    [Fact]
    public void TrimSilenceAtStart_KeepsProgramChangesAndControllersAtTick0()
    {
        var chunk = new TrackChunk();
        chunk.Events.Add(new SequenceTrackNameEvent("Track"));
        chunk.Events.Add(new ProgramChangeEvent((SevenBitNumber)12) { DeltaTime = 0 });
        chunk.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { DeltaTime = 240 });
        chunk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 720 });
        chunk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 });

        var file = CreateEditableFile(chunk);
        var track = file.Tracks[0];
        track.IsConductorTrack.ShouldBeFalse();
        var notes = track.Chunk.GetNotes().ToList();
        notes.Count.ShouldBe(1);
        notes[0].Time.ShouldBe(960);

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();
        var timedEvents = file.Tracks[0].Chunk.GetTimedEvents()
            .OrderBy(te => te.Time)
            .ToList();

        // Program change was at 0 -> stays at 0
        timedEvents.ShouldContain(te => te.Event is ProgramChangeEvent && te.Time == 0);

        // Controller was at 240 -> now at 0 (clamped)
        timedEvents.ShouldContain(te => te.Event is ControlChangeEvent && te.Time == 0);

        // First note was at 960 -> now at 0
        var noteOns = timedEvents.Where(te => te.Event is NoteOnEvent).ToList();
        noteOns[0].Time.ShouldBe(0);
    }

    [Fact]
    public void TrimSilenceAtStart_NoOpWhenNoGap()
    {
        var file = CreateEditableFile(NoteTrack(Note(60, 0, 240)));

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeFalse();
    }

    [Fact]
    public void TrimSilenceAtStart_NoOpWhenNoNotes()
    {
        var file = CreateEditableFile(
            ConductorTrack(TempoEvent(0, 120)),
            new TrackChunk());

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeFalse();
    }

    // =================================================================
    // TrimSilenceAtStart — fixture-based
    // =================================================================

    [Fact]
    public void TrimSilenceAtStart_TrimsAnotherWorld()
    {
        var path = FindDataFile("another-world.mid");
        var midi = MidiFile.Read(path);
        var file = new EditableMidiFile(midi);

        var changed = FileDocumentCommandHelpers.TrimSilenceAtStart(file);

        changed.ShouldBeTrue();

        // At least one non-conductor track with notes should have first note at 0
        var firstNoteTimes = file.Tracks
            .Where(t => !t.IsConductorTrack)
            .Select(t => t.Chunk.GetNotes().FirstOrDefault()?.Time)
            .Where(t => t.HasValue)
            .Select(t => t.Value)
            .ToList();
        firstNoteTimes.Count.ShouldBeGreaterThan(0);
        firstNoteTimes.Min().ShouldBe(0, "earliest track first note should be at tick 0 after trim");
    }

    // =================================================================
    // ApplySanitize end-to-end with Trim
    // =================================================================

    [Fact]
    public void ApplySanitize_WithTrim_TrimsSilence()
    {
        var conductor = ConductorTrack(TempoEvent(0, 120));
        var noteTrack = NoteTrack(Note(60, 480, 240));
        var file = CreateEditableFile(conductor, noteTrack);

        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes().Count().ShouldBe(1);

        var changed = FileDocumentCommandHelpers.ApplySanitize(file, new SanitizingSettings { Trim = true });
        changed.ShouldBeTrue();

        file.Tracks.Count.ShouldBe(2, "conductor track preserved");
        file.Tracks[0].IsConductorTrack.ShouldBeTrue("first track still conductor");
        file.Tracks[1].Chunk.GetNotes().Count().ShouldBe(1, "note preserved");
        file.Tracks[1].Chunk.GetNotes().First().Time.ShouldBe(0, "trimmed to start");
    }

    [Fact]
    public void ApplySanitize_WithTrim_ChangedIsTrue()
    {
        var file = CreateEditableFile(NoteTrack(Note(60, 480, 240)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SanitizeFileCommand(),
            EditorCommandContext.Create(session),
            new SanitizeFileOptions(new SanitizingSettings
            {
                Trim = true,
            }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
    }

    [Fact]
    public void ApplySanitize_WithTrimAndDedup_WorksTogether()
    {
        var conductor = ConductorTrack(
            TempoEvent(0, 120),
            TempoEvent(0, 120));
        var noteTrack = NoteTrack(
            Note(60, 480, 240),
            Note(60, 480, 240));
        var file = CreateEditableFile(conductor, noteTrack);

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings
            {
                Trim = true,
                RemoveDuplicatedNotes = true,
                RemoveDuplicatedSetTempoEvents = true,
            });

        changed.ShouldBeTrue();

        // Notes deduplicated: 2 -> 1
        file.Tracks[1].Chunk.GetNotes().Count().ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().First().Time.ShouldBe(0);

        // Tempo deduplicated: 2 at same tick -> 1 at tick 0
        var tempos = file.Tracks[0].Chunk.GetTimedEvents()
            .Where(te => te.Event is SetTempoEvent)
            .ToList();
        tempos.Count.ShouldBe(1);
        tempos[0].Time.ShouldBe(0);
    }

    [Fact]
    public void ApplySanitize_WithTrim_ChangedFalseWhenNoSilence()
    {
        var file = CreateEditableFile(NoteTrack(Note(60, 0, 240)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SanitizeFileCommand(),
            EditorCommandContext.Create(session),
            new SanitizeFileOptions(new SanitizingSettings
            {
                Trim = true,
                RemoveDuplicatedNotes = true,
            }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(0);
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
    }

    // =================================================================
    // Helpers
    // =================================================================

    private static string FindDataFile(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "data", name);
            if (File.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir, "MidiBard.Tests", "Data", name);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Could not find test data file: {name}");
    }
}
