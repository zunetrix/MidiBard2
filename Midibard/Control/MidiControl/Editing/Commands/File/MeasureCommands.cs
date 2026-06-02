using System;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.File;

public sealed record InsertMeasuresOptions(
    int AfterMeasureNumber,
    int MeasureCount,
    bool ShiftTempoEvents = true,
    bool ShiftTimeSigEvents = true);

public sealed record DeleteMeasuresOptions(
    int StartMeasureNumber,
    int MeasureCount,
    bool ShiftTempoEvents = true,
    bool ShiftTimeSigEvents = true);

public sealed record InsertMeasuresResult(
    int InsertedMeasures,
    long InsertionTick,
    long ShiftedTickDelta,
    int ShiftedNoteEvents,
    int ShiftedMetaEvents);

public sealed record DeleteMeasuresResult(
    int DeletedMeasures,
    long DeletedTickStart,
    long DeletedTickEnd,
    int RemovedNoteEvents,
    int RemovedMetaEvents,
    int ShiftedNoteEvents,
    int ShiftedMetaEvents);

[EditorOperation(
    "file.insert-measures",
    "Insert Measures",
    Scope = EditorOperationScope.File,
    MenuPath = "Edit/Measures")]
public sealed class InsertMeasuresCommand
    : EditorOperationBase, IEditorCommand<InsertMeasuresOptions, InsertMeasuresResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, InsertMeasuresOptions options)
    {
        if (options.MeasureCount < 1 || options.MeasureCount > 256)
            return EditorCommandValidation.Failure("Measure count must be between 1 and 256.");

        if (options.AfterMeasureNumber < 0)
            return EditorCommandValidation.Failure("Measure number must be 0 or greater.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<InsertMeasuresResult> Execute(
        EditorCommandContext context,
        InsertMeasuresOptions options)
    {
        var file = context.File;
        var tempoMap = file.TempoMap;
        var insertionTick = GetMeasureStartTick(options.AfterMeasureNumber, tempoMap);
        var shiftDelta = GetMeasureDurationTicks(insertionTick, options.MeasureCount, tempoMap);

        if (shiftDelta <= 0)
            return EditorCommandResult<InsertMeasuresResult>.NoChange("Could not compute measure duration.");

        var shiftedNotes = 0;
        var shiftedMeta = 0;

        foreach (var track in file.Tracks)
        {
            if (track.Events is null)
                continue;

            var eventsToShift = track.Events
                .Where(ev => ev.Tick >= insertionTick)
                .ToArray();

            foreach (var ev in eventsToShift)
            {
                if (ev.Source.Event is NoteOnEvent || ev.Source.Event is NoteOffEvent
                    || ev.Source.Event is ProgramChangeEvent || ev.Source.Event is PitchBendEvent)
                {
                    ev.EditTick = (int)(ev.Tick + shiftDelta);
                    ev.ApplyEditValues();
                    shiftedNotes++;
                }
                else if (options.ShiftTempoEvents && ev.Source.Event is SetTempoEvent
                    || options.ShiftTimeSigEvents && ev.Source.Event is TimeSignatureEvent)
                {
                    ev.EditTick = (int)(ev.Tick + shiftDelta);
                    ev.ApplyEditValues();
                    shiftedMeta++;
                }
                else if (ev.Source.Event is BaseTextEvent || ev.Source.Event is KeySignatureEvent)
                {
                    ev.EditTick = (int)(ev.Tick + shiftDelta);
                    ev.ApplyEditValues();
                    shiftedMeta++;
                }
            }
        }

        file.MarkChanged();

        var result = new InsertMeasuresResult(
            options.MeasureCount,
            insertionTick,
            shiftDelta,
            shiftedNotes,
            shiftedMeta);

        return EditorCommandResult<InsertMeasuresResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadEventList: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }

    internal static long GetMeasureStartTick(int measureNumber, TempoMap tempoMap)
    {
        if (measureNumber <= 0)
            return 0;

        try
        {
            var ppqn = ((TicksPerQuarterNoteTimeDivision)tempoMap.TimeDivision).TicksPerQuarterNote;
            var timeSig = tempoMap.GetTimeSignatureAtTime(new MetricTimeSpan(0));
            long ticksPerMeasure = (long)ppqn * timeSig.Numerator;
            return ticksPerMeasure * measureNumber;
        }
        catch
        {
            return 0;
        }
    }

    internal static long GetMeasureDurationTicks(long fromTick, int measureCount, TempoMap tempoMap)
    {
        if (measureCount <= 0)
            return 0;

        try
        {
            var ppqn = ((TicksPerQuarterNoteTimeDivision)tempoMap.TimeDivision).TicksPerQuarterNote;
            var timeSigAtStart = tempoMap.GetTimeSignatureAtTime(
                TimeConverter.ConvertTo<MetricTimeSpan>(fromTick, tempoMap));
            long ticksPerMeasure = (long)ppqn * timeSigAtStart.Numerator;
            return ticksPerMeasure * measureCount;
        }
        catch
        {
            return 0;
        }
    }
}

