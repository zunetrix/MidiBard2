using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SanitizeFileDiagnosticTests
{
    private static MidiFile CreateMidiFile(params TrackChunk[] chunks)
        => new(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(CreateMidiFile(chunks));

    private static TrackChunk CreateNoteTrack(string name, params Note[] notes)
    {
        var chunk = string.IsNullOrEmpty(name)
            ? new TrackChunk()
            : new TrackChunk(new SequenceTrackNameEvent(name));
        using var manager = chunk.ManageNotes();

        foreach (var note in notes)
            manager.Objects.Add(note);

        return chunk;
    }

    private static TrackChunk CreateEventTrack(params TimedEvent[] events)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();

        foreach (var ev in events)
            manager.Objects.Add(ev);

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

    private static TimedEvent TempoEvent(long tick, int bpm)
        => new(new SetTempoEvent((long)(60_000_000.0 / bpm)), tick);

    private static TimedEvent TimeSigEvent(long tick, byte num, byte den)
        => new(new TimeSignatureEvent(num, den), tick);

    [Fact]
    public void Sanitize_RemovesDuplicateNotes()
    {
        var file = CreateEditableFile(CreateNoteTrack(
            "Piano",
            Note(60, 0, 120),
            Note(60, 0, 120)));

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings { RemoveDuplicatedNotes = true });

        changed.ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
    }

    [Fact]
    public void Sanitize_RemovesEmptyTrackChunks()
    {
        var file = CreateEditableFile(
            CreateNoteTrack("Piano", Note(60, 0, 120)),
            new TrackChunk());

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings { RemoveEmptyTrackChunks = true });

        changed.ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
    }

    [Fact]
    public void Sanitize_RemovesDuplicateTempoEvents()
    {
        var file = CreateEditableFile(CreateEventTrack(
            TempoEvent(0, 120),
            TempoEvent(0, 120)));

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings { RemoveDuplicatedSetTempoEvents = true });

        changed.ShouldBeTrue();
        // Duplicates are removed; one tempo event is kept so the conductor track
        // remains detectable as a conductor track by IsConductorChunk.
        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        file.Tracks[0].Chunk.Events.OfType<SetTempoEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Sanitize_RemovesDuplicateTimeSignatureEvents()
    {
        var file = CreateEditableFile(CreateEventTrack(
            TimeSigEvent(0, 4, 4),
            TimeSigEvent(0, 4, 4)));

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings { RemoveDuplicatedTimeSignatureEvents = true });

        changed.ShouldBeTrue();
        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        file.Tracks[0].Chunk.Events.OfType<TimeSignatureEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Sanitize_NoOpWithNoIssues()
    {
        var file = CreateEditableFile(CreateNoteTrack(
            "Piano",
            Note(60, 0, 120),
            Note(64, 240, 120)));

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings
            {
                RemoveDuplicatedNotes = true,
                RemoveEmptyTrackChunks = true,
                RemoveOrphanedNoteOffEvents = true,
                RemoveDuplicatedSetTempoEvents = true,
                RemoveDuplicatedTimeSignatureEvents = true,
            });

        changed.ShouldBeFalse();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(2);
    }

    [Fact]
    public void Sanitize_WorksThroughCommandExecutor()
    {
        var file = CreateEditableFile(CreateNoteTrack(
            "Piano",
            Note(60, 0, 120),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SanitizeFileCommand(),
            EditorCommandContext.Create(session),
            new SanitizeFileOptions(new SanitizingSettings
            {
                RemoveDuplicatedNotes = true,
            }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
    }

    [Fact]
    public void Sanitize_WorksAfterFileRoundTrip()
    {
        var sourceFile = CreateMidiFile(CreateNoteTrack(
            "Piano",
            Note(60, 0, 120),
            Note(60, 0, 120)));

        var tempPath = Path.GetTempFileName() + ".mid";
        try
        {
            sourceFile.Write(tempPath);
            var loaded = MidiFile.Read(tempPath);
            var loadedFile = new EditableMidiFile(loaded);

            var changed = FileDocumentCommandHelpers.ApplySanitize(
                loadedFile,
                new SanitizingSettings { RemoveDuplicatedNotes = true });

            changed.ShouldBeTrue();
            loadedFile.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
            loadedFile.IsDirty.ShouldBeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void Sanitize_ProtectsEmptyConductorTrack()
    {
        var file = CreateEditableFile(
            CreateNoteTrack("Piano", Note(60, 0, 120)));

        var conductor = new TrackChunk();
        var conductorTrack = new EditableTrack(conductor, 0);
        conductorTrack.MarkAsConductorTrack();
        file.Tracks.Insert(0, conductorTrack);

        var countBefore = file.Tracks.Count;

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings
            {
                RemoveEmptyTrackChunks = true,
                RemoveDuplicatedNotes = true,
            });

        changed.ShouldBeTrue();
        file.Tracks.Count.ShouldBe(countBefore);
        file.Tracks.ShouldContain(t => t.IsConductorTrack);
    }
}
