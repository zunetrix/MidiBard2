using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

[EditorOperation(
    OperationId,
    "Map Instruments",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Names",
    RequiresSelectedTracks = true)]
public sealed class MapInstrumentsCommand
    : EditorOperationBase, IEditorCommand<MapInstrumentsCommandOptions, MidiForgeMapInstrumentsResult>
{
    public const string OperationId = "track.map-instruments";

    private static readonly Regex TrackNumberPattern = new(
        @"^track\s*\d+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public EditorCommandValidation Validate(EditorCommandContext context, MapInstrumentsCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeMapInstrumentsResult> Execute(
        EditorCommandContext context,
        MapInstrumentsCommandOptions commandOptions)
    {
        var file = context.File;
        var options = commandOptions.Options ?? new MidiForgeMapInstrumentsOptions();
        var validTrackIndices = MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(
            file,
            commandOptions.TrackIndices);
        var renamedTracks = 0;
        var skippedTracks = 0;

        foreach (var (trackIndex, fallbackIndex) in validTrackIndices.Select((index, order) => (index, order + 1)))
        {
            var track = file.Tracks[trackIndex];
            var currentName = track.Name ?? string.Empty;
            if (!ShouldRename(track, fallbackIndex, options.Mode))
            {
                skippedTracks++;
                continue;
            }

            if (!TryResolveTrackName(track, context.Services.MidiMapProvider, options.IncludeDrumTracks, fallbackIndex, out var mappedName))
            {
                skippedTracks++;
                continue;
            }

            if (MidiForgeTrackNamePrimitives.SetEditableTrackName(track, mappedName))
                renamedTracks++;
            else if (!string.Equals(currentName, mappedName, StringComparison.Ordinal))
                skippedTracks++;
        }

        var result = new MidiForgeMapInstrumentsResult(
            validTrackIndices.Length,
            renamedTracks,
            skippedTracks);
        if (renamedTracks == 0)
            return EditorCommandResult<MidiForgeMapInstrumentsResult>.UnchangedResult(result);

        return EditorCommandResult<MidiForgeMapInstrumentsResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }

    private static bool ShouldRename(
        EditableTrack track,
        int fallbackIndex,
        MidiForgeMapInstrumentsMode mode)
    {
        return mode switch
        {
            MidiForgeMapInstrumentsMode.ReplaceSelectedNames => true,
            MidiForgeMapInstrumentsMode.EmptyNamesOnly => string.IsNullOrWhiteSpace(track.Name),
            _ => IsEmptyOrGenericName(track, fallbackIndex),
        };
    }

    private static bool TryResolveTrackName(
        EditableTrack track,
        IEditorMidiMapProvider mapProvider,
        bool includeDrumTracks,
        int fallbackIndex,
        out string trackName)
    {
        trackName = string.Empty;
        var chunk = track.Chunk;
        var channelEvents = chunk.Events.OfType<ChannelEvent>().ToArray();
        var hasDrumEvents = channelEvents.Any(e => (byte)e.Channel == MidiForgeAnalysis.DrumChannel);
        if (hasDrumEvents)
            return includeDrumTracks && TryResolveDrumTrackName(chunk, mapProvider, out trackName);

        var program = chunk.Events.OfType<ProgramChangeEvent>().FirstOrDefault()?.ProgramNumber;
        if (program is { } programNumber &&
            mapProvider.TryResolveInstrumentTrackName(programNumber, out trackName))
            return true;

        trackName = MidiForgeTrackNaming.GetDefaultTrackName(
            chunk,
            fallbackIndex,
            MidiForgeTrackNameFillMode.Ffxiv,
            mapProvider);
        return !string.IsNullOrWhiteSpace(trackName);
    }

    private static bool TryResolveDrumTrackName(
        TrackChunk chunk,
        IEditorMidiMapProvider mapProvider,
        out string trackName)
    {
        var mappedTrackNames = chunk.GetNotes()
            .Where(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel)
            .Select(note => GetSourceMapTrackName(mapProvider, (byte)note.NoteNumber))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        trackName = mappedTrackNames.Length == 1
            ? mappedTrackNames[0]
            : "Drumkit";
        return true;
    }

    private static string GetSourceMapTrackName(IEditorMidiMapProvider mapProvider, int noteNumber)
    {
        foreach (var sourceMap in mapProvider.GetDrumkitSourceMaps())
        {
            if (sourceMap.SourceNotes.Contains(noteNumber))
                return sourceMap.TrackName;
        }

        return string.Empty;
    }

    private static bool IsEmptyOrGenericName(EditableTrack track, int fallbackIndex)
    {
        var name = track.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return true;

        if (TrackNumberPattern.IsMatch(name))
            return true;

        if (string.Equals(name, $"Track {fallbackIndex:00}", StringComparison.OrdinalIgnoreCase))
            return true;

        var program = track.Chunk.Events.OfType<ProgramChangeEvent>().FirstOrDefault()?.ProgramNumber;
        if (program is not { } programNumber)
            return string.Equals(name, "Drumkit", StringComparison.OrdinalIgnoreCase);

        var midiName = DryWetMidiExtensions.GetGMProgramName(programNumber);
        return !string.IsNullOrWhiteSpace(midiName) &&
               string.Equals(name, midiName, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MapInstrumentsCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeMapInstrumentsOptions Options);
