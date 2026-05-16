using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitTracksByLengthRangeCommandTests
{
    [Fact]
    public void Execute_CreatesInRangeAndOutOfRangeTracksPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)2 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)2 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)2 }, 20),
            Note(60, 0, 119, channel: 2),
            Note(62, 200, 120, channel: 2),
            Note(64, 500, 240, channel: 2),
            Note(65, 900, 241, channel: 2)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByLengthRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByLengthRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 120, MaximumLengthTicks: 240)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.InRangeTracks.ShouldBe(1);
        result.Result.Value.OutOfRangeTracks.ShouldBe(1);
        result.Result.Value.InRangeNotes.ShouldBe(2);
        result.Result.Value.OutOfRangeNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range 120 - 240)",
            "Piano (Out of Range 120 - 240)",
        });
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length)
            .ShouldBe(new long[] { 119, 120, 240, 241 });
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length)
            .ShouldBe(new long[] { 120, 240 });
        file.Tracks[2].Chunk.GetNotes().Select(note => note.Length)
            .ShouldBe(new long[] { 119, 241 });
        foreach (var splitTrack in file.Tracks.Skip(1))
        {
            splitTrack.Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)2);
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
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length)
            .ShouldBe(new long[] { 119, 120, 240, 241 });
    }

    [Fact]
    public void Execute_SkipsEmptyPartitions()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(72, 120, 240)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByLengthRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByLengthRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 120, MaximumLengthTicks: 240)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.InRangeTracks.ShouldBe(1);
        result.Result.Value.OutOfRangeTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range 120 - 240)",
        });
    }

    [Fact]
    public void Execute_NormalizesInvertedAndClampedRange()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 240, 480)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByLengthRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByLengthRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 480, MaximumLengthTicks: -10)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.InRangeNotes.ShouldBe(2);
        result.Result.Value.OutOfRangeNotes.ShouldBe(0);
        file.Tracks[1].Name.ShouldBe("Piano (In Range 0 - 480)");
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length)
            .ShouldBe(new long[] { 120, 480 });
    }

    [Fact]
    public void Execute_InsertsGeneratedTracksAfterEachSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(
            CreateTrack("Short",
                Note(60, 0, 119),
                Note(62, 120, 120)),
            CreateTrack("Long",
                Note(64, 0, 240),
                Note(65, 240, 241)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByLengthRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByLengthRangeCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 120, MaximumLengthTicks: 240)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(4);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Short",
            "Short (In Range 120 - 240)",
            "Short (Out of Range 120 - 240)",
            "Long",
            "Long (In Range 120 - 240)",
            "Long (Out of Range 120 - 240)",
        });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
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
            new SplitTracksByLengthRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByLengthRangeCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitLengthRangeOptions(MinimumLengthTicks: 120, MaximumLengthTicks: 240)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
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
