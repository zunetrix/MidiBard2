using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.repeat-loop",
    "Repeat Selected Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Arrangement")]
public sealed class RepeatLoopCommand
    : EditorOperationBase, IEditorCommand<RepeatLoopOptions, RepeatLoopResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, RepeatLoopOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.SelectedNotes is null || options.SelectedNotes.Count == 0)
            return EditorCommandValidation.Failure("Select at least one note.");

        if (options.EndCondition == MidiForgeRepeatLoopEndCondition.RepeatCount
            && (options.RepeatCount < 1 || options.RepeatCount > 128))
            return EditorCommandValidation.Failure("Repeat count must be between 1 and 128.");

        if (options.EndCondition == MidiForgeRepeatLoopEndCondition.UntilTick
            && options.EndTick <= 0)
            return EditorCommandValidation.Failure("End tick must be greater than 0.");

        return context.File.Tracks[options.TrackIndex].Events is null
            ? EditorCommandValidation.Failure("Load the track before editing notes.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<RepeatLoopResult> Execute(
        EditorCommandContext context,
        RepeatLoopOptions options)
    {
        var track = context.File.Tracks[options.TrackIndex];
        if (track.Events is null)
            return EditorCommandResult<RepeatLoopResult>.NoChange("Load the track before editing notes.");

        var resolvedNotes = ResolveSelectedNotes(track, options.SelectedNotes).ToArray();
        if (resolvedNotes.Length == 0)
            return EditorCommandResult<RepeatLoopResult>.UnchangedResult(
                new RepeatLoopResult(0, 0, 0, 0));

        var selectionStart = resolvedNotes.Min(n => n.Tick);
        var selectionEnd = resolvedNotes.Max(n => n.Tick + n.DurationTicks);
        var intervalTicks = RepeatLoopHelpers.IntervalToTicks(
            options.Interval, context.File.TempoMap, selectionStart);

        if (intervalTicks <= 0)
            return EditorCommandResult<RepeatLoopResult>.NoChange("Interval resolved to zero ticks.");

        var stopTick = ResolveStopTick(
            context, track, resolvedNotes, options, selectionEnd);

        if (stopTick <= selectionEnd)
            return EditorCommandResult<RepeatLoopResult>.UnchangedResult(
                new RepeatLoopResult(0, 0, 0, 0));

        var existingNotes = BuildExistingNoteMap(track, resolvedNotes);
        var insertedNotes = 0;
        var trimmedNotes = 0;
        var repeatedGroups = 0;
        var lastInsertedTick = selectionStart;

        for (int i = 1; ; i++)
        {
            var offset = (long)i * intervalTicks;
            var repeatStart = selectionStart + offset;

            if (repeatStart >= stopTick)
                break;

            var anyInserted = false;
            foreach (var note in resolvedNotes)
            {
                if (note.Source.Event is not NoteOnEvent noteOn)
                    continue;

                var noteNumber = (byte)noteOn.NoteNumber;
                var velocity = (byte)noteOn.Velocity;
                var newTick = note.Tick + offset;
                if (newTick >= stopTick)
                    continue;

                var newEndTick = newTick + note.DurationTicks;
                if (newEndTick > stopTick)
                    newEndTick = stopTick;

                var newDuration = newEndTick - newTick;
                if (newDuration <= 0)
                    continue;

                if (options.TrimToFit && WouldOverlap(existingNotes, noteNumber, newTick, newEndTick))
                {
                    trimmedNotes++;
                    continue;
                }

                track.InsertNote(newTick, noteNumber, velocity, newDuration);
                AddToExistingNoteMap(existingNotes, noteNumber, newTick, newEndTick);
                insertedNotes++;
                anyInserted = true;
                lastInsertedTick = Math.Max(lastInsertedTick, newTick);
            }

            if (anyInserted)
                repeatedGroups++;
        }

        if (insertedNotes == 0)
            return EditorCommandResult<RepeatLoopResult>.UnchangedResult(
                new RepeatLoopResult(0, 0, trimmedNotes, 0));

        track.FlushChanges();

        var result = new RepeatLoopResult(repeatedGroups, insertedNotes, trimmedNotes, lastInsertedTick);
        return EditorCommandResult<RepeatLoopResult>.ChangedResult(
            result,
            refreshHints: NoteEditCommandHelpers.NoteChangedHints);
    }

    private static long ResolveStopTick(
        EditorCommandContext context,
        EditableTrack track,
        EditableEvent[] resolvedNotes,
        RepeatLoopOptions options,
        long selectionEnd)
    {
        return options.EndCondition switch
        {
            MidiForgeRepeatLoopEndCondition.UntilTick => options.EndTick,
            MidiForgeRepeatLoopEndCondition.RepeatCount
                => resolvedNotes.Max(n => n.Tick) + (long)(options.RepeatCount + 1)
                    * RepeatLoopHelpers.IntervalToTicks(options.Interval, context.File.TempoMap,
                        resolvedNotes.Min(n => n.Tick)),
            MidiForgeRepeatLoopEndCondition.UntilNextNoteOnTrack
                => Math.Min(
                    FindNextNonSelectedNoteTick(track, resolvedNotes, selectionEnd),
                    FindEndOfSongTick(context.File, track)),
            _ => FindEndOfSongTick(context.File, track),
        };
    }

    private static long FindNextNonSelectedNoteTick(
        EditableTrack track,
        EditableEvent[] resolvedNotes,
        long afterTick)
    {
        if (track.Events is null)
            return long.MaxValue;

        var selectedTicks = new HashSet<long>(resolvedNotes.Select(n => n.Tick));
        long minTick = long.MaxValue;

        foreach (var ev in track.Events)
        {
            if (ev.NoteOffSource is null || ev.Source.Event is not NoteOnEvent)
                continue;

            if (ev.Tick > afterTick && !selectedTicks.Contains(ev.Tick))
            {
                minTick = Math.Min(minTick, ev.Tick);
                break;
            }
        }

        return minTick;
    }

    private static long FindEndOfSongTick(EditableMidiFile file, EditableTrack track)
    {
        long maxTick = 0;
        foreach (var t in file.Tracks)
        {
            foreach (var note in t.Chunk.GetNotes())
            {
                var endTick = note.Time + note.Length;
                if (endTick > maxTick)
                    maxTick = endTick;
            }
        }

        return maxTick > 0 ? maxTick : 480 * 4 * 100;
    }

    private static Dictionary<int, List<(long Start, long End)>> BuildExistingNoteMap(
        EditableTrack track,
        EditableEvent[] resolvedNotes)
    {
        var map = new Dictionary<int, List<(long, long)>>();
        var selectedTicks = new HashSet<long>(resolvedNotes.Select(n => n.Tick));

        if (track.Events != null)
        {
            foreach (var ev in track.Events)
            {
                if (ev.NoteOffSource is null || ev.Source.Event is not NoteOnEvent noteOn)
                    continue;

                if (selectedTicks.Contains(ev.Tick))
                    continue;

                var pitch = (byte)noteOn.NoteNumber;
                if (!map.ContainsKey(pitch))
                    map[pitch] = new List<(long, long)>();

                map[pitch].Add((ev.Tick, ev.Tick + ev.DurationTicks));
            }
        }

        return map;
    }

    private static bool WouldOverlap(
        Dictionary<int, List<(long Start, long End)>> existingNotes,
        int noteNumber,
        long newStart,
        long newEnd)
    {
        if (!existingNotes.TryGetValue(noteNumber, out var ranges))
            return false;

        foreach (var (start, end) in ranges)
        {
            if (newStart < end && newEnd > start)
                return true;
        }

        return false;
    }

    private static void AddToExistingNoteMap(
        Dictionary<int, List<(long Start, long End)>> map,
        int noteNumber,
        long start,
        long end)
    {
        if (!map.ContainsKey(noteNumber))
            map[noteNumber] = new List<(long, long)>();

        map[noteNumber].Add((start, end));
    }

    private static IEnumerable<EditableEvent> ResolveSelectedNotes(
        EditableTrack track,
        IReadOnlyList<NoteSelectionKey> selectedNotes)
    {
        var excludedEvents = new List<EditableEvent>();
        foreach (var noteKey in selectedNotes)
        {
            var editableEvent = NoteEditCommandHelpers.ResolveNote(track, noteKey, excludedEvents);
            if (editableEvent?.Source.Event is not NoteOnEvent)
                continue;

            excludedEvents.Add(editableEvent);
            yield return editableEvent;
        }
    }
}

