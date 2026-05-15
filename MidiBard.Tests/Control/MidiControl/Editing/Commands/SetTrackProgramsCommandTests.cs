using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SetTrackProgramsCommandTests
{
    [Fact]
    public void Execute_AddsProgramChangeRenamesTrackAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Old", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTrackProgramsCommand(),
            EditorCommandContext.Create(session),
            new SetTrackProgramsCommandOptions(
                new[] { 0 },
                new MidiForgeSetTrackProgramOptions(
                    ProgramNumber: 40,
                    RenameTracks: true,
                    RenameMode: MidiForgeTrackNameFillMode.Midi)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.ChangedTracks.ShouldBe(1);
        result.Result.Value.AddedProgramChanges.ShouldBe(1);
        result.Result.Value.UpdatedProgramChanges.ShouldBe(0);
        result.Result.Value.RenamedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Violin");
        var programChange = file.Tracks[0].Chunk.GetTimedEvents()
            .Single(timedEvent => timedEvent.Event is ProgramChangeEvent);
        programChange.Time.ShouldBe(0);
        ((ProgramChangeEvent)programChange.Event).ProgramNumber.ShouldBe((SevenBitNumber)40);
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Old");
        file.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Execute_ReplacesAllProgramChangesWhenRequested()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)4) { Channel = (FourBitNumber)0 }, 480),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTrackProgramsCommand(),
            EditorCommandContext.Create(session),
            new SetTrackProgramsCommandOptions(
                new[] { 0 },
                new MidiForgeSetTrackProgramOptions(
                    ProgramNumber: 24,
                    ReplaceAllProgramChanges: true,
                    RenameTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.UpdatedProgramChanges.ShouldBe(2);
        file.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
            .Select(timedEvent => (int)(byte)((ProgramChangeEvent)timedEvent.Event).ProgramNumber)
            .ShouldBe(new[] { 24, 24 });
    }

    [Fact]
    public void Execute_CanUpdateOnlyFirstProgramChange()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)4) { Channel = (FourBitNumber)0 }, 480),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTrackProgramsCommand(),
            EditorCommandContext.Create(session),
            new SetTrackProgramsCommandOptions(
                new[] { 0 },
                new MidiForgeSetTrackProgramOptions(
                    ProgramNumber: 24,
                    ReplaceAllProgramChanges: false,
                    RenameTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.UpdatedProgramChanges.ShouldBe(1);
        file.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
            .Select(timedEvent => (int)(byte)((ProgramChangeEvent)timedEvent.Event).ProgramNumber)
            .ShouldBe(new[] { 24, 4 });
    }

    [Fact]
    public void Execute_CanRenameTrackWhenProgramIsAlreadySet()
    {
        var file = CreateEditableFile(CreateTrack("Old",
            Timed(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)0 }, 0),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTrackProgramsCommand(),
            EditorCommandContext.Create(session),
            new SetTrackProgramsCommandOptions(
                new[] { 0 },
                new MidiForgeSetTrackProgramOptions(
                    ProgramNumber: 40,
                    ReplaceAllProgramChanges: true,
                    RenameTracks: true,
                    RenameMode: MidiForgeTrackNameFillMode.Midi)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.ChangedTracks.ShouldBe(1);
        result.Result.Value.AddedProgramChanges.ShouldBe(0);
        result.Result.Value.UpdatedProgramChanges.ShouldBe(0);
        result.Result.Value.RenamedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Violin");
        file.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
            .Select(timedEvent => (int)(byte)((ProgramChangeEvent)timedEvent.Event).ProgramNumber)
            .ShouldBe(new[] { 40 });
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Old");
        file.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
            .Select(timedEvent => (int)(byte)((ProgramChangeEvent)timedEvent.Event).ProgramNumber)
            .ShouldBe(new[] { 40 });
    }

    [Fact]
    public void Execute_SkipsConductorAndDoesNotDirtyWhenUnchanged()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano",
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
                Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SetTrackProgramsCommand(),
            EditorCommandContext.Create(session),
            new SetTrackProgramsCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSetTrackProgramOptions(
                    ProgramNumber: 0,
                    RenameTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.ChangedTracks.ShouldBe(0);
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
