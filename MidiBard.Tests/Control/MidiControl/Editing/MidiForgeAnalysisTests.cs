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
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)0), 0),
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
        analysis.FirstProgramNumber.ShouldBe(0);
        analysis.ProgramChangeCount.ShouldBe(1);
        analysis.PitchBendCount.ShouldBe(1);
        analysis.ZeroLengthNoteCount.ShouldBe(0);
        analysis.MaxSimultaneousNotes.ShouldBe(2);
        analysis.MaxActiveOverlappingNotes.ShouldBe(2);
        analysis.SuggestedTransposeSemitones.ShouldBe(12);
    }

    [Fact]
    public void AnalyzeTrackChunk_ReportsActiveOverlapAndZeroLengthNotes()
    {
        var chunk = CreateTrack(
            Note(60, 0, 240),
            Note(64, 120, 120),
            Note(67, 240, 120),
            Note(70, 300, 0));

        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, "Piano", 0);

        analysis.MaxSimultaneousNotes.ShouldBe(1);
        analysis.MaxActiveOverlappingNotes.ShouldBe(2);
        analysis.ZeroLengthNoteCount.ShouldBe(1);
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

    [Fact]
    public void GetTrackDiagnostics_ReportsActionableWarnings()
    {
        var chunk = CreateTrack(
            Timed(new PitchBendEvent(8192), 12),
            Note(36, 0, 120),
            Note(40, 0, 120),
            Note(44, 0, 120),
            Note(47, 0, 120));
        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, string.Empty, 0);

        var diagnostics = MidiForgeAnalysis.GetTrackDiagnostics(analysis);

        diagnostics.ShouldContain("Track has no name.");
        diagnostics.ShouldContain("4 note(s) outside C3-C6 (4 below, 0 above).");
        diagnostics.ShouldContain("Suggested transpose: +12 semitone(s).");
        diagnostics.ShouldContain("Max simultaneous notes is 4; FFXIV playback usually needs 3 or fewer.");
        diagnostics.ShouldContain("Contains 1 pitch bend event(s); verify playback result.");
    }

    [Fact]
    public void GetTrackDiagnosticTooltipLines_IncludesBardForgeStyleSummaryAndWarnings()
    {
        var chunk = CreateTrack(
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)0), 0),
            Timed(new PitchBendEvent(8192), 12),
            Note(36, 0, 120),
            Note(40, 0, 120),
            Note(44, 0, 120),
            Note(47, 0, 120),
            Note(60, 240, 0));
        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, string.Empty, 0);

        var tooltip = MidiForgeAnalysis.GetTrackDiagnosticTooltipLines(analysis);

        tooltip.ShouldContain("Notes: 5");
        tooltip.ShouldContain("Pitch bends: 1");
        tooltip.ShouldContain("Program changes: 1");
        tooltip.ShouldContain("Zero-length notes: 1");
        tooltip.ShouldContain("Range: C2-C4 (36-60)");
        tooltip.ShouldContain("Max simultaneous notes: 4");
        tooltip.ShouldContain("Max active overlapping notes: 4");
        tooltip.ShouldContain("Suggested transpose: +12 semitone(s)");
        tooltip.ShouldContain("Warnings:");
        tooltip.ShouldContain("- Track has no name.");
        tooltip.ShouldContain("- Contains 1 zero-length note(s).");
    }

    [Fact]
    public void GetTrackDiagnostics_SuggestsDrumSplitForMultiNoteDrumTrack()
    {
        var chunk = CreateTrack(
            Note(36, 0, 120, channel: 9),
            Note(38, 120, 120, channel: 9));
        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, "Drumkit", 9);

        var diagnostics = MidiForgeAnalysis.GetTrackDiagnostics(analysis);

        diagnostics.ShouldContain("Drum channel has multiple note types; consider Drums > Split Drumkit Tracks.");
        diagnostics.ShouldNotContain("Suggested transpose: +12 semitone(s).");
    }

    [Fact]
    public void GetTrackDiagnostics_IdentifiesConfiguredTrackNameAlias()
    {
        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(CreateTrack(), 0, "Clean", 0);

        var diagnostics = MidiForgeAnalysis.GetTrackDiagnostics(
            analysis,
            DefaultEditorMidiMapProvider.Instance);

        diagnostics.ShouldContain(
            "Track name alias resolves to ElectricGuitarClean; use Track > Map Selected Instruments to apply the canonical name.");
    }

    [Fact]
    public void GetTrackDiagnostics_IdentifiesProgramResolvedGenericTrackName()
    {
        var chunk = CreateTrack(Timed(new ProgramChangeEvent((SevenBitNumber)(byte)52), 0));
        var analysis = MidiForgeAnalysis.AnalyzeTrackChunk(chunk, 0, "Track 01", 0);

        var diagnostics = MidiForgeAnalysis.GetTrackDiagnostics(
            analysis,
            DefaultEditorMidiMapProvider.Instance);

        diagnostics.ShouldContain(
            "Program Change resolves to Panpipes; use Track > Map Selected Instruments to rename this track.");
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