[EditorOperation(
    "file.delete-measures",
    "Delete Measures",
    Scope = EditorOperationScope.File,
    MenuPath = "Edit/Measures")]
public sealed class DeleteMeasuresCommand
    : EditorOperationBase, IEditorCommand<DeleteMeasuresOptions, DeleteMeasuresResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DeleteMeasuresOptions options)
    {
        if (options.MeasureCount < 1 || options.MeasureCount > 256)
            return EditorCommandValidation.Failure("Measure count must be between 1 and 256.");

        if (options.StartMeasureNumber < 1)
            return EditorCommandValidation.Failure("Start measure must be 1 or greater.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<DeleteMeasuresResult> Execute(
        EditorCommandContext context,
        DeleteMeasuresOptions options)
    {
        var file = context.File;
        var tempoMap = file.TempoMap;
        var deleteStartTick = InsertMeasuresCommand.GetMeasureStartTick(
            options.StartMeasureNumber - 1, tempoMap);
        var deleteEndTick = InsertMeasuresCommand.GetMeasureStartTick(
            options.StartMeasureNumber - 1 + options.MeasureCount, tempoMap);
        var shiftDelta = deleteEndTick - deleteStartTick;

        if (shiftDelta <= 0)
            return EditorCommandResult<DeleteMeasuresResult>.NoChange("Could not compute measure range.");

        var removedNotes = 0;
        var removedMeta = 0;
        var shiftedNotes = 0;
        var shiftedMeta = 0;

        foreach (var track in file.Tracks)
        {
            if (track.Events is null)
                continue;

            var eventsToRemove = track.Events
                .Where(ev => ev.Tick >= deleteStartTick && ev.Tick < deleteEndTick)
                .ToArray();

            foreach (var ev in eventsToRemove)
            {
                if (ev.Source.Event is NoteOnEvent || ev.Source.Event is NoteOffEvent
                    || ev.Source.Event is ProgramChangeEvent || ev.Source.Event is PitchBendEvent)
                {
                    track.RemoveEvent(ev);
                    removedNotes++;
                }
                else if (options.ShiftTempoEvents && ev.Source.Event is SetTempoEvent
                    || options.ShiftTimeSigEvents && ev.Source.Event is TimeSignatureEvent
                    || ev.Source.Event is BaseTextEvent
                    || ev.Source.Event is KeySignatureEvent)
                {
                    track.RemoveEvent(ev);
                    removedMeta++;
                }
            }

            var eventsToShift = track.Events
                .Where(ev => ev.Tick >= deleteEndTick)
                .ToArray();

            foreach (var ev in eventsToShift)
            {
                if (ev.Source.Event is NoteOnEvent || ev.Source.Event is NoteOffEvent
                    || ev.Source.Event is ProgramChangeEvent || ev.Source.Event is PitchBendEvent)
                {
                    ev.EditTick = (int)(ev.Tick - shiftDelta);
                    ev.ApplyEditValues();
                    shiftedNotes++;
                }
                else if (options.ShiftTempoEvents && ev.Source.Event is SetTempoEvent
                    || options.ShiftTimeSigEvents && ev.Source.Event is TimeSignatureEvent
                    || ev.Source.Event is BaseTextEvent
                    || ev.Source.Event is KeySignatureEvent)
                {
                    ev.EditTick = (int)(ev.Tick - shiftDelta);
                    ev.ApplyEditValues();
                    shiftedMeta++;
                }
            }
        }

        file.MarkChanged();

        var result = new DeleteMeasuresResult(
            options.MeasureCount,
            deleteStartTick,
            deleteEndTick,
            removedNotes,
            removedMeta,
            shiftedNotes,
            shiftedMeta);

        return EditorCommandResult<DeleteMeasuresResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadEventList: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}
