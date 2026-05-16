using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Guitar;

public enum MidiForgeGuitarToneResolutionSource
{
    None,
    CurrentOverride,
    JsonFile,
    TrackName,
    ProgramChange,
}

public sealed record MidiForgeGuitarToneOverrideSnapshot(
    GuitarToneMode ToneMode,
    string PlaybackFilePath,
    IReadOnlyDictionary<int, int> ToneByTrackIndex);

public sealed record MidiForgeGuitarToneJsonTrack(
    int Index,
    string Name,
    uint InstrumentId);

public sealed record MidiForgeGuitarToneJsonConfigSnapshot(
    IReadOnlyList<MidiForgeGuitarToneJsonTrack> Tracks);

public sealed record MidiForgeGuitarToneTrackResolution(
    int TrackIndex,
    string TrackName,
    int? Tone,
    MidiForgeGuitarToneResolutionSource Source,
    bool HasNotes,
    bool IsResolved,
    bool IsMergeable,
    string Reason);

public sealed record MidiForgeResolveGuitarToneGroupsResult(
    IReadOnlyList<MidiForgeGuitarToneTrackResolution> Tracks,
    IReadOnlyDictionary<int, int> ToneByTrackIndex,
    int SelectedTracks,
    int ResolvedTracks,
    int MergeableTracks,
    int MaximumMergeableTracks,
    bool ExceedsMaximumResolvedTracks);

