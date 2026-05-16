using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Drum;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class DisassembleDrumkitTracksCommandTests
{
    [Fact]
    public void Execute_CreatesOneTrackPerUniqueDrumNotePreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)9 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)9 }, 20),
            Note(36, 0, 120, channel: 9),
            Note(36, 240, 120, channel: 9),
            Note(38, 480, 120, channel: 9),
            Note(42, 720, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DisassembleDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new DisassembleDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeDisassembleDrumkitOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(3);
        result.Result.Value.DeletedSourceTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Drumkit",
            "Kick Drum 1",
            "Snare Drum 1",
            "Closed Hi-Hat",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 36, 36 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 38 });
        file.Tracks[3].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 42 });
        foreach (var splitTrack in file.Tracks.Skip(1))
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
            .ShouldBe(new[] { 36, 36, 38, 42 });
    }

    [Fact]
    public void Execute_UnknownNotesUseFallbackName()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit", Note(12, 0, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DisassembleDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new DisassembleDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeDisassembleDrumkitOptions()));

        result.Succeeded.ShouldBeTrue();
        file.Tracks[1].Name.ShouldBe("Drumkit Unknown");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)12);
    }

    [Fact]
    public void Execute_DeleteOriginalTracksRemovesSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(CreateTrack("Drumkit",
            Note(36, 0, 120, channel: 9),
            Note(38, 240, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DisassembleDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new DisassembleDrumkitTracksCommandOptions(
                new[] { 0 },
                new MidiForgeDisassembleDrumkitOptions(DeleteOriginalTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DeletedSourceTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Kick Drum 1", "Snare Drum 1" });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Drumkit" });
    }

    [Fact]
    public void Execute_DeleteOriginalTracksRemovesMultipleSources()
    {
        var file = CreateEditableFile(
            CreateTrack("Drums A", Note(36, 0, 120, channel: 9)),
            CreateTrack("Drums B", Note(38, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new DisassembleDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new DisassembleDrumkitTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeDisassembleDrumkitOptions(DeleteOriginalTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.DeletedSourceTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[] { "Kick Drum 1", "Snare Drum 1" });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
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
            new DisassembleDrumkitTracksCommand(),
            EditorCommandContext.Create(session),
            new DisassembleDrumkitTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeDisassembleDrumkitOptions()));

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
