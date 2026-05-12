using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class EditableMidiFileSafetyTests
{
    [Fact]
    public void CloneTrack_IgnoresConductorTrack()
    {
        var file = CreateEditableFile();
        file.Tracks[0].IsConductorTrack.ShouldBeTrue();

        file.CloneTrack(0);

        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void SplitTrackByChannel_IgnoresInvalidIndexes()
    {
        var file = CreateEditableFile();

        file.SplitTrackByChannel(-1);
        file.SplitTrackByChannel(99);

        file.Tracks.Count.ShouldBe(2);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void MergeTracks_PreservesOverlappingDifferentNotes()
    {
        var file = CreateEditableFile(
            CreateTrack(Timed(new SequenceTrackNameEvent("Lead"), 0), Note(60, 0, 480)),
            CreateTrack(Timed(new SequenceTrackNameEvent("Harmony"), 0), Note(64, 120, 120)));

        var mergedIndex = file.MergeTracks(
            0,
            new[] { 0, 1 },
            includeProgramChange: true,
            includePitchBend: true);

        mergedIndex.ShouldBe(1);
        file.Tracks[1].Name.ShouldBe("Lead (merged)");
        file.Tracks[1].Chunk.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 64 });
    }

    [Fact]
    public void MergeTracks_RemoveEqualNotesOnlyRemovesSamePitchAtSameStartTick()
    {
        var file = CreateEditableFile(
            CreateTrack(Timed(new SequenceTrackNameEvent("Lead"), 0), Note(60, 0, 480)),
            CreateTrack(
                Timed(new SequenceTrackNameEvent("Harmony"), 0),
                Note(60, 0, 120),
                Note(64, 0, 120)));

        file.MergeTracks(
            0,
            new[] { 0, 1 },
            includeProgramChange: true,
            includePitchBend: true,
            removeEqualNotes: true);

        file.Tracks[1].Chunk.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .OrderBy(note => note)
            .ShouldBe(new[] { 60, 64 });
    }

    [Fact]
    public void MergeTracks_DeleteOriginalTracks_ReplacesSelectionWithMergedTrack()
    {
        var file = CreateEditableFile(
            CreateTrack(Timed(new SequenceTrackNameEvent("Lead"), 0), Note(60, 0, 120)),
            CreateTrack(Timed(new SequenceTrackNameEvent("Harmony"), 0), Note(64, 120, 120)));

        var mergedIndex = file.MergeTracks(
            0,
            new[] { 0, 1 },
            includeProgramChange: true,
            includePitchBend: true,
            deleteOriginalTracks: true);

        mergedIndex.ShouldBe(0);
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Lead (merged)");
        file.Tracks[0].Chunk.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 64 });
    }

    [Fact]
    public void TransposeTracks_WithNoteRange_OnlyTransposesMatchingNotes()
    {
        var file = CreateEditableFile(CreateTrack(
            Timed(new SequenceTrackNameEvent("Lead"), 0),
            Note(60, 0, 120),
            Note(72, 120, 120)));

        var changedNotes = file.TransposeTracks(
            new[] { 0 },
            semitones: 12,
            minNoteNumber: 61,
            maxNoteNumber: 127);

        changedNotes.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 84 });
    }

    [Fact]
    public void TransposeTracks_ToNewTrack_KeepsOriginalAndRenamesCopy()
    {
        var file = CreateEditableFile(CreateTrack(
            Timed(new SequenceTrackNameEvent("Lead"), 0),
            Note(60, 0, 120)));

        var changedNotes = file.TransposeTracks(
            new[] { 0 },
            semitones: 12,
            toNewTrack: true);

        changedNotes.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Name.ShouldBe("Lead");
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
        file.Tracks[1].Name.ShouldBe("Lead (Transposed 12)");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)72);
    }

    private static EditableMidiFile CreateEditableFile()
    {
        var conductor = CreateTrack(Timed(new SetTempoEvent(500000), 0));
        var performance = CreateTrack(
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
            Note(60, 0, 120));

        var midiFile = new MidiFile();
        midiFile.Chunks.Add(conductor);
        midiFile.Chunks.Add(performance);

        return new EditableMidiFile(midiFile);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(params object[] objects)
    {
        var chunk = new TrackChunk();
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
