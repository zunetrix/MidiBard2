using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

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
    public void SplitTrackByChannel_SplitsPerformanceTrackAndMovesTempoToConductor()
    {
        var file = CreateEditableFile(CreateTrack(
            Timed(new SequenceTrackNameEvent("Layer"), 0),
            Timed(new SetTempoEvent(500000), 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
            Note(60, 120, 120, channel: 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)1) { Channel = (FourBitNumber)2 }, 0),
            Note(64, 240, 120, channel: 2)));

        file.SplitTrackByChannel(0);

        file.Tracks.Count.ShouldBe(3);
        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        file.Tracks[0].Chunk.Events.OfType<SetTempoEvent>().Count().ShouldBe(1);
        file.Tracks[1].Name.ShouldBe("Acoustic Grand Piano");
        file.Tracks[2].Name.ShouldBe("Bright Acoustic Piano");
        file.Tracks[1].Chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Single().ShouldBe((byte)0);
        file.Tracks[2].Chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Single().ShouldBe((byte)1);
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
        file.Tracks[2].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)64);
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void ImportTracksFromFile_ScalesTicksWhenPpqDiffers()
    {
        var file = CreateEditableFile(CreateTrack("Base", Note(60, 0, 120)));
        var imported = CreateMidiFile(
            960,
            CreateTrack("Imported",
                Timed(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)2 }, 0),
                Note(64, 960, 480, channel: 2)));

        var importedCount = file.ImportTracksFromFile(imported);

        importedCount.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        var importedNote = file.Tracks[1].Chunk.GetNotes().Single();
        importedNote.Time.ShouldBe(480);
        importedNote.Length.ShouldBe(240);
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void ImportTracksFromFile_ReusesExistingProgramChannelAndPreservesDrumChannel()
    {
        var file = CreateEditableFile(CreateTrack("Violin",
            Timed(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)5 }, 0),
            Note(60, 0, 120, channel: 5)));
        var imported = CreateMidiFile(
            480,
            CreateTrack("Imported Violin",
                Timed(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)2 }, 0),
                Note(64, 120, 120, channel: 2)),
            CreateTrack("Imported Drums",
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }, 0),
                Note(36, 240, 120, channel: 9)));

        var importedCount = file.ImportTracksFromFile(imported);

        importedCount.ShouldBe(2);
        file.Tracks[1].Chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Single().ShouldBe((byte)5);
        file.Tracks[2].Chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Single().ShouldBe((byte)9);
    }

    [Fact]
    public void ImportTracksFromFile_SkipsConductorMetaOnlyAndVelocityZeroTracks()
    {
        var file = CreateEditableFile(CreateTrack("Base", Note(60, 0, 120)));
        var imported = CreateMidiFile(
            480,
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Meta", Timed(new MarkerEvent("skip"), 0)),
            CreateTrack("Velocity Zero", Timed(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)0), 120)),
            CreateTrack("Playable", Note(67, 240, 120)));

        var importedCount = file.ImportTracksFromFile(imported);

        importedCount.ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[1].Name.ShouldBe("Playable");
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)67);
    }

    [Fact]
    public void MergeMultipleConductorTracks_PreservesConductorEventsAndRemovesExtras()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor A",
                Timed(new SetTempoEvent(500000), 0),
                Timed(new TimeSignatureEvent(4, 2), 0)),
            CreateTrack("Conductor B", Timed(new SetTempoEvent(600000), 240)),
            CreateTrack("Piano", Note(60, 0, 120)));

        file.MergeMultipleConductorTracks();

        file.Tracks.Count(track => track.IsConductorTrack).ShouldBe(1);
        file.Tracks.Count.ShouldBe(2);
        file.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is SetTempoEvent)
            .Select(timedEvent => timedEvent.Time)
            .OrderBy(time => time)
            .ShouldBe(new long[] { 0, 240 });
        file.Tracks[0].Chunk.Events.OfType<TimeSignatureEvent>().Count().ShouldBe(1);
        file.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void QuantizeTracks_InvalidAndConductorOnlySelection_DoesNotDirtyFile()
    {
        var file = CreateEditableFile();
        var settings = new QuantizingSettings
        {
            Target = QuantizerTarget.Start,
            QuantizingLevel = 1.0,
        };

        file.QuantizeTracks(
            new[] { -1, 0 },
            new SteppedGrid(MusicalTimeSpan.Quarter),
            settings,
            toNewTrack: false);

        file.IsDirty.ShouldBeFalse();
        file.Tracks.Count.ShouldBe(2);
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

    private static MidiFile CreateMidiFile(int ppq, params TrackChunk[] chunks)
        => new(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision((short)ppq),
        };

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

    private static TrackChunk CreateTrack(string name, params object[] objects)
        => CreateTrack(new object[] { Timed(new SequenceTrackNameEvent(name), 0) }.Concat(objects).ToArray());

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
