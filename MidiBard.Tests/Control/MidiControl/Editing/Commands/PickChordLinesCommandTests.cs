using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class PickChordLinesCommandTests
{
    [Fact]
    public void Execute_MaxOneCreatesOutputTrackReturnsOutputIndexPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)5 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)5 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)5 }, 20),
            Note(60, 0, 120, channel: 5),
            Note(64, 0, 120, channel: 5),
            Note(67, 0, 120, channel: 5),
            Note(72, 240, 120, channel: 5)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions(MaxSimultaneousNotes: 1)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(2);
        result.Result.Value.OutputTrackIndices.ShouldBe(new[] { 1 });
        file.Tracks[1].Name.ShouldBe("Piano (Auto Edited Max 1)");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67, 72 });
        file.Tracks[1].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)5);
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
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 64, 67, 72 });
    }

    [Fact]
    public void Execute_MaxTwoPicksSecondChordLine()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions(MaxSimultaneousNotes: 2)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.PickedParts.ShouldBe(3);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 64, 67, 72 });
    }

    [Fact]
    public void Execute_MaxThreePicksTopThreeChordLines()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 0, 120),
            Note(76, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions(MaxSimultaneousNotes: 3)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.PickedParts.ShouldBe(4);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 64, 67, 72, 76 });
    }

    [Fact]
    public void Execute_OddStrategyMaxTwoPicksThirdChordLineForThreeNoteChord()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions(
                    MaxSimultaneousNotes: 2,
                    PickStrategy: MidiForgeChordPickStrategy.OddChords)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 67, 72 });
    }

    [Fact]
    public void Execute_ReplaceOriginalReloadsSelectionAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions(
                    MaxSimultaneousNotes: 1,
                    CreateNewTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        result.Result.Value.OutputTrackIndices.ShouldBe(new[] { 0 });
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Auto Edited Max 1)");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67, 72 });
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 64, 67, 72 });
    }

    [Fact]
    public void Execute_CanPreserveTrackNameWhenRenameTracksFalse()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions(RenameTracks: false)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Piano" });
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
            new PickChordLinesCommand(),
            EditorCommandContext.Create(session),
            new PickChordLinesCommandOptions(
                new[] { 0 },
                new MidiForgePickChordLinesOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(0);
        result.Result.Value.OutputTrackIndices.ShouldBeEmpty();
        file.Tracks.Count.ShouldBe(1);
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
