using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Preview;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class BuildPreviewSnapshotQueryTests
{
    [Fact]
    public void Execute_BuildsTrackNoteSnapshotsWithoutMutatingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano",
                Note(60, 0, 480),
                Note(64, 480, 240)));
        var beforeVersion = file.Version;

        var result = Execute(file);

        result.Succeeded.ShouldBeTrue();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();

        var snapshot = result.Result!.Value;
        snapshot.Tracks.Count.ShouldBe(2);
        snapshot.MaxTimeSeconds.ShouldBe(0.75, tolerance: 0.0001);

        snapshot.Tracks[0].TrackIndex.ShouldBe(0);
        snapshot.Tracks[0].IsConductorTrack.ShouldBeTrue();
        snapshot.Tracks[0].Notes.Count.ShouldBe(0);

        snapshot.Tracks[1].TrackIndex.ShouldBe(1);
        snapshot.Tracks[1].IsConductorTrack.ShouldBeFalse();
        snapshot.Tracks[1].Notes.Select(note => note.NoteNumber)
            .ShouldBe(new[] { 60, 64 });
        snapshot.Tracks[1].Notes.Select(note => (note.StartSeconds, note.EndSeconds))
            .ShouldBe(new[] { (0.0, 0.5), (0.5, 0.75) });
    }

    [Fact]
    public void Execute_UsesDefaultDurationWhenFileHasNoNotes()
    {
        var file = CreateEditableFile(CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)));

        var result = Execute(file);

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.MaxTimeSeconds.ShouldBe(10);
        result.Result.Value.Tracks.Count.ShouldBe(1);
        result.Result.Value.Tracks[0].IsConductorTrack.ShouldBeTrue();
        result.Result.Value.Tracks[0].Notes.Count.ShouldBe(0);
    }

    private static PreviewQueryExecutionResult<PreviewSnapshot> Execute(EditableMidiFile file)
        => new PreviewQueryExecutor().Execute(
            new BuildPreviewSnapshotQuery(),
            CreateContext(file),
            new EditorOperationEmptyOptions());

    private static PreviewQueryContext CreateContext(EditableMidiFile file)
        => new(
            new PreviewSessionState(),
            file,
            new EditorSelectionSnapshot(-1, [], []),
            EmptyEditorPreviewSettings.Instance,
            EmptyEditorPreviewInstrumentCatalog.Instance,
            default);

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
