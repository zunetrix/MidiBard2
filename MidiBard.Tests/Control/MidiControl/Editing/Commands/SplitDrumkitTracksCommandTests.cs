using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Drum;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitDrumkitTracksCommandTests
{
    [Fact]
    public void Execute_DefaultMapCreatesGameDrumTracksAndRestPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)9 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)9 }, 20),
            Note(36, 0, 120, channel: 9),
            Note(38, 120, 120, channel: 9),
            Note(49, 240, 120, channel: 9),
            Note(60, 360, 120, channel: 9),
            Note(42, 480, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitDrumkitOptions(AutoEditAfterSplit: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(5);
        result.Result.Value.RestTracks.ShouldBe(1);
        result.Result.Value.AutoEditedTracks.ShouldBe(0);
        result.Result.Value.TransposedNotes.ShouldBe(3);
        result.Result.Value.DeletedSourceTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "BassDrum",
            "SnareDrum",
            "Cymbal",
            "Bongo",
            "Drumkit Rest",
            "Drumkit",
        });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 51 });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 73 });
        file.Tracks[3].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60 });
        file.Tracks[4].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 42 });
        file.Tracks.Take(5).SelectMany(track => track.Chunk.GetNotes()).ShouldAllBe(note => (byte)note.Channel == 9);
        foreach (var splitTrack in file.Tracks.Take(5))
        {
            splitTrack.Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)9);
            splitTrack.Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
            splitTrack.Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        }

        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Drumkit");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 36, 38, 49, 60, 42 });
    }

    [Fact]
    public void Execute_BardForge2PresetUsesAlternateTransposeMap()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(36, 0, 120, channel: 9),
            Note(38, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitDrumkitOptions(
                    AutoEditAfterSplit: false,
                    CreateRestTrack: false,
                    TransposePreset: MidiForgeDrumTransposePreset.BardForge2)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        file.Tracks[0].Name.ShouldBe("BassDrum");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)55);
        file.Tracks[1].Name.ShouldBe("SnareDrum");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)64);
    }

    [Fact]
    public void Execute_CanKeepSourceInPlaceAndOmitRestTrack()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(36, 0, 120, channel: 9),
            Note(42, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitDrumkitOptions(
                    AutoEditAfterSplit: false,
                    CreateRestTrack: false,
                    MoveSourceTracksToEnd: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.RestTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Drumkit", "BassDrum" });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 36, 42 });
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)51);
    }

    [Fact]
    public void Execute_DeleteOriginalTracksRemovesSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(
            CreateTrack("Drumkit",
                Note(36, 0, 120, channel: 9),
                Note(38, 120, 120, channel: 9)),
            CreateTrack("Flute", Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitDrumkitOptions(
                    AutoEditAfterSplit: false,
                    CreateRestTrack: false,
                    DeleteOriginalTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DeletedSourceTracks.ShouldBe(1);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Flute", "BassDrum", "SnareDrum" });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Drumkit", "Flute" });
    }

    [Fact]
    public void Execute_AutoEditAfterSplitPicksHighestSameStartNote()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(35, 0, 120, channel: 9),
            Note(36, 0, 120, channel: 9),
            Note(38, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitDrumkitOptions(CreateRestTrack: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.AutoEditedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("BassDrum");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 51 });
        file.Tracks[1].Name.ShouldBe("SnareDrum");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 62 });
    }

    [Fact]
    public void Execute_AutoEditAfterSplitTransposesC3HeavyBassTrack()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(35, 0, 120, channel: 9),
            Note(35, 120, 120, channel: 9),
            Note(35, 240, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeSplitDrumkitOptions(CreateRestTrack: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.AutoEditedTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("BassDrum");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 52, 52, 52 });
    }

    [Fact]
    public void Execute_SkipsNonDrumChannelAndConductorTracksWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano", Note(36, 0, 120, channel: 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SplitDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new SplitDrumkitTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitDrumkitOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.DeletedSourceTracks.ShouldBe(0);
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
