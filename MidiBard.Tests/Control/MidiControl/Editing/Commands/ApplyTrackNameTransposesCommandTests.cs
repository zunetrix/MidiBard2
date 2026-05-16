using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ApplyTrackNameTransposesCommandTests
{
    [Theory]
    [InlineData("Piano+1", "Piano", 60, 72)]
    [InlineData("Piano -1", "Piano", 60, 48)]
    public void Execute_ReplacesTrackInPlaceCleansNameAndSupportsUndo(
        string sourceTrackName,
        string expectedTrackName,
        int sourceNote,
        int expectedNote)
    {
        var file = CreateEditableFile(CreateTrack(sourceTrackName, Note(sourceNote, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ApplyTrackNameTransposesCommand(),
            EditorCommandContext.Create(session),
            new ApplyTrackNameTransposesCommandOptions(
                new[] { 0 },
                new MidiForgeApplyTrackNameTransposeOptions(CreateNewTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        result.Result.Value.CleanedTrackNames.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe(expectedTrackName);
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)(byte)expectedNote);
        TrackInfo.GetTransposeByName(file.Tracks[0].Name).ShouldBe(0);
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe(sourceTrackName);
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)(byte)sourceNote);
    }

    [Fact]
    public void Execute_CreateNewTrackKeepsOriginalAndInsertsMigratedCopy()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano+1", Note(60, 0, 120)),
            CreateTrack("Flute", Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ApplyTrackNameTransposesCommand(),
            EditorCommandContext.Create(session),
            new ApplyTrackNameTransposesCommandOptions(
                new[] { 0 },
                new MidiForgeApplyTrackNameTransposeOptions(CreateNewTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano+1", "Piano", "Flute" });
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)72);
        session.PendingRefreshHints.ReloadEventList.ShouldBeFalse();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeFalse();
    }

    [Fact]
    public void Execute_CleansNameWhenTransposeIsClampedAndNoNotesChange()
    {
        var file = CreateEditableFile(CreateTrack("Piano+1", Note(127, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ApplyTrackNameTransposesCommand(),
            EditorCommandContext.Create(session),
            new ApplyTrackNameTransposesCommandOptions(
                new[] { 0 },
                new MidiForgeApplyTrackNameTransposeOptions(CreateNewTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        result.Result.Value.CleanedTrackNames.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(0);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)127);
        TrackInfo.GetTransposeByName(file.Tracks[0].Name).ShouldBe(0);
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Execute_SkipsTracksWithoutNonzeroTransposeWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("Flute+0", Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new ApplyTrackNameTransposesCommand(),
            EditorCommandContext.Create(session),
            new ApplyTrackNameTransposesCommandOptions(
                new[] { 0, 1 },
                new MidiForgeApplyTrackNameTransposeOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.SkippedTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Piano", "Flute+0" });
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_PreservesNonNoteEventsAndProgramTrackNames()
    {
        var file = CreateEditableFile(CreateTrack("Program: ElectricGuitar +1",
            Timed(new ProgramChangeEvent((SevenBitNumber)30) { Channel = (FourBitNumber)3 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)3 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)3 }, 20),
            Note(60, 0, 120, channel: 3)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new ApplyTrackNameTransposesCommand(),
            EditorCommandContext.Create(session),
            new ApplyTrackNameTransposesCommandOptions(
                new[] { 0 },
                new MidiForgeApplyTrackNameTransposeOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Program: ElectricGuitar");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)72);
        file.Tracks[0].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)3);
        file.Tracks[0].Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
        file.Tracks[0].Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
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
