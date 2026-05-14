using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class DifferenceTracksCommandTests
{
    [Fact]
    public void Execute_CreatesDiffAndRestTracksFromTargetPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Target",
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)7 }, 0),
                Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)7 }, 10),
                Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)7 }, 20),
                Note(60, 0, 100, channel: 7),
                Note(62, 200, 100, channel: 7),
                Note(64, 400, 100, channel: 7)),
            CreateTrack("Compare",
                Note(72, 50, 100),
                Note(74, 500, 100)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DifferenceTracksCommand(),
            EditorCommandContext.Create(session),
            new DifferenceTracksCommandOptions(new[] { 0, 1 }, TargetTrackIndex: 0));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DiffNotes.ShouldBe(2);
        result.Result.Value.RestNotes.ShouldBe(1);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Target",
            "Target (Diff)",
            "Target (Diff Rest)",
            "Compare",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62, 64 });
        file.Tracks[2].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
        foreach (var splitTrack in file.Tracks.Skip(1).Take(2))
        {
            splitTrack.Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)7);
            splitTrack.Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
            splitTrack.Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        }

        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Name.ShouldBe("Target");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 62, 64 });
    }

    [Fact]
    public void Execute_TouchingNoteEdgesDoNotOverlap()
    {
        var file = CreateEditableFile(
            CreateTrack("Target", Note(60, 0, 100)),
            CreateTrack("Compare", Note(72, 100, 100)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DifferenceTracksCommand(),
            EditorCommandContext.Create(session),
            new DifferenceTracksCommandOptions(new[] { 0, 1 }, TargetTrackIndex: 0));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.DiffNotes.ShouldBe(1);
        result.Result.Value.RestNotes.ShouldBe(0);
        file.Tracks[1].Name.ShouldBe("Target (Diff)");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    [Fact]
    public void Execute_TargetCanBeLaterThanComparisonTrack()
    {
        var file = CreateEditableFile(
            CreateTrack("Compare", Note(72, 50, 100)),
            CreateTrack("Target", Note(60, 0, 100), Note(62, 240, 100)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DifferenceTracksCommand(),
            EditorCommandContext.Create(session),
            new DifferenceTracksCommandOptions(new[] { 0, 1 }, TargetTrackIndex: 1));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DiffNotes.ShouldBe(1);
        result.Result.Value.RestNotes.ShouldBe(1);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Compare",
            "Target",
            "Target (Diff)",
            "Target (Diff Rest)",
        });
        file.Tracks[2].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)62);
        file.Tracks[3].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    [Fact]
    public void Execute_AllNotesOverlapCreatesOnlyRestTrack()
    {
        var file = CreateEditableFile(
            CreateTrack("Target", Note(60, 0, 100), Note(62, 200, 100)),
            CreateTrack("Compare", Note(72, 50, 100), Note(74, 250, 100)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DifferenceTracksCommand(),
            EditorCommandContext.Create(session),
            new DifferenceTracksCommandOptions(new[] { 0, 1 }, TargetTrackIndex: 0));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.DiffNotes.ShouldBe(0);
        result.Result.Value.RestNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Target",
            "Target (Diff Rest)",
            "Compare",
        });
    }

    [Fact]
    public void Execute_InvalidTargetDoesNotDirtyFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Target", Note(60, 0, 100)),
            CreateTrack("Compare", Note(72, 50, 100)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new DifferenceTracksCommand(),
            EditorCommandContext.Create(session),
            new DifferenceTracksCommandOptions(new[] { 1 }, TargetTrackIndex: 0));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_NoComparisonNotesDoesNotDirtyFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Target", Note(60, 0, 100)),
            CreateTrack("Compare", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new DifferenceTracksCommand(),
            EditorCommandContext.Create(session),
            new DifferenceTracksCommandOptions(new[] { 0, 1 }, TargetTrackIndex: 0));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
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
