using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ChangeTrackNoteLengthsCommandTests
{
    [Fact]
    public void Execute_CreateNewTrackChangesOnlyMatchingLengthsPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)3 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)3 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)3 }, 20),
            Note(60, 0, 120, channel: 3),
            Note(62, 240, 240, channel: 3),
            Note(64, 520, 480, channel: 3)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ChangeTrackNoteLengthsCommand(),
            EditorCommandContext.Create(session),
            new ChangeTrackNoteLengthsCommandOptions(
                new[] { 0 },
                new MidiForgeChangeNoteLengthOptions(
                    MinimumLengthTicks: 100,
                    MaximumLengthTicks: 240,
                    NewLengthTicks: 60)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Piano (Changed 2 notes)" });
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 240, 480 });
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 60, 60, 480 });
        file.Tracks[1].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)3);
        file.Tracks[1].Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
        file.Tracks[1].Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeFalse();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 240, 480 });
    }

    [Fact]
    public void Execute_DeleteOriginalTracksReplacesTrackInPlaceAndReloadsSelection()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 240, 240)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ChangeTrackNoteLengthsCommand(),
            EditorCommandContext.Create(session),
            new ChangeTrackNoteLengthsCommandOptions(
                new[] { 0 },
                new MidiForgeChangeNoteLengthOptions(
                    MinimumLengthTicks: 0,
                    MaximumLengthTicks: 120,
                    NewLengthTicks: 480,
                    DeleteOriginalTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Changed 1 notes)");
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 480, 240 });
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 240 });
    }

    [Fact]
    public void Execute_NormalizesRangesAndRepairsZeroLengthNotes()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 0),
            Note(62, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ChangeTrackNoteLengthsCommand(),
            EditorCommandContext.Create(session),
            new ChangeTrackNoteLengthsCommandOptions(
                new[] { 0 },
                new MidiForgeChangeNoteLengthOptions(
                    MinimumLengthTicks: 10,
                    MaximumLengthTicks: 0,
                    NewLengthTicks: 30)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ChangedNotes.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 30, 120 });
    }

    [Fact]
    public void Execute_SkipsConductorAndTracksWithoutMatchingNotesWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new ChangeTrackNoteLengthsCommand(),
            EditorCommandContext.Create(session),
            new ChangeTrackNoteLengthsCommandOptions(
                new[] { 0, 1 },
                new MidiForgeChangeNoteLengthOptions(
                    MinimumLengthTicks: 240,
                    MaximumLengthTicks: 480,
                    NewLengthTicks: 60)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.ChangedNotes.ShouldBe(0);
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