[EditorOperation(
    "guitar.resolve-tone-groups",
    "Resolve Guitar Tone Groups",
    Kind = EditorOperationKind.Query,
    Scope = EditorOperationScope.Guitar,
    MenuPath = "Guitar/Tone",
    RequiresSelectedTracks = true,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class ResolveGuitarToneGroupsQuery
    : EditorOperationBase, IEditorQuery<ResolveGuitarToneGroupsQueryOptions, MidiForgeResolveGuitarToneGroupsResult>
{
    public EditorCommandValidation Validate(EditorQueryContext context, ResolveGuitarToneGroupsQueryOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorQueryResult<MidiForgeResolveGuitarToneGroupsResult> Execute(
        EditorQueryContext context,
        ResolveGuitarToneGroupsQueryOptions options)
    {
        var file = context.File;
        var validTrackIndices = options.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        var matchingJsonTracks = GetMatchingJsonTracks(file, options.JsonConfig);
        var trackResults = new List<MidiForgeGuitarToneTrackResolution>();
        var toneByTrackIndex = new Dictionary<int, int>();

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var hasNotes = sourceChunk.GetNotes().Any();
            var source = MidiForgeGuitarToneResolutionSource.None;
            int? tone = null;

            if (TryResolveCurrentOverrideTrackTone(file, options.CurrentOverride, trackIndex, out var resolvedTone))
            {
                tone = resolvedTone;
                source = MidiForgeGuitarToneResolutionSource.CurrentOverride;
            }
            else if (TryResolveJsonTrackTone(matchingJsonTracks, trackIndex, out resolvedTone))
            {
                tone = resolvedTone;
                source = MidiForgeGuitarToneResolutionSource.JsonFile;
            }
            else if (MidiForgeGuitarTonePrimitives.TryResolveToneFromTrackName(track.Name, out resolvedTone))
            {
                tone = resolvedTone;
                source = MidiForgeGuitarToneResolutionSource.TrackName;
            }
            else if (TryResolveTrackProgramTone(sourceChunk, out resolvedTone))
            {
                tone = resolvedTone;
                source = MidiForgeGuitarToneResolutionSource.ProgramChange;
            }

            var isResolved = tone.HasValue;
            var isMergeable = isResolved && hasNotes;
            if (isResolved)
                toneByTrackIndex[trackIndex] = tone.Value;

            trackResults.Add(new MidiForgeGuitarToneTrackResolution(
                trackIndex,
                track.Name,
                tone,
                source,
                hasNotes,
                isResolved,
                isMergeable,
                GetReason(source, hasNotes)));
        }

        var result = new MidiForgeResolveGuitarToneGroupsResult(
            trackResults,
            toneByTrackIndex,
            validTrackIndices.Length,
            trackResults.Count(track => track.IsResolved),
            trackResults.Count(track => track.IsMergeable),
            MidiForgeGuitarTonePrimitives.MaximumMergeTracks,
            trackResults.Count(track => track.IsResolved) > MidiForgeGuitarTonePrimitives.MaximumMergeTracks);

        return new EditorQueryResult<MidiForgeResolveGuitarToneGroupsResult>(result);
    }

    private static IReadOnlyList<MidiForgeGuitarToneJsonTrack> GetMatchingJsonTracks(
        EditableMidiFile file,
        MidiForgeGuitarToneJsonConfigSnapshot jsonConfig)
    {
        if (jsonConfig?.Tracks == null || jsonConfig.Tracks.Count == 0)
            return null;

        foreach (var dbTrack in jsonConfig.Tracks)
        {
            if (dbTrack.Index < 0 || dbTrack.Index >= file.Tracks.Count)
                return null;

            if (!string.Equals(dbTrack.Name, file.Tracks[dbTrack.Index].Name, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return jsonConfig.Tracks;
    }

    private static bool TryResolveCurrentOverrideTrackTone(
        EditableMidiFile file,
        MidiForgeGuitarToneOverrideSnapshot snapshot,
        int trackIndex,
        out int tone)
    {
        tone = 0;
        if (file.FilePath == null ||
            snapshot == null ||
            snapshot.PlaybackFilePath == null ||
            snapshot.ToneMode != GuitarToneMode.OverrideByTrack ||
            snapshot.ToneByTrackIndex == null ||
            !IsSamePath(file.FilePath, snapshot.PlaybackFilePath) ||
            !snapshot.ToneByTrackIndex.TryGetValue(trackIndex, out tone))
        {
            return false;
        }

        tone = Math.Clamp(tone, 0, 4);
        return true;
    }

    private static bool TryResolveJsonTrackTone(
        IReadOnlyList<MidiForgeGuitarToneJsonTrack> jsonTracks,
        int trackIndex,
        out int tone)
    {
        tone = 0;
        if (jsonTracks == null || (uint)trackIndex >= (uint)jsonTracks.Count)
            return false;

        var dbTrack = jsonTracks[trackIndex];
        return dbTrack.Index == trackIndex &&
               MidiForgeGuitarTonePrimitives.TryResolveToneFromInstrumentId(dbTrack.InstrumentId, out tone);
    }

    private static bool TryResolveTrackProgramTone(TrackChunk chunk, out int tone)
    {
        tone = 0;
        var programChange = chunk.Events
            .OfType<ProgramChangeEvent>()
            .FirstOrDefault();

        return programChange != null &&
               MidiForgeGuitarTonePrimitives.TryResolveToneFromProgram(programChange.ProgramNumber, out tone);
    }

    private static string GetReason(MidiForgeGuitarToneResolutionSource source, bool hasNotes)
    {
        if (source == MidiForgeGuitarToneResolutionSource.None)
            return "No guitar tone could be resolved.";

        if (!hasNotes)
            return "Tone was resolved, but the track has no notes to merge.";

        return source switch
        {
            MidiForgeGuitarToneResolutionSource.CurrentOverride => "Resolved from the current Override By Track tone.",
            MidiForgeGuitarToneResolutionSource.JsonFile => "Resolved from the matching MIDI json file.",
            MidiForgeGuitarToneResolutionSource.TrackName => "Resolved from the track name.",
            MidiForgeGuitarToneResolutionSource.ProgramChange => "Resolved from the first Program Change event.",
            _ => "Resolved.",
        };
    }

    private static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed record ResolveGuitarToneGroupsQueryOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeGuitarToneOverrideSnapshot CurrentOverride = null,
    MidiForgeGuitarToneJsonConfigSnapshot JsonConfig = null);