public sealed record RepeatLoopOptions(
    int TrackIndex,
    IReadOnlyList<NoteSelectionKey> SelectedNotes,
    MidiForgeRepeatLoopInterval Interval,
    MidiForgeRepeatLoopEndCondition EndCondition,
    int RepeatCount = 4,
    long EndTick = 0,
    bool TrimToFit = true);

internal static class RepeatLoopHelpers
{
    public static long IntervalToTicks(
        MidiForgeRepeatLoopInterval interval,
        TempoMap tempoMap,
        long atTick)
    {
        var pos = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(atTick, tempoMap);
        long bar = pos.Bars;
        long barTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, 0), tempoMap);
        long nextBeatTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, 1), tempoMap);
        long beatDuration = nextBeatTick - barTick;

        var timeSig = tempoMap.GetTimeSignatureAtTime(
            TimeConverter.ConvertTo<MetricTimeSpan>(barTick, tempoMap));
        int beatsPerBar = timeSig.Numerator;

        return interval switch
        {
            MidiForgeRepeatLoopInterval.HalfBar => beatDuration * beatsPerBar / 2,
            MidiForgeRepeatLoopInterval.OneBar => beatDuration * beatsPerBar,
            MidiForgeRepeatLoopInterval.TwoBars => beatDuration * beatsPerBar * 2,
            MidiForgeRepeatLoopInterval.FourBars => beatDuration * beatsPerBar * 4,
            MidiForgeRepeatLoopInterval.OneBeat => beatDuration,
            MidiForgeRepeatLoopInterval.TwoBeats => beatDuration * 2,
            MidiForgeRepeatLoopInterval.FourBeats => beatDuration * 4,
            _ => beatDuration * beatsPerBar,
        };
    }
}
