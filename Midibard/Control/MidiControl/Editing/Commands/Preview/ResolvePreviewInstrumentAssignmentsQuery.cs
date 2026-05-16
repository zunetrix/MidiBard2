using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Preview;

public sealed record ResolvePreviewInstrumentAssignmentsOptions(
    double PositionSeconds = 0);

public sealed record PreviewInstrumentAssignments(
    IReadOnlyList<PreviewTrackInstrumentAssignment> Tracks);

public sealed record PreviewTrackInstrumentAssignment(
    int TrackIndex,
    string TrackName,
    int Transpose,
    uint? BaseInstrumentId,
    bool IsProgramElectricGuitar,
    IReadOnlyList<uint?> GuitarToneChannelInstrumentIds,
    IReadOnlyList<uint?> ResolvedChannelInstrumentIds)
{
    public uint? GetResolvedInstrumentId(int channel)
        => (uint)channel < (uint)ResolvedChannelInstrumentIds.Count
            ? ResolvedChannelInstrumentIds[channel]
            : BaseInstrumentId;
}

[EditorOperation(
    "preview.resolve-instruments",
    "Resolve Preview Instruments",
    Kind = EditorOperationKind.PreviewQuery,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class ResolvePreviewInstrumentAssignmentsQuery
    : EditorOperationBase, IPreviewQuery<ResolvePreviewInstrumentAssignmentsOptions, PreviewInstrumentAssignments>
{
    public EditorCommandValidation Validate(
        PreviewQueryContext context,
        ResolvePreviewInstrumentAssignmentsOptions options)
        => EditorCommandValidation.Success;

    public PreviewQueryResult<PreviewInstrumentAssignments> Execute(
        PreviewQueryContext context,
        ResolvePreviewInstrumentAssignmentsOptions options)
    {
        var file = context.File;
        var trackStates = file.Tracks
            .Select((track, index) => PreviewInstrumentResolutionPrimitives.CreateTrackState(
                index,
                track.Name,
                context.Settings,
                context.InstrumentCatalog))
            .ToArray();

        ApplyProgramChangesThroughPosition(
            file,
            trackStates,
            options.PositionSeconds,
            context.Settings,
            context.InstrumentCatalog);

        var assignments = trackStates
            .Select(state => PreviewInstrumentResolutionPrimitives.CreateAssignment(
                state,
                context.Settings,
                context.InstrumentCatalog))
            .ToArray();

        return new PreviewQueryResult<PreviewInstrumentAssignments>(
            new PreviewInstrumentAssignments(assignments));
    }

    private static void ApplyProgramChangesThroughPosition(
        EditableMidiFile file,
        IReadOnlyList<PreviewInstrumentTrackState> trackStates,
        double positionSeconds,
        IEditorPreviewSettings settings,
        IEditorPreviewInstrumentCatalog instrumentCatalog)
    {
        var tempoMap = file.TempoMap;
        var programEvents = new List<(double TimeSeconds, int TrackIndex, int Channel, SevenBitNumber Program)>();

        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            var track = file.Tracks[trackIndex];
            if (track.IsConductorTrack)
                continue;

            foreach (var timedEvent in track.Chunk.GetTimedEvents())
            {
                if (timedEvent.Event is not ProgramChangeEvent programChange)
                    continue;

                var timeSeconds = TimeConverter.ConvertTo<MetricTimeSpan>(timedEvent.Time, tempoMap)
                    .TotalMicroseconds / 1_000_000.0;
                if (timeSeconds > positionSeconds)
                    continue;

                programEvents.Add((
                    timeSeconds,
                    trackIndex,
                    (byte)programChange.Channel,
                    programChange.ProgramNumber));
            }
        }

        foreach (var programEvent in programEvents
            .OrderBy(programEvent => programEvent.TimeSeconds)
            .ThenBy(programEvent => programEvent.TrackIndex))
        {
            if ((uint)programEvent.TrackIndex >= (uint)trackStates.Count)
                continue;

            PreviewInstrumentResolutionPrimitives.ApplyProgramChange(
                trackStates[programEvent.TrackIndex],
                programEvent.Channel,
                programEvent.Program,
                settings);
        }
    }
}

public sealed class PreviewInstrumentTrackState
{
    public int TrackIndex { get; init; }
    public string TrackName { get; init; } = string.Empty;
    public int Transpose { get; init; }
    public uint? BaseInstrumentId { get; init; }
    public bool IsProgramElectricGuitar { get; init; }
    public uint?[] GuitarToneChannelInstrumentIds { get; } = new uint?[16];
}

public static class PreviewInstrumentResolutionPrimitives
{
    public static PreviewInstrumentTrackState CreateTrackState(
        int trackIndex,
        string trackName,
        IEditorPreviewSettings settings,
        IEditorPreviewInstrumentCatalog instrumentCatalog)
        => new()
        {
            TrackIndex = trackIndex,
            TrackName = trackName,
            Transpose = TrackInfo.GetTransposeByName(trackName),
            BaseInstrumentId = instrumentCatalog.ResolveTrackInstrument(
                trackName,
                settings.DefaultInstrumentId,
                settings.ForceDefaultInstrument),
            IsProgramElectricGuitar = TrackInfo.IsProgramElectricGuitarTrackName(trackName),
        };

    public static PreviewTrackInstrumentAssignment CreateAssignment(
        PreviewInstrumentTrackState state,
        IEditorPreviewSettings settings,
        IEditorPreviewInstrumentCatalog instrumentCatalog)
        => new(
            state.TrackIndex,
            state.TrackName,
            state.Transpose,
            state.BaseInstrumentId,
            state.IsProgramElectricGuitar,
            state.GuitarToneChannelInstrumentIds.ToArray(),
            Enumerable.Range(0, 16)
                .Select(channel => ResolveInstrumentForChannel(
                    state.BaseInstrumentId,
                    state.IsProgramElectricGuitar,
                    state.GuitarToneChannelInstrumentIds,
                    channel,
                    instrumentCatalog))
                .ToArray());

    public static uint? ResolveInstrumentForChannel(
        uint? baseInstrumentId,
        bool isProgramElectricGuitar,
        IReadOnlyList<uint?> guitarToneChannelInstrumentIds,
        int channel,
        IEditorPreviewInstrumentCatalog instrumentCatalog)
    {
        if (baseInstrumentId is null or 0)
            return null;

        if (!instrumentCatalog.IsGuitar(baseInstrumentId.Value))
            return baseInstrumentId;

        if ((uint)channel < (uint)guitarToneChannelInstrumentIds.Count &&
            guitarToneChannelInstrumentIds[channel] is { } guitarToneInstrumentId)
        {
            return guitarToneInstrumentId;
        }

        return baseInstrumentId;
    }

    public static void ApplyProgramChange(
        PreviewInstrumentTrackState trackState,
        int channel,
        SevenBitNumber program,
        IEditorPreviewSettings settings)
    {
        if ((uint)channel >= 16)
            return;

        if (!TryResolveGuitarProgramInstrument(program, out var guitarToneInstrumentId))
            return;

        switch (settings.GuitarToneMode)
        {
            case GuitarToneMode.Off:
                break;
            case GuitarToneMode.Standard:
                trackState.GuitarToneChannelInstrumentIds[channel] = guitarToneInstrumentId;
                break;
            case GuitarToneMode.Simple:
                SetAllGuitarToneInstruments(trackState, guitarToneInstrumentId);
                break;
            case GuitarToneMode.OverrideByTrack:
                break;
            case GuitarToneMode.ProgramElectricGuitarMode:
                if (trackState.IsProgramElectricGuitar)
                    trackState.GuitarToneChannelInstrumentIds[channel] = guitarToneInstrumentId;
                break;
            default:
                break;
        }
    }

    private static void SetAllGuitarToneInstruments(
        PreviewInstrumentTrackState trackState,
        uint instrumentId)
    {
        for (var i = 0; i < trackState.GuitarToneChannelInstrumentIds.Length; i++)
            trackState.GuitarToneChannelInstrumentIds[i] = instrumentId;
    }

    private static bool TryResolveGuitarProgramInstrument(
        SevenBitNumber program,
        out uint instrumentId)
        => GuitarToneProgramResolver.TryResolveInstrumentFromProgram(program, out instrumentId);
}
