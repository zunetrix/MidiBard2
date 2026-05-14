using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Util.MidiPreprocessor;

namespace MidiBard.Control.MidiControl.Editing.Commands.Preview;

public sealed record PreviewEventTimeline(
    IReadOnlyList<PreviewTimelineEvent> Events,
    IReadOnlyList<PreviewTimelineProgramEvent> ProgramEvents,
    IReadOnlyList<PreviewTimelineEventSnapshot> EventSnapshots,
    TempoMap TempoMap,
    bool HasNoteEvents);

public sealed record PreviewTimelineEvent(
    int TrackIndex,
    MidiEvent Event,
    long Time,
    double TimeSeconds,
    int EventValue);

public readonly record struct PreviewTimelineProgramEvent(
    double TimeSeconds,
    int TrackIndex,
    int Channel,
    Melanchall.DryWetMidi.Common.SevenBitNumber Program);

public readonly record struct PreviewTimelineEventSnapshot(
    int TrackIndex,
    long Time,
    string EventType,
    int Channel,
    int EventValue,
    int? ProgramNumber = null);

[EditorOperation(
    "preview.build-event-timeline",
    "Build Preview Event Timeline",
    Kind = EditorOperationKind.PreviewQuery,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class BuildPreviewEventTimelineQuery
    : EditorOperationBase, IPreviewQuery<EditorOperationEmptyOptions, PreviewEventTimeline>
{
    public EditorCommandValidation Validate(PreviewQueryContext context, EditorOperationEmptyOptions options)
        => EditorCommandValidation.Success;

    public PreviewQueryResult<PreviewEventTimeline> Execute(
        PreviewQueryContext context,
        EditorOperationEmptyOptions options)
    {
        var timeline = PreviewEventTimelinePrimitives.BuildTimeline(
            context.File,
            context.Settings.AntiStackType);

        return new PreviewQueryResult<PreviewEventTimeline>(timeline);
    }
}

public static class PreviewEventTimelinePrimitives
{
    public static PreviewEventTimeline BuildTimeline(
        EditableMidiFile file,
        AntiStackType antiStackType)
    {
        var chunks = BuildPlaybackTrackChunks(file);
        var snapshot = new MidiFile(chunks)
        {
            TimeDivision = file.Source.TimeDivision,
        };

        if (antiStackType != AntiStackType.Off)
            MidiPreprocessor.RemoveStackedNotes(snapshot, antiStackType);

        var tempoMap = snapshot.GetTempoMap();
        var events = new List<PreviewTimelineEvent>();
        var programEvents = new List<PreviewTimelineProgramEvent>();
        var eventSnapshots = new List<PreviewTimelineEventSnapshot>();

        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            if (file.Tracks[trackIndex].IsConductorTrack)
                continue;

            foreach (var timedEvent in chunks[trackIndex].GetTimedEvents())
            {
                if (!TryCreateTimelineEvent(
                        trackIndex,
                        timedEvent,
                        tempoMap,
                        out var timelineEvent,
                        out var programEvent))
                {
                    continue;
                }

                events.Add(timelineEvent);
                eventSnapshots.Add(CreateEventSnapshot(timelineEvent));
                if (programEvent.HasValue)
                    programEvents.Add(programEvent.Value);
            }
        }

        return new PreviewEventTimeline(
            events
                .OrderBy(timelineEvent => timelineEvent.Time)
                .ThenBy(timelineEvent => timelineEvent.EventValue)
                .ToArray(),
            programEvents
                .OrderBy(programEvent => programEvent.TimeSeconds)
                .ThenBy(programEvent => programEvent.TrackIndex)
                .ToArray(),
            eventSnapshots.ToArray(),
            tempoMap,
            events.Any(timelineEvent =>
                timelineEvent.Event is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0));
    }

    private static TrackChunk[] BuildPlaybackTrackChunks(EditableMidiFile file)
    {
        var chunks = new TrackChunk[file.Tracks.Count];
        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            chunks[trackIndex] = BuildPlaybackTrackChunk(file.Tracks[trackIndex]);
            MidiPreprocessor.FixNoteOffChannels(chunks[trackIndex]);
        }

        return chunks;
    }

    private static TrackChunk BuildPlaybackTrackChunk(EditableTrack track)
    {
        if (track.Events == null)
            return new TrackChunk(track.Chunk.Events.Select(ev => ev.Clone()));

        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in EnumerateLiveTimedEvents(track))
            manager.Objects.Add(CloneTimedEvent(timedEvent));

        return chunk;
    }

    private static IEnumerable<TimedEvent> EnumerateLiveTimedEvents(EditableTrack track)
    {
        foreach (var editableEvent in track.Events!)
        {
            yield return editableEvent.Source;
            if (editableEvent.NoteOffSource != null)
                yield return editableEvent.NoteOffSource;
        }
    }

    private static TimedEvent CloneTimedEvent(TimedEvent timedEvent)
        => new(timedEvent.Event.Clone(), timedEvent.Time);

    private static bool TryCreateTimelineEvent(
        int trackIndex,
        TimedEvent timedEvent,
        TempoMap tempoMap,
        out PreviewTimelineEvent timelineEvent,
        out PreviewTimelineProgramEvent? programEvent)
    {
        timelineEvent = null;
        programEvent = null;

        if (!TryGetEventInfo(timedEvent.Event, out var channel, out var eventValue))
            return false;

        var seconds = TimeConverter.ConvertTo<MetricTimeSpan>(timedEvent.Time, tempoMap)
            .TotalMicroseconds / 1_000_000.0;
        if (timedEvent.Event is ProgramChangeEvent programChange)
        {
            programEvent = new PreviewTimelineProgramEvent(
                seconds,
                trackIndex,
                channel,
                programChange.ProgramNumber);
        }

        timelineEvent = new PreviewTimelineEvent(
            trackIndex,
            timedEvent.Event,
            timedEvent.Time,
            seconds,
            eventValue);
        return true;
    }

    private static bool TryGetEventInfo(MidiEvent midiEvent, out int channel, out int eventValue)
    {
        channel = 0;
        eventValue = -1;

        switch (midiEvent)
        {
            case ProgramChangeEvent programChange:
                channel = (byte)programChange.Channel;
                eventValue = -2;
                return true;
            case NoteOffEvent noteOff:
                channel = (byte)noteOff.Channel;
                eventValue = (byte)noteOff.NoteNumber;
                return true;
            case NoteOnEvent noteOn:
                channel = (byte)noteOn.Channel;
                eventValue = (byte)noteOn.NoteNumber;
                return true;
            default:
                return false;
        }
    }

    private static PreviewTimelineEventSnapshot CreateEventSnapshot(PreviewTimelineEvent timelineEvent)
    {
        TryGetEventInfo(timelineEvent.Event, out var channel, out var eventValue);
        var programNumber = timelineEvent.Event is ProgramChangeEvent programChange
            ? (int)(byte)programChange.ProgramNumber
            : (int?)null;

        return new PreviewTimelineEventSnapshot(
            timelineEvent.TrackIndex,
            timelineEvent.Time,
            timelineEvent.Event.EventType.ToString(),
            channel,
            eventValue,
            programNumber);
    }
}
