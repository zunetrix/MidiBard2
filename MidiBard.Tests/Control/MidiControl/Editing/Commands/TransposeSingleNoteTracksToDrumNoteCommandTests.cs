using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Drum;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class TransposeSingleNoteTracksToDrumNoteCommandTests
{
    [Fact]
    public void Execute_DeleteOriginalByDefaultReplacesSinglePitchTrackPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Hand Clap",
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }, 0),
                Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)9 }, 10),
                Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)9 }, 20),
                Note(39, 0, 120, channel: 9),
                Note(39, 240, 120, channel: 9)),
            CreateTrack("Mixed", Note(38, 0, 120, channel: 9), Note(40, 240, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TransposeSingleNoteTracksToDrumNoteCommand(),
            EditorCommandContext.Create(session),
            new TransposeSingleNoteTracksToDrumNoteCommandOptions(
                new[] { 0, 1 },
                new MidiForgeTransposeToDrumNoteOptions(TargetNote: 62, TrackName: " SnareDrum ")));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.DeletedSourceTracks.ShouldBe(1);
        result.Result.Value.SkippedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Name.ShouldBe("SnareDrum");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62, 62 });
        file.Tracks[0].Chunk.GetNotes().ShouldAllBe(note => (byte)note.Channel == 9);
        file.Tracks[0].Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)9);
        file.Tracks[0].Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
        file.Tracks[0].Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        file.Tracks[1].Name.ShouldBe("Mixed");
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Hand Clap", "Mixed" });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 39, 39 });
    }

    [Fact]
    public void Execute_KeepOriginalInsertsAfterEachSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(
            CreateTrack("Hand Clap", Note(39, 0, 120, channel: 9)),
            CreateTrack("Snare", Note(40, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TransposeSingleNoteTracksToDrumNoteCommand(),
            EditorCommandContext.Create(session),
            new TransposeSingleNoteTracksToDrumNoteCommandOptions(
                new[] { 0, 1 },
                new MidiForgeTransposeToDrumNoteOptions(
                    TargetNote: 73,
                    TrackName: "Cymbal",
                    DeleteOriginalTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DeletedSourceTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Hand Clap", "Cymbal", "Snare", "Cymbal" });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)39);
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)73);
        file.Tracks[2].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)40);
        file.Tracks[3].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)73);
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeFalse();
        session.PendingRefreshHints.ReloadEventList.ShouldBeFalse();
    }

    [Fact]
    public void Execute_UsesFallbackNameWhenTrackNameEmptyAndClampsTargetNote()
    {
        var file = CreateEditableFile(CreateTrack("Snare", Note(40, 0, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TransposeSingleNoteTracksToDrumNoteCommand(),
            EditorCommandContext.Create(session),
            new TransposeSingleNoteTracksToDrumNoteCommandOptions(
                new[] { 0 },
                new MidiForgeTransposeToDrumNoteOptions(TargetNote: 200, TrackName: string.Empty)));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Snare (Transposed 87)");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)127);
    }

    [Fact]
    public void Execute_SkipsConductorEmptyAndMultiPitchTracksWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)),
            CreateTrack("Mixed", Note(38, 0, 120, channel: 9), Note(40, 240, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new TransposeSingleNoteTracksToDrumNoteCommand(),
            EditorCommandContext.Create(session),
            new TransposeSingleNoteTracksToDrumNoteCommandOptions(
                new[] { 0, 1, 2 },
                new MidiForgeTransposeToDrumNoteOptions(TargetNote: 62, TrackName: "SnareDrum")));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.DeletedSourceTracks.ShouldBe(0);
        result.Result.Value.SkippedTracks.ShouldBe(2);
        file.Tracks.Count.ShouldBe(3);
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
