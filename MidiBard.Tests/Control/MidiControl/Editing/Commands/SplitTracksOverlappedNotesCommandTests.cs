using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitTracksOverlappedNotesCommandTests
{
    [Fact]
    public void Execute_CreatesNoOverlapAndOverlapTracksPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)3 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)3 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)3 }, 20),
            Note(60, 0, 120, channel: 3),
            Note(60, 0, 240, channel: 3),
            Note(62, 240, 120, channel: 3),
            Note(64, 480, 120, channel: 3)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksOverlappedNotesCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksOverlappedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(3);
        result.Result.Value.OverlapGroups.ShouldBe(1);
        result.Result.Value.OverlappedNotes.ShouldBe(2);
        result.Result.Value.NonOverlappedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano no overlap",
            "Piano overlap (1)",
            "Piano overlap (2)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62, 64 });
        file.Tracks[2].Chunk.GetNotes().Single().Length.ShouldBe(120);
        file.Tracks[3].Chunk.GetNotes().Single().Length.ShouldBe(240);
        foreach (var splitTrack in file.Tracks.Skip(1))
        {
            splitTrack.Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)3);
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
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 60, 62, 64 });
    }

    [Fact]
    public void Execute_NoDuplicateStartPitchDoesNotDirtyFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(60, 120, 120),
            Note(64, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksOverlappedNotesCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksOverlappedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.OverlapGroups.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_SkipsTracksWithoutNotesWithoutDirtyingFile()
    {
        var file = CreateEditableFile(CreateTrack(
            "Empty",
            Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksOverlappedNotesCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksOverlappedNotesCommandOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_RefreshesTrackIndexesAfterAppendingSplitTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Note(60, 0, 120),
                Note(60, 0, 240)),
            CreateTrack("Flute",
                Note(72, 0, 120),
                Note(72, 0, 240)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksOverlappedNotesCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksOverlappedNotesCommandOptions(new[] { 1, 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(4);
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Flute",
            "Piano overlap (1)",
            "Piano overlap (2)",
            "Flute overlap (1)",
            "Flute overlap (2)",
        });
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
