using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

using MidiBard.Util.MidiPreprocessor;

namespace MidiBard.Control.MidiControl.Editing.Commands.Preview;

public sealed record EstimatePreviewDurationOptions(
    PreviewEventTimeline Timeline = null);

public readonly record struct PreviewDurationEstimate(
    double DurationSeconds,
    long DurationTicks);

public sealed record AnalyzePreviewRangeOptions(
    PreviewEventTimeline Timeline = null);

public sealed record PreviewRangeAnalysis(
    int NoteOnEvents,
    int PlayableNoteEvents,
    int OutOfRangeNoteEvents,
    int? MinimumMidiNote,
    int? MaximumMidiNote,
    int? MinimumPreviewMidiNote,
    int? MaximumPreviewMidiNote,
    int? MinimumGameNote,
    int? MaximumGameNote)
{
    public bool HasNotes => NoteOnEvents > 0;
    public bool HasOutOfRangeNotes => OutOfRangeNoteEvents > 0;
}

[EditorOperation(
    "preview.estimate-duration",
    "Estimate Preview Duration",
    Kind = EditorOperationKind.PreviewQuery,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class EstimatePreviewDurationQuery
    : EditorOperationBase, IPreviewQuery<EstimatePreviewDurationOptions, PreviewDurationEstimate>
{
    public EditorCommandValidation Validate(
        PreviewQueryContext context,
        EstimatePreviewDurationOptions options)
        => EditorCommandValidation.Success;

    public PreviewQueryResult<PreviewDurationEstimate> Execute(
        PreviewQueryContext context,
        EstimatePreviewDurationOptions options)
    {
        var timeline = options.Timeline
            ?? PreviewEventTimelinePrimitives.BuildTimeline(context.File, context.Settings.AntiStackType);

        return new PreviewQueryResult<PreviewDurationEstimate>(
            PreviewTimelineAnalysisPrimitives.EstimateDuration(timeline));
    }
}

[EditorOperation(
    "preview.analyze-range",
    "Analyze Preview Range",
    Kind = EditorOperationKind.PreviewQuery,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class AnalyzePreviewRangeQuery
    : EditorOperationBase, IPreviewQuery<AnalyzePreviewRangeOptions, PreviewRangeAnalysis>
{
    public EditorCommandValidation Validate(
        PreviewQueryContext context,
        AnalyzePreviewRangeOptions options)
        => EditorCommandValidation.Success;

    public PreviewQueryResult<PreviewRangeAnalysis> Execute(
        PreviewQueryContext context,
        AnalyzePreviewRangeOptions options)
    {
        var timeline = options.Timeline
            ?? PreviewEventTimelinePrimitives.BuildTimeline(context.File, context.Settings.AntiStackType);

        return new PreviewQueryResult<PreviewRangeAnalysis>(
            PreviewTimelineAnalysisPrimitives.AnalyzeRange(context.File, timeline, context.Settings));
    }
}

public static class PreviewTimelineAnalysisPrimitives
{
    public static PreviewDurationEstimate EstimateDuration(PreviewEventTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        if (timeline.Events.Count == 0)
            return new PreviewDurationEstimate(0, 0);

        return new PreviewDurationEstimate(
            Math.Max(0.0, timeline.Events.Max(timelineEvent => timelineEvent.TimeSeconds)),
            Math.Max(0, timeline.Events.Max(timelineEvent => timelineEvent.Time)));
    }

    public static PreviewRangeAnalysis AnalyzeRange(
        EditableMidiFile file,
        PreviewEventTimeline timeline,
        IEditorPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(settings);

        var transposes = file.Tracks
            .Select(track => TrackInfo.GetTransposeByName(track.Name))
            .ToArray();
        var notes = EnumeratePreviewNotes(timeline, transposes, settings).ToArray();

        if (notes.Length == 0)
        {
            return new PreviewRangeAnalysis(
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        return new PreviewRangeAnalysis(
            notes.Length,
            notes.Count(note => IsPlayableGameNote(note.GameNote)),
            notes.Count(note => !IsPlayableGameNote(note.GameNote)),
            notes.Min(note => note.MidiNote),
            notes.Max(note => note.MidiNote),
            notes.Min(note => note.PreviewMidiNote),
            notes.Max(note => note.PreviewMidiNote),
            notes.Min(note => note.GameNote),
            notes.Max(note => note.GameNote));
    }

    private static IEnumerable<PreviewNoteRangeItem> EnumeratePreviewNotes(
        PreviewEventTimeline timeline,
        IReadOnlyList<int> trackTransposes,
        IEditorPreviewSettings settings)
    {
        foreach (var timelineEvent in timeline.Events)
        {
            if (timelineEvent.Event is not NoteOnEvent noteOn || (byte)noteOn.Velocity == 0)
                continue;

            var midiNote = (byte)noteOn.NoteNumber;
            var transpose = (uint)timelineEvent.TrackIndex < (uint)trackTransposes.Count
                ? trackTransposes[timelineEvent.TrackIndex]
                : 0;
            var previewMidiNote = midiNote + transpose;
            var gameNote = TrackInfo.TranslateNoteNumber(
                previewMidiNote,
                settings.TransposeGlobal,
                settings.AdaptNotesOOR);

            yield return new PreviewNoteRangeItem(midiNote, previewMidiNote, gameNote);
        }
    }

    private static bool IsPlayableGameNote(int gameNote)
        => gameNote is >= 0 and <= 36;

    private readonly record struct PreviewNoteRangeItem(
        int MidiNote,
        int PreviewMidiNote,
        int GameNote);
}
