using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class PrepareForPlaybackConservativeCommandTests
{
    [Fact]
    public void ConservativeOptions_AreStableOneClickDefaults()
    {
        var options = PrepareForPlaybackConservativeCommand.ConservativeOptions;

        options.FillEmptyTrackNames.ShouldBeTrue();
        options.ApplyTrackNameTransposes.ShouldBeTrue();
        options.SplitDrumkits.ShouldBeTrue();
        options.MaxSimultaneousNotes.ShouldBe(1);
        options.PickStrategy.ShouldBe(MidiForgeChordPickStrategy.HighestChords);
        options.RangeStrategy.ShouldBe(MidiForgeRangeFitStrategy.LowerHighNotesFirst);
        options.DrumTransposePreset.ShouldBe(MidiForgeDrumTransposePreset.Default);
    }

    [Fact]
    public void Execute_DelegatesToPrepareCommandAndSupportsSingleUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack(
                string.Empty,
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
                Note(60, 0, 120),
                Note(100, 0, 120)),
            CreateTrack("Flute+1", Note(60, 240, 120)),
            CreateTrack("Drumkit",
                Note(36, 0, 120, channel: 9),
                Note(38, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackConservativeCommand(),
            EditorCommandContext.Create(session),
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(3);
        result.Result.Value.FilledTrackNames.ShouldBe(1);
        result.Result.Value.TrackNameTransposeTracks.ShouldBe(1);
        result.Result.Value.DrumSourceTracks.ShouldBe(1);
        result.Result.Value.DrumTracksCreated.ShouldBe(2);
        result.Result.Value.AutoEditedTracks.ShouldBe(2);
        result.Result.Value.AutoEditedReplacedTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldContain("Flute");
        file.Tracks.Select(track => track.Name).ShouldContain("BassDrum");
        file.Tracks.Select(track => track.Name).ShouldContain("SnareDrum");
        file.Tracks.Select(track => track.Name).ShouldNotContain("Drumkit");
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Conductor",
            string.Empty,
            "Flute+1",
            "Drumkit",
        });
    }

    [Fact]
    public void Execute_NoChangeWhenFileHasNoPerformanceTracks()
    {
        var file = CreateEditableFile(CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackConservativeCommand(),
            EditorCommandContext.Create(session),
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
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
