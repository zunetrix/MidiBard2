using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class FillEmptyTrackNamesCommandTests
{
    [Fact]
    public void Execute_FillsOnlyEmptyNamesAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack(
                string.Empty,
                Timed(new ProgramChangeEvent((SevenBitNumber)0), 0),
                Note(60, 0, 120)),
            CreateTrack(
                "Custom",
                Timed(new ProgramChangeEvent((SevenBitNumber)40), 0),
                Note(64, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new FillEmptyTrackNamesCommand(),
            EditorCommandContext.Create(session),
            new FillEmptyTrackNamesOptions(new[] { 0, 1 }, MidiForgeTrackNameFillMode.Midi));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.RenamedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Acoustic Grand Piano");
        file.Tracks[1].Name.ShouldBe("Custom");
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBeEmpty();
        file.Tracks[1].Name.ShouldBe("Custom");
    }

    [Fact]
    public void Execute_DrumChannelUsesDrumkitName()
    {
        var file = CreateEditableFile(CreateTrack(string.Empty, Note(36, 0, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new FillEmptyTrackNamesCommand(),
            EditorCommandContext.Create(session),
            new FillEmptyTrackNamesOptions(new[] { 0 }, MidiForgeTrackNameFillMode.Midi));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Drumkit");
    }

    [Fact]
    public void Execute_WhenNoNamesAreEmptyReturnsResultWithoutDirtyingFile()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new FillEmptyTrackNamesCommand(),
            EditorCommandContext.Create(session),
            new FillEmptyTrackNamesOptions(new[] { 0 }, MidiForgeTrackNameFillMode.Midi));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.RenamedTracks.ShouldBe(0);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
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

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);

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
