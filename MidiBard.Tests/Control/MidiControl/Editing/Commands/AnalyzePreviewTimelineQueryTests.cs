using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Preview;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class AnalyzePreviewTimelineQueryTests
{
    [Fact]
    public void EstimateDuration_UsesTimelineMaximumWithoutMutatingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 720)));
        var beforeVersion = file.Version;

        var result = ExecuteDuration(file, new FakePreviewSettings());

        result.Succeeded.ShouldBeTrue();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();

        result.Result!.Value.DurationTicks.ShouldBe(720);
        result.Result.Value.DurationSeconds.ShouldBe(0.75, tolerance: 0.0001);
    }

    [Fact]
    public void AnalyzeRange_UsesTrackNameTransposeAndPreviewSettingsWithoutMutatingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano+1",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120)),
            CreateTrack("Flute",
                Timed(NoteOn(90), 240),
                Timed(NoteOff(90), 360)));
        var beforeVersion = file.Version;

        var result = ExecuteRange(
            file,
            new FakePreviewSettings
            {
                AdaptNotesOOR = false,
            });

        result.Succeeded.ShouldBeTrue();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();

        var analysis = result.Result!.Value;
        analysis.NoteOnEvents.ShouldBe(2);
        analysis.PlayableNoteEvents.ShouldBe(1);
        analysis.OutOfRangeNoteEvents.ShouldBe(1);
        analysis.MinimumMidiNote.ShouldBe(60);
        analysis.MaximumMidiNote.ShouldBe(90);
        analysis.MinimumPreviewMidiNote.ShouldBe(72);
        analysis.MaximumPreviewMidiNote.ShouldBe(90);
        analysis.MinimumGameNote.ShouldBe(24);
        analysis.MaximumGameNote.ShouldBe(42);
        analysis.HasOutOfRangeNotes.ShouldBeTrue();
    }

    [Fact]
    public void AnalyzeRange_AdaptedOutOfRangeNotesAreReportedAsPlayable()
    {
        var file = CreateEditableFile(
            CreateTrack("Flute",
                Timed(NoteOn(90), 0),
                Timed(NoteOff(90), 120)));

        var result = ExecuteRange(
            file,
            new FakePreviewSettings
            {
                AdaptNotesOOR = true,
            });

        result.Succeeded.ShouldBeTrue();

        var analysis = result.Result!.Value;
        analysis.NoteOnEvents.ShouldBe(1);
        analysis.PlayableNoteEvents.ShouldBe(1);
        analysis.OutOfRangeNoteEvents.ShouldBe(0);
        analysis.MinimumGameNote.ShouldBe(30);
        analysis.MaximumGameNote.ShouldBe(30);
    }

    [Fact]
    public void AnalyzeRange_ReturnsEmptyAnalysisWhenTimelineHasNoNotes()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Timed(new ProgramChangeEvent((SevenBitNumber)(byte)2), 0)));

        var result = ExecuteRange(file, new FakePreviewSettings());

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ShouldBe(new PreviewRangeAnalysis(
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null));
    }

    private static PreviewQueryExecutionResult<PreviewDurationEstimate> ExecuteDuration(
        EditableMidiFile file,
        FakePreviewSettings settings)
        => new PreviewQueryExecutor().Execute(
            new EstimatePreviewDurationQuery(),
            CreateContext(file, settings),
            new EstimatePreviewDurationOptions());

    private static PreviewQueryExecutionResult<PreviewRangeAnalysis> ExecuteRange(
        EditableMidiFile file,
        FakePreviewSettings settings)
        => new PreviewQueryExecutor().Execute(
            new AnalyzePreviewRangeQuery(),
            CreateContext(file, settings),
            new AnalyzePreviewRangeOptions());

    private static PreviewQueryContext CreateContext(
        EditableMidiFile file,
        FakePreviewSettings settings)
        => new(
            new PreviewSessionState(),
            file,
            new EditorSelectionSnapshot(-1, [], []),
            settings,
            EmptyEditorPreviewInstrumentCatalog.Instance,
            default);

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string trackName, params TimedEvent[] events)
    {
        var chunk = string.IsNullOrEmpty(trackName)
            ? new TrackChunk()
            : new TrackChunk(new SequenceTrackNameEvent(trackName));

        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in events)
            manager.Objects.Add(timedEvent);

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time, int channel = 0)
    {
        if (midiEvent is ChannelEvent channelEvent)
            channelEvent.Channel = (FourBitNumber)(byte)channel;

        return new TimedEvent(midiEvent, time);
    }

    private static NoteOnEvent NoteOn(int noteNumber, int channel = 0)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)(byte)100)
        {
            Channel = (FourBitNumber)(byte)channel,
        };

    private static NoteOffEvent NoteOff(int noteNumber, int channel = 0)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)(byte)0)
        {
            Channel = (FourBitNumber)(byte)channel,
        };

    private sealed class FakePreviewSettings : IEditorPreviewSettings
    {
        public uint DefaultInstrumentId { get; set; }
        public bool ForceDefaultInstrument { get; set; }
        public GuitarToneMode GuitarToneMode { get; set; } = GuitarToneMode.Off;
        public AntiStackType AntiStackType { get; set; } = AntiStackType.Off;
        public int TransposeGlobal { get; set; }
        public bool AdaptNotesOOR { get; set; } = true;
        public IReadOnlyList<TrackStatus> TrackStatus { get; } =
            Enumerable.Range(0, 100).Select(_ => new TrackStatus()).ToArray();
    }
}
