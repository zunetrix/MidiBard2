using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class BuildAutoEditRecommendationsQueryTests
{
    [Fact]
    public void Execute_PredictsChordPickingAndRangeAdaptationWithoutMutatingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Note(60, 0, 120),
                Note(100, 0, 120),
                Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorQueryExecutor().Execute(
            new BuildAutoEditRecommendationsQuery(),
            EditorQueryContext.Create(session),
            new BuildAutoEditRecommendationsQueryOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SelectedTracks.ShouldBe(1);
        result.Result.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.TracksToEdit.ShouldBe(1);
        result.Result.Value.CreatedTracks.ShouldBe(1);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(2);
        result.Result.Value.ChangedNotes.ShouldBe(1);
        result.Result.Value.WillCreateNewTracks.ShouldBeTrue();
        result.Result.Value.WillAdaptOutOfRangeNotes.ShouldBeTrue();

        var track = result.Result.Value.Tracks.Single();
        track.TrackIndex.ShouldBe(0);
        track.TrackName.ShouldBe("Piano");
        track.HasNotes.ShouldBeTrue();
        track.WillEdit.ShouldBeTrue();
        track.CreatedTracks.ShouldBe(1);
        track.ChangedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 100, 72 });
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
    }

    [Fact]
    public void Execute_ReportsReplacementRecommendations()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Note(60, 0, 120),
                Note(64, 0, 120),
                Note(67, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new BuildAutoEditRecommendationsQuery(),
            EditorQueryContext.Create(session),
            new BuildAutoEditRecommendationsQueryOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: false,
                    CreateNewTracks: false)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.CreatedTracks.ShouldBe(0);
        result.Result.Value.ReplacedTracks.ShouldBe(1);
        result.Result.Value.Tracks.Single().ReplacedTracks.ShouldBe(1);
        result.Result.Value.Tracks.Single().Reason.ShouldBe("Will pick chord lines without range adaptation.");
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Piano");
    }

    [Fact]
    public void Execute_ReportsEmptyTracksAndSkipsConductorAndInvalidIndices()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Empty", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)),
            CreateTrack("Piano", Note(72, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new BuildAutoEditRecommendationsQuery(),
            EditorQueryContext.Create(session),
            new BuildAutoEditRecommendationsQueryOptions(
                new[] { -1, 0, 1, 2, 2, 99 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: true)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SelectedTracks.ShouldBe(2);
        result.Result.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.TracksToEdit.ShouldBe(1);
        result.Result.Value.Tracks.Select(track => track.TrackName).ShouldBe(new[] { "Empty", "Piano" });
        result.Result.Value.Tracks[0].HasNotes.ShouldBeFalse();
        result.Result.Value.Tracks[0].Reason.ShouldBe("Track has no notes.");
        result.Result.Value.Tracks[1].WillEdit.ShouldBeTrue();
    }

    [Fact]
    public void Execute_AggregatesMultipleTrackRecommendations()
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

        var result = new EditorQueryExecutor().Execute(
            new BuildAutoEditRecommendationsQuery(),
            EditorQueryContext.Create(session),
            new BuildAutoEditRecommendationsQueryOptions(
                new[] { 0, 1 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: true,
                    RangeStrategy: MidiForgeRangeFitStrategy.LowerHighNotesFirst)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.SelectedTracks.ShouldBe(2);
        result.Result.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.TracksToEdit.ShouldBe(2);
        result.Result.Value.CreatedTracks.ShouldBe(2);
        result.Result.Value.ReplacedTracks.ShouldBe(0);
        result.Result.Value.PickedParts.ShouldBe(2);
        result.Result.Value.ChangedNotes.ShouldBe(2);
        result.Result.Value.Tracks.Select(track => track.TrackIndex).ShouldBe(new[] { 0, 1 });
    }

    [Fact]
    public void Execute_RejectedWhenTrackIndicesAreMissing()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new BuildAutoEditRecommendationsQuery(),
            EditorQueryContext.Create(session),
            new BuildAutoEditRecommendationsQueryOptions(null!, new MidiForgeAutoEditOptions()));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Choose at least one track.");
        file.IsDirty.ShouldBeFalse();
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
