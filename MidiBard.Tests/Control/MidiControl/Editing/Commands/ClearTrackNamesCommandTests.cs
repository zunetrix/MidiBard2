using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ClearTrackNamesCommandTests
{
    [Fact]
    public void Execute_ClearsSelectedTrackNamesThroughExecutorAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("BassDrum", Note(48, 120, 120, channel: 9)),
            CreateTrack("Custom", Note(64, 240, 120)));
        var session = new MidiEditorSessionState { File = file };
        var context = EditorCommandContext.Create(session);

        var result = new EditorCommandExecutor().Execute(
            new ClearTrackNamesCommand(),
            context,
            new ClearTrackNamesOptions(new[] { 0, 1, 2 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(3);
        result.Result.Value.RenamedTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { string.Empty, "BassDrum", string.Empty });
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name)
            .ShouldBe(new[] { "Piano", "BassDrum", "Custom" });
    }

    [Fact]
    public void Execute_WhenNothingChangesReturnsResultWithoutDirtyingFile()
    {
        var file = CreateEditableFile(CreateTrack(string.Empty, Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new ClearTrackNamesCommand(),
            EditorCommandContext.Create(session),
            new ClearTrackNamesOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.RenamedTracks.ShouldBe(0);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_CanClearPreservedDrumNamesWhenRequested()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit", Note(36, 0, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ClearTrackNamesCommand(),
            EditorCommandContext.Create(session),
            new ClearTrackNamesOptions(
                new[] { 0 },
                PreserveDrumInstrumentNames: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBeEmpty();
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] tracks)
        => new(new MidiFile(tracks));

    private static TrackChunk CreateTrack(string name, params TimedEvent[] events)
    {
        var chunk = new TrackChunk();
        if (!string.IsNullOrEmpty(name))
            chunk.Events.Add(new SequenceTrackNameEvent(name));

        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in events)
            manager.Objects.Add(timedEvent);

        return chunk;
    }

    private static TimedEvent Note(int noteNumber, long time, long length, int channel = 0)
        => new(
            new NoteOnEvent(
                (SevenBitNumber)(byte)noteNumber,
                (SevenBitNumber)100)
            {
                Channel = (FourBitNumber)(byte)channel,
            },
            time);
}
