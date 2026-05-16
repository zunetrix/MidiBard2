using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class AutoEditSelectedTracksCommandTests
{
    [Fact]
    public void Execute_PicksChordLinesAdaptsRangeAndSupportsSingleUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(100, 0, 120),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AutoEditSelectedTracksCommand(),
            EditorCommandContext.Create(session),
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: true)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(2);
        result.Result.Value.ChangedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 100, 72 });
        file.Tracks[1].Name.ShouldBe("Piano (Auto Edited Max 1)");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { MidiForgeNotePrimitives.AdaptMidiNoteToPlayableRange(100), 72 });
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
    }

    [Fact]
    public void Execute_ReplaceOriginalUsesChildReloadHintsAndSingleUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AutoEditSelectedTracksCommand(),
            EditorCommandContext.Create(session),
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: false,
                    CreateNewTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano (Auto Edited Max 1)");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 67, 72 });
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Piano");
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 64, 67, 72 });
    }

    [Fact]
    public void Execute_SkipsRangeAdaptationWhenDisabled()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(100, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AutoEditSelectedTracksCommand(),
            EditorCommandContext.Create(session),
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ChangedNotes.ShouldBe(0);
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)100);
    }

    [Fact]
    public void Execute_UsesTolerantChordGroupingWhenConfigured()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(60, 0, 120),
            Note(64, 8, 120),
            Note(67, 12, 120),
            Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AutoEditSelectedTracksCommand(),
            EditorCommandContext.Create(session),
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: false,
                    ChordTimingTolerance: new MidiForgeChordTimingToleranceOptions(
                        MidiForgeChordTimingToleranceMode.OneOver128Note))));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.PickedParts.ShouldBe(2);
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time))
            .ShouldBe(new[] { (67, 12L), (72, 240L) });
    }

    [Fact]
    public void Execute_AggregatesMultipleTracksThroughChildCommands()
    {
        var file = CreateEditableFile(
            CreateTrack("Lead",
                Note(72, 0, 120),
                Note(100, 240, 120)),
            CreateTrack("Chord",
                Note(60, 0, 120),
                Note(64, 0, 120),
                Note(67, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new AutoEditSelectedTracksCommand(),
            EditorCommandContext.Create(session),
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0, 1 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: true,
                    RangeStrategy: MidiForgeRangeFitStrategy.LowerHighNotesFirst)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(2);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Lead",
            "Lead (Auto Edited Max 1)",
            "Chord",
            "Chord (Auto Edited Max 1)",
        });
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Execute_NoChangeWhenNoSelectedTracksHaveNotes()
    {
        var file = CreateEditableFile(CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new AutoEditSelectedTracksCommand(),
            EditorCommandContext.Create(session),
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(0);
        result.Result.Value.ChangedNotes.ShouldBe(0);
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
