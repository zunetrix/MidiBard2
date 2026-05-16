using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SplitTracksChordsCommandTests
{
    [Fact]
    public void Execute_GroupMergedCreatesNoChordAndChordPartTracksPreservesEventsAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)2 }, 0),
            Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)2 }, 10),
            Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)2 }, 20),
            Note(60, 0, 120, channel: 2),
            Note(64, 0, 120, channel: 2),
            Note(67, 0, 120, channel: 2),
            Note(72, 240, 120, channel: 2)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(
                new[] { 0 },
                new MidiForgeSplitChordsOptions(
                    Strategy: MidiForgeChordSplitStrategy.SameStartTick,
                    GroupMode: MidiForgeChordGroupMode.GroupMerged,
                    MinimumSimultaneousNotes: 2,
                    InsertPartsAtEnd: true)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(4);
        result.Result.Value.ChordGroups.ShouldBe(3);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano no chords",
            "Piano chords parts (1)",
            "Piano chords parts (2)",
            "Piano chords parts (3)",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 72 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67 });
        file.Tracks[3].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 64 });
        file.Tracks[4].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60 });
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
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 64, 67, 72 });
    }

    [Fact]
    public void Execute_IndividualModeSeparatesChordSizesAndParts()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 240, 120),
            Note(71, 240, 120),
            Note(74, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(
                new[] { 0 },
                new MidiForgeSplitChordsOptions(GroupMode: MidiForgeChordGroupMode.Individual)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(5);
        file.Tracks.Skip(1).Select(track => track.Name).ShouldBe(new[]
        {
            "Piano chords of 2 (1)",
            "Piano chords of 2 (2)",
            "Piano chords of 3 (1)",
            "Piano chords of 3 (2)",
            "Piano chords of 3 (3)",
        });
    }

    [Fact]
    public void Execute_GroupModeCreatesWholeChordSizeTracks()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 240, 120),
            Note(71, 240, 120),
            Note(74, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(
                new[] { 0 },
                new MidiForgeSplitChordsOptions(GroupMode: MidiForgeChordGroupMode.Group)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        file.Tracks.Skip(1).Select(track => track.Name).ShouldBe(new[]
        {
            "Piano chords of 2",
            "Piano chords of 3",
        });
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 60, 64 });
        file.Tracks[2].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).ShouldBe(new[] { 67, 71, 74 });
    }

    [Fact]
    public void Execute_SameStartTickAndLengthKeepsDifferentDurationNotesAsNoChord()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 240)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(
                new[] { 0 },
                new MidiForgeSplitChordsOptions(
                    Strategy: MidiForgeChordSplitStrategy.SameStartTickAndLength,
                    InsertPartsAtEnd: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("Piano no chords");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber).OrderBy(note => note)
            .ShouldBe(new[] { 60, 64 });
    }

    [Fact]
    public void Execute_InsertPartsAtEndFalseInsertsDerivedTracksAfterSource()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Note(60, 0, 120),
                Note(64, 0, 120)),
            CreateTrack("Flute", Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(
                new[] { 0 },
                new MidiForgeSplitChordsOptions(InsertPartsAtEnd: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "Piano chords parts (1)",
            "Piano chords parts (2)",
            "Flute",
        });
    }

    [Fact]
    public void Execute_SkipsTracksWithoutNotesWithoutDirtyingFile()
    {
        var file = CreateEditableFile(CreateTrack(
            "Empty",
            Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(
                new[] { 0 },
                new MidiForgeSplitChordsOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ChordGroups.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
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
