using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class GeneratePitchBendNotesCommandTests
{
    [Fact]
    public void Execute_CreatesDerivedTrackWithSegmentedNotesRemovesPitchBendsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Lead",
            Timed(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)0 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)0 }, 10),
            Timed(new PitchBendEvent(12288) { Channel = (FourBitNumber)0 }, 120),
            Timed(new PitchBendEvent(8192) { Channel = (FourBitNumber)0 }, 360),
            Note(60, 0, 480)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new GeneratePitchBendNotesCommand(),
            EditorCommandContext.Create(session),
            new GeneratePitchBendNotesCommandOptions(
                new[] { 0 },
                new MidiForgeGeneratePitchBendNotesOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.GeneratedNotes.ShouldBe(3);
        result.Result.Value.SkippedTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Lead", "Lead (Pitch Bend Notes)" });
        file.Tracks[0].Chunk.Events.OfType<PitchBendEvent>().Count().ShouldBe(2);
        file.Tracks[1].Chunk.Events.OfType<PitchBendEvent>().ShouldBeEmpty();
        file.Tracks[1].Chunk.Events.OfType<ProgramChangeEvent>().Single().ProgramNumber.ShouldBe((SevenBitNumber)40);
        file.Tracks[1].Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time, note.Length))
            .ShouldBe(new[]
            {
                (60, 0L, 120L),
                (61, 120L, 240L),
                (60, 360L, 120L),
            });
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeFalse();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Lead");
        file.Tracks[0].Chunk.Events.OfType<PitchBendEvent>().Count().ShouldBe(2);
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    [Fact]
    public void Execute_DeleteOriginalTracksReplacesTrackInPlaceAndReloadsSelection()
    {
        var file = CreateEditableFile(CreateTrack("Lead",
            Timed(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 }, 0),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new GeneratePitchBendNotesCommand(),
            EditorCommandContext.Create(session),
            new GeneratePitchBendNotesCommandOptions(
                new[] { 0 },
                new MidiForgeGeneratePitchBendNotesOptions(DeleteOriginalTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Lead (Pitch Bend Notes)");
        file.Tracks[0].Chunk.Events.OfType<PitchBendEvent>().ShouldBeEmpty();
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)62);
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Lead");
        file.Tracks[0].Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)16383);
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    [Fact]
    public void Execute_UsesLastPitchBendBeforeNoteStartAndFiltersByNoteChannel()
    {
        var file = CreateEditableFile(CreateTrack("Lead",
            Timed(new PitchBendEvent(0) { Channel = (FourBitNumber)0 }, 0),
            Timed(new PitchBendEvent(16383) { Channel = (FourBitNumber)1 }, 50),
            Note(60, 100, 200, channel: 0),
            Note(72, 100, 200, channel: 1)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new GeneratePitchBendNotesCommand(),
            EditorCommandContext.Create(session),
            new GeneratePitchBendNotesCommandOptions(
                new[] { 0 },
                new MidiForgeGeneratePitchBendNotesOptions()));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes()
            .OrderBy(note => (byte)note.NoteNumber)
            .Select(note => ((int)(byte)note.NoteNumber, (int)(byte)note.Channel, note.Time, note.Length))
            .ShouldBe(new[]
            {
                (58, 0, 100L, 200L),
                (74, 1, 100L, 200L),
            });
    }

    [Fact]
    public void Execute_DeduplicatesConsecutiveSameSemitoneBendsAndIgnoresZeroLengthEndSegment()
    {
        var file = CreateEditableFile(CreateTrack("Lead",
            Timed(new PitchBendEvent(12288) { Channel = (FourBitNumber)0 }, 120),
            Timed(new PitchBendEvent(13000) { Channel = (FourBitNumber)0 }, 240),
            Timed(new PitchBendEvent(8192) { Channel = (FourBitNumber)0 }, 360),
            Timed(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 }, 480),
            Note(60, 0, 480)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new GeneratePitchBendNotesCommand(),
            EditorCommandContext.Create(session),
            new GeneratePitchBendNotesCommandOptions(
                new[] { 0 },
                new MidiForgeGeneratePitchBendNotesOptions()));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time, note.Length))
            .ShouldBe(new[]
            {
                (60, 0L, 120L),
                (61, 120L, 240L),
                (60, 360L, 120L),
            });
    }

    [Fact]
    public void Execute_SkipsConductorAndTracksWithoutPitchBendsOrNotesWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Lead", Note(60, 0, 120)),
            CreateTrack("Bends Only", Timed(new PitchBendEvent(12288), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new GeneratePitchBendNotesCommand(),
            EditorCommandContext.Create(session),
            new GeneratePitchBendNotesCommandOptions(
                new[] { 0, 1, 2 },
                new MidiForgeGeneratePitchBendNotesOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.GeneratedNotes.ShouldBe(0);
        result.Result.Value.SkippedTracks.ShouldBe(2);
        file.Tracks.Count.ShouldBe(3);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_InsertsGeneratedTracksAfterEachSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(
            CreateTrack("Lead A",
                Timed(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 }, 0),
                Note(60, 0, 120)),
            CreateTrack("Lead B",
                Timed(new PitchBendEvent(0) { Channel = (FourBitNumber)0 }, 0),
                Note(72, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new GeneratePitchBendNotesCommand(),
            EditorCommandContext.Create(session),
            new GeneratePitchBendNotesCommandOptions(
                new[] { 0, 1 },
                new MidiForgeGeneratePitchBendNotesOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.GeneratedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Lead A",
            "Lead A (Pitch Bend Notes)",
            "Lead B",
            "Lead B (Pitch Bend Notes)",
        });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)62);
        file.Tracks[3].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)70);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string name, params object[] objects)
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent(name));
        using var manager = chunk.ManageTimedEvents();

        foreach (var item in objects)
        {
            switch (item)
            {
                case TimedEvent timedEvent:
                    manager.Objects.Add(timedEvent);
                    break;
                case Note note:
                    manager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                        note.Time));
                    manager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                        note.EndTime));
                    break;
            }
        }

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);

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
