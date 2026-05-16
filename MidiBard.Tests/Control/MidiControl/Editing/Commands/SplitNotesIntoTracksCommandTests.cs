using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitNotesIntoTracksCommandTests
{
    [Fact]
    public void Execute_DistributesNotesByEveryNAmountPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)8 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)8 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)8 }, 20),
            Note(60, 0, 120, channel: 8),
            Note(62, 120, 120, channel: 8),
            Note(64, 240, 120, channel: 8),
            Note(65, 360, 120, channel: 8),
            Note(67, 480, 120, channel: 8)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitNotesIntoTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitNotesIntoTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitNotesIntoTracksOptions(NumberOfTracks: 2, EveryNotesAmount: 2)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DistributedNotes.ShouldBe(5);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (Group 1)",
            "Piano (Group 2)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 62, 67 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 64, 65 });
        foreach (var splitTrack in file.Tracks.Skip(1))
        {
            splitTrack.Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)8);
            splitTrack.Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
            splitTrack.Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        }

        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 62, 64, 65, 67 });
    }

    [Fact]
    public void Execute_SkipsEmptyGeneratedGroups()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitNotesIntoTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitNotesIntoTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitNotesIntoTracksOptions(NumberOfTracks: 4, EveryNotesAmount: 1)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (Group 1)",
            "Piano (Group 2)",
        });
    }

    [Fact]
    public void Execute_ClampsTrackCountAndEveryNotesAmount()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitNotesIntoTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitNotesIntoTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitNotesIntoTracksOptions(NumberOfTracks: 0, EveryNotesAmount: 0)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.DistributedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (Group 1)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 62 });
    }

    [Fact]
    public void Execute_SkipsConductorAndEmptyTracksWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SplitNotesIntoTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitNotesIntoTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitNotesIntoTracksOptions(NumberOfTracks: 2, EveryNotesAmount: 1)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_InsertsGeneratedTracksAfterEachSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(60, 0, 120), Note(62, 120, 120)),
            CreateTrack("Flute", Note(72, 0, 120), Note(74, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitNotesIntoTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitNotesIntoTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitNotesIntoTracksOptions(NumberOfTracks: 2, EveryNotesAmount: 1)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(4);
        result.Result.Value.DistributedNotes.ShouldBe(4);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (Group 1)",
            "Piano (Group 2)",
            "Flute",
            "Flute (Group 1)",
            "Flute (Group 2)",
        });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
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
