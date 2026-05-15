using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class AdaptTracksToPlayableRangeCommandTests
{
    [Fact]
    public void Execute_CreateNewTrackKeepsOriginalAdaptsNotesPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)4 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)4 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)4 }, 20),
            Note(36, 0, 120, channel: 4),
            Note(60, 120, 120, channel: 4),
            Note(96, 240, 120, channel: 4)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                new[] { 0 },
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: true,
                    RangeStrategy: MidiForgeRangeFitStrategy.FitNotesIndividually)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Piano (Adapted 2 notes)" });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 36, 60, 96 });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 48, 60, 84 });
        file.Tracks[1].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)4);
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
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 36, 60, 96 });
    }

    [Fact]
    public void Execute_BestOctaveFitAppliesBestOctaveBeforeWrapping()
    {
        var file = CreateEditableFile(CreateTrack("Low",
            Note(36, 0, 120),
            Note(40, 120, 120),
            Note(60, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                new[] { 0 },
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: true,
                    RangeStrategy: MidiForgeRangeFitStrategy.BestOctaveFit)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.OctaveShiftedTracks.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 48, 52, 72 });
    }

    [Fact]
    public void Execute_LowerHighNotesFirstLowersWholeTrackBeforeWrapping()
    {
        var file = CreateEditableFile(CreateTrack("Lead",
            Note(60, 0, 120),
            Note(88, 120, 120),
            Note(100, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                new[] { 0 },
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: true,
                    RangeStrategy: MidiForgeRangeFitStrategy.LowerHighNotesFirst)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.OctaveShiftedTracks.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(3);
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 48, 64, 76 });
    }

    [Fact]
    public void Execute_ReplaceOriginalSupportsUndoAndReloadsSelection()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(96, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                new[] { 0 },
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: false,
                    RangeStrategy: MidiForgeRangeFitStrategy.FitNotesIndividually)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Adapted 1 notes)");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)84);
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)96);
    }

    [Fact]
    public void Execute_CanPreserveTrackNameWhenRenameTracksFalse()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(96, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                new[] { 0 },
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: true,
                    RangeStrategy: MidiForgeRangeFitStrategy.FitNotesIndividually,
                    RenameTracks: false)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Piano" });
    }

    [Fact]
    public void Execute_SkipsConductorAndEmptyTracksWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new SequenceTrackNameEvent("Empty"), 0)));
        var session = new MidiEditorSessionState { File = file };
        var initialTrackCount = file.Tracks.Count;
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                new[] { 0, 1 },
                new MidiForgeAdaptToRangeOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(initialTrackCount);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(36, 48)]
    [InlineData(60, 60)]
    [InlineData(96, 84)]
    public void AdaptMidiNoteToPlayableRange_WrapsSingleNotesIntoPlayableRange(int sourceNote, int expectedNote)
        => MidiForgeNotePrimitives.AdaptMidiNoteToPlayableRange(sourceNote).ShouldBe(expectedNote);

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
