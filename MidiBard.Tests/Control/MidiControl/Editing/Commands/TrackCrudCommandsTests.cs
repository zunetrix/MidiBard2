using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class TrackCrudCommandsTests
{
    [Fact]
    public void CloneTracks_ClonesPerformanceTracksAndSupportsUndo()
    {
        var file = CreateEditableFile(
            ConductorTrack(),
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("Flute", Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new CloneTracksCommand(),
            EditorCommandContext.Create(session),
            new CloneTracksOptions(new[] { 0, 1, 2 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedTracks.ShouldBe(2);
        result.Result.Value.CreatedTrackIndices.ShouldBe(new[] { 2, 4 });
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { string.Empty, "Piano", "Piano", "Flute", "Flute" });
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeFalse();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { string.Empty, "Piano", "Flute" });
    }

    [Fact]
    public void DeleteTracks_RemovesPerformanceTracksAndSupportsUndo()
    {
        var file = CreateEditableFile(
            ConductorTrack(),
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("Flute", Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DeleteTracksCommand(),
            EditorCommandContext.Create(session),
            new DeleteTracksOptions(new[] { 0, 1 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedTracks.ShouldBe(1);
        result.Result.Value.RemovedTrackIndices.ShouldBe(new[] { 1 });
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { string.Empty, "Flute" });
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { string.Empty, "Piano", "Flute" });
    }

    [Fact]
    public void DeleteTracks_RejectsWhenNoPerformanceTracksAreSelected()
    {
        var file = CreateEditableFile(ConductorTrack());
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DeleteTracksCommand(),
            EditorCommandContext.Create(session),
            new DeleteTracksOptions(new[] { 0 }));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Choose at least one performance track.");
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void ReorderTrack_MovesTrackAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("Flute", Note(72, 120, 120)),
            CreateTrack("Cello", Note(48, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ReorderTrackCommand(),
            EditorCommandContext.Create(session),
            new ReorderTrackOptions(0, 2));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { "Flute", "Cello", "Piano" });
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { "Piano", "Flute", "Cello" });
    }

    [Fact]
    public void RenameTrack_RenamesTrackAndDoesNotDirtyWhenNameIsUnchanged()
    {
        var file = CreateEditableFile(CreateTrack("Old", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var executor = new EditorCommandExecutor();

        var unchanged = executor.Execute(
            new RenameTrackCommand(),
            EditorCommandContext.Create(session),
            new RenameTrackOptions(0, "Old"));

        unchanged.Succeeded.ShouldBeTrue();
        unchanged.Changed.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(0);

        var changed = executor.Execute(
            new RenameTrackCommand(),
            EditorCommandContext.Create(session),
            new RenameTrackOptions(0, "New"));

        changed.Succeeded.ShouldBeTrue();
        changed.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("New");
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Old");
    }

    [Fact]
    public void SetTrackChannel_UpdatesChannelEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120, channel: 0)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SetTrackChannelCommand(),
            EditorCommandContext.Create(session),
            new SetTrackChannelOptions(0, 5));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Chunk.Events
            .OfType<ChannelEvent>()
            .Select(midiEvent => (int)(byte)midiEvent.Channel)
            .Distinct()
            .ShouldBe(new[] { 5 });
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.Events
            .OfType<ChannelEvent>()
            .Select(midiEvent => (int)(byte)midiEvent.Channel)
            .Distinct()
            .ShouldBe(new[] { 0 });
    }

    [Fact]
    public void SplitTrackByChannel_ReplacesTrackWithChannelTracksAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(
            string.Empty,
            Note(60, 0, 120, channel: 0),
            Note(64, 120, 120, channel: 1)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTrackByChannelCommand(),
            EditorCommandContext.Create(session),
            new SplitTrackByChannelOptions(0));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedTracks.ShouldBe(1);
        result.Result.Value.CreatedTrackIndices.ShouldBe(new[] { 0, 1 });
        result.Result.Value.RemovedTrackIndices.ShouldBe(new[] { 0 });
        file.Tracks.Count.ShouldBe(2);
        file.Tracks.Select(track => track.Channel).ShouldBe(new[] { 0, 1 });
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].HasMultipleChannels.ShouldBeTrue();
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk ConductorTrack()
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();
        manager.Objects.Add(new TimedEvent(new SetTempoEvent(500000), 0));
        return chunk;
    }

    private static TrackChunk CreateTrack(string name, params Note[] notes)
    {
        var chunk = string.IsNullOrEmpty(name)
            ? new TrackChunk()
            : new TrackChunk(new SequenceTrackNameEvent(name));
        using var manager = chunk.ManageTimedEvents();

        foreach (var note in notes)
        {
            manager.Objects.Add(new TimedEvent(
                new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                note.Time));
            manager.Objects.Add(new TimedEvent(
                new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                note.EndTime));
        }

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
}
