using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class TrimOverlappedSustainedNotesCommandTests
{
    [Fact]
    public void Execute_TrimsToNextOverlappingStartPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)4 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)4 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)4 }, 20),
            Note(60, 0, 480, channel: 4),
            Note(64, 240, 120, channel: 4),
            Note(67, 240, 120, channel: 4)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TrimOverlappedSustainedNotesCommand(),
            EditorCommandContext.Create(session),
            new TrimOverlappedSustainedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(1);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Piano (Trimmed)" });
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 240, 120, 120 });
        file.Tracks[1].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)4);
        file.Tracks[1].Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
        file.Tracks[1].Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 480, 120, 120 });
    }

    [Fact]
    public void Execute_UsesEarliestLaterOverlappingStart()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 600),
            Note(64, 300, 120),
            Note(67, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TrimOverlappedSustainedNotesCommand(),
            EditorCommandContext.Create(session),
            new TrimOverlappedSustainedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ChangedNotes.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 120, 120 });
    }

    [Fact]
    public void Execute_IgnoresSameStartChordsWithoutDirtyingFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 480),
            Note(64, 0, 480)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new TrimOverlappedSustainedNotesCommand(),
            EditorCommandContext.Create(session),
            new TrimOverlappedSustainedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ChangedNotes.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_TouchingEdgesDoNotTrimOrDirtyFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 240),
            Note(64, 240, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new TrimOverlappedSustainedNotesCommand(),
            EditorCommandContext.Create(session),
            new TrimOverlappedSustainedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
    }

    [Fact]
    public void Execute_RefreshesTrackIndexesWhenTrimmingMultipleTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Note(60, 0, 480),
                Note(64, 240, 120)),
            CreateTrack("Flute",
                Note(72, 0, 480),
                Note(76, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TrimOverlappedSustainedNotesCommand(),
            EditorCommandContext.Create(session),
            new TrimOverlappedSustainedNotesCommandOptions(new[] { 1, 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (Trimmed)",
            "Flute",
            "Flute (Trimmed)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 240, 120 });
        file.Tracks[3].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 120 });
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
