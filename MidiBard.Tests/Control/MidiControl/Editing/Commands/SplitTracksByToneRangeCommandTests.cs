using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitTracksByToneRangeCommandTests
{
    [Fact]
    public void Execute_CreatesInRangeAndOutOfRangeTracksPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)2 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)2 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)2 }, 20),
            Note(47, 0, 120, channel: 2),
            Note(48, 120, 120, channel: 2),
            Note(84, 240, 120, channel: 2),
            Note(85, 360, 120, channel: 2)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "C3", MaximumNote: "C6")));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.InRangeTracks.ShouldBe(1);
        result.Result.Value.OutOfRangeTracks.ShouldBe(1);
        result.Result.Value.InRangeNotes.ShouldBe(2);
        result.Result.Value.OutOfRangeNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range C3 (48) - C6 (84))",
            "Piano (Out of Range C3 (48) - C6 (84))",
        });
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 47, 48, 84, 85 });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 48, 84 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 47, 85 });
        foreach (var splitTrack in file.Tracks.Skip(1))
        {
            splitTrack.Chunk.Events.OfType<ProgramChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)2);
            splitTrack.Chunk.Events.OfType<ControlChangeEvent>().Single().ControlValue.ShouldBe((SevenBitNumber)90);
            splitTrack.Chunk.Events.OfType<PitchBendEvent>().Single().PitchValue.ShouldBe((ushort)12288);
        }

        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 47, 48, 84, 85 });
    }

    [Fact]
    public void Execute_SkipsEmptyPartitions()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "C3", MaximumNote: "C6")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.InRangeTracks.ShouldBe(1);
        result.Result.Value.OutOfRangeTracks.ShouldBe(0);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano (In Range C3 (48) - C6 (84))",
        });
    }

    [Fact]
    public void Execute_NormalizesInvertedAndClampedRange()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(0, 0, 120),
            Note(60, 120, 120),
            Note(127, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "200", MaximumNote: "-10")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.InRangeNotes.ShouldBe(3);
        result.Result.Value.OutOfRangeNotes.ShouldBe(0);
        file.Tracks[1].Name.ShouldBe("Piano (In Range C-1 (0) - G9 (127))");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 0, 60, 127 });
    }

    [Fact]
    public void Execute_InsertsGeneratedTracksAfterEachSourceAndRefreshesIndexes()
    {
        var file = CreateEditableFile(
            CreateTrack("Low",
                Note(47, 0, 120),
                Note(48, 120, 120)),
            CreateTrack("High",
                Note(84, 0, 120),
                Note(85, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "C3", MaximumNote: "C6")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(4);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Low",
            "Low (In Range C3 (48) - C6 (84))",
            "Low (Out of Range C3 (48) - C6 (84))",
            "High",
            "High (In Range C3 (48) - C6 (84))",
            "High (Out of Range C3 (48) - C6 (84))",
        });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
    }

    [Fact]
    public void Execute_SkipsConductorAndEmptyTracksWithoutDirtyingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0, 1 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "C3", MaximumNote: "C6")));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    // --- Note text input tests ---

    [Fact]
    public void Execute_AcceptsNoteTextMinimumMaximum()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(47, 0, 120),
            Note(60, 120, 120),
            Note(72, 240, 120),
            Note(84, 360, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "C3", MaximumNote: "C5")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.InRangeNotes.ShouldBe(2); // C4 (60), C5 (72)
        result.Result.Value.OutOfRangeNotes.ShouldBe(2); // B2 (47), C6 (84)
    }

    [Fact]
    public void Execute_AcceptsWithAndFlatNoteText()
    {
        var file = CreateEditableFile(CreateTrack("Guitar",
            Note(50, 0, 120),    // D#3 / Eb3
            Note(55, 120, 120),  // G3
            Note(70, 240, 120),  // A#4 / Bb4
            Note(75, 360, 120))); // B4
        var session = new MidiEditorSessionState { File = file };

        // Db3 (49) to G#5 (81) — all 4 notes fall in range
        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "Db3", MaximumNote: "G#5")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.InRangeNotes.ShouldBe(4);
        result.Result.Value.OutOfRangeNotes.ShouldBe(0);
    }

    [Fact]
    public void Execute_AcceptsIntegerStringsAsWellAsNoteText()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(47, 0, 120),
            Note(60, 120, 120),
            Note(84, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "48", MaximumNote: "84")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.InRangeNotes.ShouldBe(2); // C3 (48) and C6 (84) are at range boundaries
        result.Result.Value.OutOfRangeNotes.ShouldBe(1); // B2 (47) is out
    }

    [Fact]
    public void Execute_MixedIntegerAndNoteText()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(47, 0, 120),
            Note(60, 120, 120),
            Note(84, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        // Minimum as integer string, Maximum as note text
        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "48", MaximumNote: "C5")));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.InRangeNotes.ShouldBe(1); // only C4 (60) is in [48, 72]
        result.Result.Value.OutOfRangeNotes.ShouldBe(2); // B2 (47) and C6 (84)
    }

    [Fact]
    public void Execute_DefaultValuesArePlayableRange()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(47, 0, 120),   // below C3
            Note(60, 120, 120), // middle C
            Note(84, 240, 120), // C6
            Note(85, 360, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions()));

        result.Succeeded.ShouldBeTrue();
        // default is C3-C6 (48-84)
        result.Result!.Value.InRangeNotes.ShouldBe(2); // C4 (60), C6 (84)
        result.Result.Value.OutOfRangeNotes.ShouldBe(2); // B2 (47), C#6 (85)
    }

    [Fact]
    public void Execute_FallbackToDefaultWhenInputCannotBeParsed()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(47, 0, 120),
            Note(60, 120, 120),
            Note(84, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksByToneRangeCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksByToneRangeCommandOptions(
                new[] { 0 },
                new MidiForgeSplitToneRangeOptions(MinimumNote: "garbage", MaximumNote: "notanote")));

        result.Succeeded.ShouldBeTrue();
        // Falls back to defaults C3-C6
        result.Result!.Value.InRangeNotes.ShouldBe(2); // C4 (60), C6 (84)
        result.Result.Value.OutOfRangeNotes.ShouldBe(1); // B2 (47)
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
