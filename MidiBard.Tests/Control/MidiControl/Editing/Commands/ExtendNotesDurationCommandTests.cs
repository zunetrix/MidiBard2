using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ExtendNotesDurationCommandTests
{
    [Fact]
    public void Execute_CreatesExtendedTrackPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)2 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)2 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)2 }, 20),
            Note(60, 0, 120, channel: 2),
            Note(62, 480, 120, channel: 2),
            Note(64, 960, 120, channel: 2)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ExtendNotesDurationCommand(),
            EditorCommandContext.Create(session),
            new ExtendNotesDurationCommandOptions(
                new[] { 0 },
                new MidiForgeExtendNotesDurationOptions(RespectEmptyMeasures: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Piano (Extended)" });
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 480, 480, 120 });
        file.Tracks[1].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)2);
        file.Tracks[1].Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
        file.Tracks[1].Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 120, 120, 120 });
    }

    [Fact]
    public void Execute_RespectsMaximumDurationTicks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 480, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ExtendNotesDurationCommand(),
            EditorCommandContext.Create(session),
            new ExtendNotesDurationCommandOptions(
                new[] { 0 },
                new MidiForgeExtendNotesDurationOptions(
                    MaximumDurationTicks: 240,
                    RespectEmptyMeasures: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ChangedNotes.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new long[] { 240, 120 });
    }

    [Theory]
    [InlineData(true, 1920)]
    [InlineData(false, 3840)]
    public void Execute_CanRespectEmptyMeasuresWhenExtending(bool respectEmptyMeasures, long expectedLength)
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(62, 3840, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ExtendNotesDurationCommand(),
            EditorCommandContext.Create(session),
            new ExtendNotesDurationCommandOptions(
                new[] { 0 },
                new MidiForgeExtendNotesDurationOptions(RespectEmptyMeasures: respectEmptyMeasures)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ChangedNotes.ShouldBe(1);
        file.Tracks[1].Chunk.GetNotes().Select(note => note.Length).ShouldBe(new[] { expectedLength, 120 });
    }

    [Fact]
    public void Execute_DoesNotDirtyWhenNoNotesCanBeExtended()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new ExtendNotesDurationCommand(),
            EditorCommandContext.Create(session),
            new ExtendNotesDurationCommandOptions(
                new[] { 0, 1 },
                new MidiForgeExtendNotesDurationOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
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
