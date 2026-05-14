using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Guitar;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class MergeGuitarToneTracksCommandTests
{
    [Fact]
    public void Execute_CreatesProgramElectricGuitarTrackWithGeneratedProgramChangesAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("ElectricGuitarClean",
                Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)5 }, 10),
                Note(60, 0, 120, channel: 5)),
            CreateTrack("ElectricGuitarPowerChords",
                Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)6 }, 20),
                Note(64, 0, 120, channel: 6)),
            CreateTrack("ElectricGuitarSpecial", Note(67, 240, 120, channel: 7)));
        var toneByTrack = new Dictionary<int, int>
        {
            [0] = 1,
            [1] = 3,
            [2] = 4,
        };
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MergeGuitarToneTracksCommand(),
            EditorCommandContext.Create(session),
            new MergeGuitarToneTracksCommandOptions(
                new[] { 0, 1, 2 },
                new MidiForgeMergeGuitarToneTracksOptions(toneByTrack)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(3);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.DeletedSourceTracks.ShouldBe(0);
        result.Result.Value.SkippedTracks.ShouldBe(0);
        result.Result.Value.GeneratedProgramChanges.ShouldBe(3);
        result.Result.Value.MergedNotes.ShouldBe(3);
        result.Result.Value.MergedChannelEvents.ShouldBe(2);
        file.Tracks.Count.ShouldBe(4);

        var mergedTrack = file.Tracks[3];
        mergedTrack.Name.ShouldBe("ProgramElectricGuitar");

        var expectedPrograms = toneByTrack
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                MidiForgeOperations.TryResolveGuitarProgramForTone(pair.Value, out var program).ShouldBeTrue();
                return (int)(byte)program;
            })
            .ToArray();
        mergedTrack.Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
            .Select(timedEvent => (
                timedEvent.Time,
                Channel: (int)(byte)((ProgramChangeEvent)timedEvent.Event).Channel,
                Program: (int)(byte)((ProgramChangeEvent)timedEvent.Event).ProgramNumber))
            .ShouldBe(new[]
            {
                (0L, 0, expectedPrograms[0]),
                (0L, 1, expectedPrograms[1]),
                (0L, 2, expectedPrograms[2]),
            });

        mergedTrack.Chunk.GetNotes()
            .OrderBy(note => note.Time)
            .ThenBy(note => (byte)note.NoteNumber)
            .Select(note => (
                Note: (int)(byte)note.NoteNumber,
                Channel: (int)(byte)note.Channel,
                note.Time))
            .ShouldBe(new[]
            {
                (60, 0, 0L),
                (64, 1, 0L),
                (67, 2, 240L),
            });
        mergedTrack.Chunk.Events.OfType<ControlChangeEvent>().Single().Channel.ShouldBe((FourBitNumber)0);
        mergedTrack.Chunk.Events.OfType<PitchBendEvent>().Single().Channel.ShouldBe((FourBitNumber)1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "ElectricGuitarClean",
            "ElectricGuitarPowerChords",
            "ElectricGuitarSpecial",
        });
    }

    [Fact]
    public void Execute_DeleteOriginalTracks_ReplacesSelectedTracksWithMergedTrack()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(48, 0, 120)),
            CreateTrack("ElectricGuitarClean", Note(60, 0, 120)),
            CreateTrack("ElectricGuitarOverdriven", Note(64, 120, 120)),
            CreateTrack("Flute", Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MergeGuitarToneTracksCommand(),
            EditorCommandContext.Create(session),
            new MergeGuitarToneTracksCommandOptions(
                new[] { 1, 2 },
                new MidiForgeMergeGuitarToneTracksOptions(
                    new Dictionary<int, int> { [1] = 1, [2] = 0 },
                    DeleteOriginalTracks: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.DeletedSourceTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Piano",
            "ProgramElectricGuitar",
            "Flute",
        });
        file.Tracks.Select(track => track.Index).ShouldBe(Enumerable.Range(0, file.Tracks.Count));
    }

    [Fact]
    public void Execute_SkipsTracksWithoutResolvedToneAndEmptyTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("ElectricGuitarClean", Note(60, 0, 120)),
            CreateTrack("Unknown", Note(64, 120, 120)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MergeGuitarToneTracksCommand(),
            EditorCommandContext.Create(session),
            new MergeGuitarToneTracksCommandOptions(
                new[] { 0, 1, 2 },
                new MidiForgeMergeGuitarToneTracksOptions(new Dictionary<int, int> { [0] = 1, [2] = 0 })));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.SkippedTracks.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("ProgramElectricGuitar");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    [Fact]
    public void Execute_DoesNotDirtyWhenNoMergeableTracks()
    {
        var file = CreateEditableFile(
            CreateTrack("Unknown", Note(60, 0, 120)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new MergeGuitarToneTracksCommand(),
            EditorCommandContext.Create(session),
            new MergeGuitarToneTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeMergeGuitarToneTracksOptions(new Dictionary<int, int> { [1] = 0 })));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.SkippedTracks.ShouldBe(2);
        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_UsesCustomTrackNameOmitsDisabledChannelEventsAndSkipsPastMaximumChannels()
    {
        var sourceCount = MidiForgeOperations.MaximumGuitarToneMergeTracks + 1;
        var chunks = Enumerable.Range(0, sourceCount)
            .Select(index => CreateTrack($"Guitar {index}",
                Timed(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)90) { Channel = (FourBitNumber)5 }, 10),
                Timed(new PitchBendEvent((ushort)12288) { Channel = (FourBitNumber)5 }, 20),
                Note(60 + index % 12, index * 120, 60, channel: 5)))
            .ToArray();
        var file = CreateEditableFile(chunks);
        var toneByTrack = Enumerable.Range(0, sourceCount).ToDictionary(index => index, index => index % 5);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MergeGuitarToneTracksCommand(),
            EditorCommandContext.Create(session),
            new MergeGuitarToneTracksCommandOptions(
                Enumerable.Range(0, sourceCount).ToArray(),
                new MidiForgeMergeGuitarToneTracksOptions(
                    toneByTrack,
                    TrackName: "  Custom Guitar  ",
                    IncludePitchBendEvents: false,
                    IncludeControlChangeEvents: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(MidiForgeOperations.MaximumGuitarToneMergeTracks);
        result.Result.Value.SkippedTracks.ShouldBe(1);
        result.Result.Value.GeneratedProgramChanges.ShouldBe(MidiForgeOperations.MaximumGuitarToneMergeTracks);
        result.Result.Value.MergedChannelEvents.ShouldBe(MidiForgeOperations.MaximumGuitarToneMergeTracks);
        var mergedTrack = file.Tracks[MidiForgeOperations.MaximumGuitarToneMergeTracks];
        mergedTrack.Name.ShouldBe("Custom Guitar");
        mergedTrack.Chunk.Events.OfType<PitchBendEvent>().ShouldBeEmpty();
        mergedTrack.Chunk.Events.OfType<ControlChangeEvent>().Count()
            .ShouldBe(MidiForgeOperations.MaximumGuitarToneMergeTracks);
        mergedTrack.Chunk.GetNotes().Any(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel).ShouldBeFalse();
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
