using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Preview;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class BuildPreviewEventTimelineQueryTests
{
    [Fact]
    public void Execute_BuildsTimelineAndSnapshotsWithoutMutatingFile()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack("Piano",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)2), 0),
                Timed(NoteOn(60), 120),
                Timed(NoteOff(60), 360)));
        var beforeVersion = file.Version;

        var result = Execute(file, new FakePreviewSettings());

        result.Succeeded.ShouldBeTrue();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();

        var timeline = result.Result!.Value;
        timeline.HasNoteEvents.ShouldBeTrue();
        timeline.Events.Select(timelineEvent => timelineEvent.Event.EventType.ToString())
            .ShouldBe(new[]
            {
                MidiEventType.ProgramChange.ToString(),
                MidiEventType.NoteOn.ToString(),
                MidiEventType.NoteOff.ToString(),
            });
        timeline.Events.Select(timelineEvent => timelineEvent.TrackIndex)
            .ShouldBe(new[] { 1, 1, 1 });
        timeline.ProgramEvents.Single().ShouldBe(new PreviewTimelineProgramEvent(0, 1, 0, (SevenBitNumber)(byte)2));
        timeline.EventSnapshots.Select(snapshot => snapshot.EventType)
            .ShouldBe(new[]
            {
                MidiEventType.ProgramChange.ToString(),
                MidiEventType.NoteOn.ToString(),
                MidiEventType.NoteOff.ToString(),
            });
    }

    [Fact]
    public void Execute_UsesLiveEditableNoteDurationWithoutFlushingTrack()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120)));
        var track = file.Tracks.Single();
        track.LoadEvents(file.TempoMap);
        var note = track.Events!.Single(editableEvent => editableEvent.NoteOffSource != null);
        note.EditDuration = 360;
        note.ApplyEditValues();

        var result = Execute(file, new FakePreviewSettings());

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.EventSnapshots.ShouldContain(snapshot =>
            snapshot.EventType == MidiEventType.NoteOff.ToString() &&
            snapshot.EventValue == 60 &&
            snapshot.Time == 360);
    }

    [Theory]
    [InlineData(AntiStackType.Off, 2, new long[] { 120, 240 })]
    [InlineData(AntiStackType.KeepFirstNote, 1, new long[] { 120 })]
    [InlineData(AntiStackType.KeepShortestNote, 1, new long[] { 120 })]
    [InlineData(AntiStackType.KeepLongestNote, 1, new long[] { 240 })]
    public void Execute_AppliesAntiStackSettingToTimelineOnly(
        AntiStackType antiStackType,
        int expectedNoteOnCount,
        long[] expectedNoteOffTimes)
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(NoteOn(60), 0),
                Timed(NoteOn(60), 0),
                Timed(NoteOff(60), 120),
                Timed(NoteOff(60), 240)));

        var result = Execute(file, new FakePreviewSettings { AntiStackType = antiStackType });

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.EventSnapshots
            .Where(snapshot => snapshot.EventType == MidiEventType.NoteOn.ToString())
            .Select(snapshot => snapshot.EventValue)
            .ShouldBe(Enumerable.Repeat(60, expectedNoteOnCount).ToArray());
        result.Result.Value.EventSnapshots
            .Where(snapshot => snapshot.EventType == MidiEventType.NoteOff.ToString())
            .Select(snapshot => snapshot.Time)
            .ShouldBe(expectedNoteOffTimes);
        file.Tracks.Single().Chunk.GetNotes()
            .Select(note => note.EndTime)
            .OrderBy(time => time)
            .ShouldBe(new long[] { 120, 240 });
    }

    [Fact]
    public void Execute_FixesMismatchedNoteOffChannelsInTimelineOnly()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano",
                Timed(NoteOn(60, channel: 0), 0),
                Timed(NoteOff(60, channel: 1), 120, channel: 1)));

        var result = Execute(file, new FakePreviewSettings());

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.EventSnapshots.Single(snapshot =>
            snapshot.EventType == MidiEventType.NoteOff.ToString()).Channel.ShouldBe(0);
        ((NoteOffEvent)file.Tracks.Single().Chunk.GetTimedEvents()
            .Single(timedEvent => timedEvent.Event is NoteOffEvent)
            .Event)
            .Channel
            .ShouldBe((FourBitNumber)1);
    }

    private static PreviewQueryExecutionResult<PreviewEventTimeline> Execute(
        EditableMidiFile file,
        FakePreviewSettings settings)
        => new PreviewQueryExecutor().Execute(
            new BuildPreviewEventTimelineQuery(),
            new PreviewQueryContext(
                new PreviewSessionState(),
                file,
                new EditorSelectionSnapshot(-1, [], []),
                settings,
                EmptyEditorPreviewInstrumentCatalog.Instance,
                default),
            new EditorOperationEmptyOptions());

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
    }
}
