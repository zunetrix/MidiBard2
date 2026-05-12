using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeAnalysisTests
{
    [Fact]
    public void AnalyzeTrackChunk_ReportsBardForgeStyleDiagnostics()
    {
        var chunk = CreateTrack(
            Timed(new PitchBendEvent(8192), 12),
            Note(36, 0, 120),
            Note(40, 0, 120),
            Note(60, 240, 120));

        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 3, "Piano", 0);

        analysis.TrackIndex.ShouldBe(3);
        analysis.TrackName.ShouldBe("Piano");
        analysis.Channel.ShouldBe(0);
        analysis.IsConductorTrack.ShouldBeFalse();
        analysis.IsDrumTrack.ShouldBeFalse();
        analysis.NoteCount.ShouldBe(3);
        analysis.UniqueNoteCount.ShouldBe(3);
        analysis.LowestNote.ShouldBe(36);
        analysis.HighestNote.ShouldBe(60);
        analysis.OutOfRangeBelowCount.ShouldBe(2);
        analysis.OutOfRangeAboveCount.ShouldBe(0);
        analysis.HasOutOfRangeNotes.ShouldBeTrue();
        analysis.PitchBendCount.ShouldBe(1);
        analysis.MaxSimultaneousNotes.ShouldBe(2);
        analysis.SuggestedTransposeSemitones.ShouldBe(12);
    }

    [Fact]
    public void AnalyzeTrackChunk_SuggestsDownOctaveForMostlyHighNotes()
    {
        var chunk = CreateTrack(
            Note(60, 0, 120),
            Note(96, 120, 120),
            Note(100, 240, 120));

        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, channel: 0);

        analysis.OutOfRangeAboveCount.ShouldBe(2);
        analysis.SuggestedTransposeSemitones.ShouldBe(-12);
    }

    [Fact]
    public void AnalyzeTrackChunk_DoesNotSuggestTransposeForDrumChannel()
    {
        var chunk = CreateTrack(
            Note(36, 0, 120, channel: 9),
            Note(40, 120, 120, channel: 9));

        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, "Drumkit", 9);

        analysis.IsDrumTrack.ShouldBeTrue();
        analysis.SuggestedTransposeSemitones.ShouldBe(0);
    }

    [Fact]
    public void AnalyzeTrackChunk_HandlesConductorTrackWithoutNotes()
    {
        var chunk = CreateTrack(Timed(new SetTempoEvent(500000), 0));

        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0);

        analysis.IsConductorTrack.ShouldBeTrue();
        analysis.HasNotes.ShouldBeFalse();
        analysis.NoteCount.ShouldBe(0);
        analysis.LowestNote.ShouldBeNull();
        analysis.HighestNote.ShouldBeNull();
        analysis.MaxSimultaneousNotes.ShouldBe(0);
    }

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
