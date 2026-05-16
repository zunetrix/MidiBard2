using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeTrackAnalysis(
    int TrackIndex,
    string TrackName,
    int Channel,
    bool IsConductorTrack,
    bool IsDrumTrack,
    int NoteCount,
    int UniqueNoteCount,
    int? LowestNote,
    int? HighestNote,
    int OutOfRangeBelowCount,
    int OutOfRangeAboveCount,
    int? FirstProgramNumber,
    int ProgramChangeCount,
    int PitchBendCount,
    int ZeroLengthNoteCount,
    int MaxSimultaneousNotes,
    int MaxActiveOverlappingNotes,
    int SuggestedTransposeSemitones)
{
    public bool HasNotes => NoteCount > 0;
    public bool HasOutOfRangeNotes => OutOfRangeBelowCount > 0 || OutOfRangeAboveCount > 0;
}

public static class MidiForgeAnalysis
{
    public const int PlayableLowestMidiNote = 48;
    public const int PlayableHighestMidiNote = 84;
    public const int DrumChannel = 9;

    private static readonly HashSet<int> MidiEffectsPrograms = new(Enumerable.Range(120, 8));

    public static IReadOnlyList<MidiForgeTrackAnalysis> AnalyzeTracks(IEnumerable<EditableTrack> tracks)
        => tracks.Select(AnalyzeTrack).ToArray();

    public static IReadOnlyList<string> GetTrackDiagnostics(
        MidiForgeTrackAnalysis analysis,
        IEditorMidiMapProvider? mapProvider = null)
    {
        var diagnostics = new List<string>();

        if (analysis.IsConductorTrack)
            return diagnostics;

        if (string.IsNullOrWhiteSpace(analysis.TrackName))
            diagnostics.Add("Track has no name.");

        if (!analysis.HasNotes)
            diagnostics.Add("Track has no notes.");

        if (analysis.HasOutOfRangeNotes)
            diagnostics.Add(
                $"{analysis.OutOfRangeBelowCount + analysis.OutOfRangeAboveCount} note(s) outside C3-C6 " +
                $"({analysis.OutOfRangeBelowCount} below, {analysis.OutOfRangeAboveCount} above).");

        if (analysis.SuggestedTransposeSemitones != 0)
            diagnostics.Add($"Suggested transpose: {analysis.SuggestedTransposeSemitones:+#;-#;0} semitone(s).");

        if (analysis.ZeroLengthNoteCount > 0)
            diagnostics.Add($"Contains {analysis.ZeroLengthNoteCount} zero-length note(s).");

        if (analysis.MaxSimultaneousNotes > 3)
            diagnostics.Add($"Max simultaneous notes is {analysis.MaxSimultaneousNotes}; FFXIV playback usually needs 3 or fewer.");

        if (analysis.IsDrumTrack && analysis.MaxSimultaneousNotes >= 2)
            diagnostics.Add($"Drum track has {analysis.MaxSimultaneousNotes} simultaneous hit(s); consider Drums > Split Drumkit Tracks.");

        if (analysis.PitchBendCount > 0)
            diagnostics.Add($"Contains {analysis.PitchBendCount} pitch bend event(s); verify playback result.");

        if (analysis.IsDrumTrack && analysis.UniqueNoteCount > 1)
            diagnostics.Add("Drum channel has multiple note types; consider Drums > Split Drumkit Tracks.");

        AddMapDiagnostics(analysis, mapProvider, diagnostics);

        return diagnostics;
    }

    public static IReadOnlyList<string> GetTrackDiagnosticTooltipLines(
        MidiForgeTrackAnalysis analysis,
        IEditorMidiMapProvider? mapProvider = null)
    {
        if (analysis.IsConductorTrack)
            return Array.Empty<string>();

        var lines = new List<string>
        {
            $"Notes: {analysis.NoteCount}",
            $"Pitch bends: {analysis.PitchBendCount}",
            $"Program changes: {analysis.ProgramChangeCount}",
            $"Zero-length notes: {analysis.ZeroLengthNoteCount}",
            $"Range: {FormatNoteRange(analysis)}",
            $"Max simultaneous notes: {analysis.MaxSimultaneousNotes}",
            $"Max active overlapping notes: {analysis.MaxActiveOverlappingNotes}",
            $"Suggested transpose: {analysis.SuggestedTransposeSemitones:+#;-#;0} semitone(s)"
        };

        var warnings = GetTrackDiagnostics(analysis, mapProvider);
        if (warnings.Count == 0)
            return lines;

        lines.Add(string.Empty);
        lines.Add("Warnings:");
        lines.AddRange(warnings.Select(warning => $"- {warning}"));
        return lines;
    }

    public static MidiForgeTrackAnalysis AnalyzeTrack(EditableTrack track)
        => AnalyzeTrackChunk(
            track.Chunk,
            track.Index,
            track.Name,
            track.Channel,
            track.IsConductorTrack);

    public static MidiForgeTrackAnalysis AnalyzeTrackChunk(
        TrackChunk chunk,
        int trackIndex,
        string? trackName = null,
        int? channel = null,
        bool? isConductorTrack = null)
    {
        var notes = chunk.GetNotes().ToArray();
        var noteNumbers = notes.Select(note => (int)(byte)note.NoteNumber).ToArray();
        var effectiveChannel = channel ?? ExtractChannel(chunk);
        var conductor = isConductorTrack ?? IsConductorTrack(chunk);
        var isDrumTrack = !conductor && effectiveChannel == DrumChannel;
        var programChangeCount = chunk.Events.OfType<ProgramChangeEvent>().Count();
        var firstProgramNumber = chunk.Events
            .OfType<ProgramChangeEvent>()
            .Select(program => (int?)(byte)program.ProgramNumber)
            .FirstOrDefault();
        var pitchBendCount = chunk.Events.OfType<PitchBendEvent>().Count();
        int? lowest = noteNumbers.Length == 0 ? null : noteNumbers.Min();
        int? highest = noteNumbers.Length == 0 ? null : noteNumbers.Max();
        var outBelow = noteNumbers.Count(note => note < PlayableLowestMidiNote);
        var outAbove = noteNumbers.Count(note => note > PlayableHighestMidiNote);
        var zeroLengthNoteCount = notes.Count(note => note.Length <= 0);
        var maxSimultaneousNotes = notes
            .GroupBy(note => note.Time)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();
        var maxActiveOverlappingNotes = GetMaxActiveOverlappingNotes(notes);
        var suggestedTranspose = ShouldSkipTransposeSuggestion(chunk, isDrumTrack)
            ? 0
            : GetOptimalTransposeAmount(noteNumbers);

        return new MidiForgeTrackAnalysis(
            trackIndex,
            trackName ?? string.Empty,
            effectiveChannel,
            conductor,
            isDrumTrack,
            notes.Length,
            noteNumbers.Distinct().Count(),
            lowest,
            highest,
            outBelow,
            outAbove,
            firstProgramNumber,
            programChangeCount,
            pitchBendCount,
            zeroLengthNoteCount,
            maxSimultaneousNotes,
            maxActiveOverlappingNotes,
            suggestedTranspose);
    }

    public static string FormatMidiNoteName(int noteNumber)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return $"{names[Math.Clamp(noteNumber, 0, 127) % 12]}{Math.Clamp(noteNumber, 0, 127) / 12 - 1}";
    }

    public static int GetOptimalTransposeAmount(IEnumerable<int> notes)
    {
        var noteNumbers = notes.ToArray();
        if (noteNumbers.Length == 0) return 0;

        var currentRange = CountRangeGroups(noteNumbers, 0);
        var transposeAmount = currentRange.OutOfRangeBelow > currentRange.InRange
            ? 12
            : currentRange.OutOfRangeAbove > currentRange.InRange
                ? -12
                : 0;

        if (transposeAmount == 0 || currentRange.OutOfRange == 0)
            return 0;

        var optimalTransposeAmount = 0;

        for (var octaveAttempts = 0; octaveAttempts < 10; octaveAttempts++)
        {
            var nextTransposeAmount = optimalTransposeAmount + transposeAmount;
            var transposedRange = CountRangeGroups(noteNumbers, nextTransposeAmount);

            if (transposedRange.OutOfRange == 0 ||
                transposedRange.OutOfRange * 100 / noteNumbers.Length <= 20)
                return nextTransposeAmount;

            if (transposedRange.OutOfRange >= currentRange.OutOfRange)
                return optimalTransposeAmount;

            currentRange = transposedRange;
            optimalTransposeAmount = nextTransposeAmount;
        }

        return optimalTransposeAmount;
    }

    private static bool ShouldSkipTransposeSuggestion(TrackChunk chunk, bool isDrumTrack)
    {
        if (isDrumTrack) return true;

        var programNumber = chunk.Events
            .OfType<ProgramChangeEvent>()
            .Select(program => (int)(byte)program.ProgramNumber)
            .FirstOrDefault(-1);

        return MidiEffectsPrograms.Contains(programNumber);
    }

    private static void AddMapDiagnostics(
        MidiForgeTrackAnalysis analysis,
        IEditorMidiMapProvider? mapProvider,
        List<string> diagnostics)
    {
        if (mapProvider == null)
            return;

        var trackName = analysis.TrackName?.Trim() ?? string.Empty;
        if (mapProvider.TryResolveInstrumentTrackNameAlias(trackName, out var aliasTrackName) &&
            !string.Equals(trackName, aliasTrackName, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(
                $"Track name alias resolves to {aliasTrackName}; use Track > Map Selected Instruments to apply the canonical name.");
        }

        if (!analysis.FirstProgramNumber.HasValue ||
            !ShouldSuggestProgramNameMapping(trackName, analysis.FirstProgramNumber.Value) ||
            !mapProvider.TryResolveInstrumentTrackName((SevenBitNumber)(byte)analysis.FirstProgramNumber.Value, out var programTrackName))
        {
            return;
        }

        diagnostics.Add(
            $"Program Change resolves to {programTrackName}; use Track > Map Selected Instruments to rename this track.");
    }

    private static bool ShouldSuggestProgramNameMapping(string trackName, int programNumber)
    {
        if (string.IsNullOrWhiteSpace(trackName))
            return true;

        var normalized = trackName.Trim().Replace(" ", string.Empty);
        if (normalized.Length > "Track".Length &&
            normalized.StartsWith("Track", StringComparison.OrdinalIgnoreCase) &&
            normalized["Track".Length..].All(char.IsDigit))
        {
            return true;
        }

        var midiName = DryWetMidiExtensions.GetGMProgramName((byte)programNumber);
        return !string.IsNullOrWhiteSpace(midiName) &&
               string.Equals(trackName.Trim(), midiName, StringComparison.OrdinalIgnoreCase);
    }

    private static (int OutOfRangeBelow, int OutOfRangeAbove, int InRange, int OutOfRange) CountRangeGroups(
        IEnumerable<int> notes,
        int transposeAmount)
    {
        var below = 0;
        var above = 0;
        var inRange = 0;

        foreach (var originalNote in notes)
        {
            var note = originalNote + transposeAmount;
            if (note < PlayableLowestMidiNote) below++;
            else if (note > PlayableHighestMidiNote) above++;
            else inRange++;
        }

        return (below, above, inRange, below + above);
    }

    private static int ExtractChannel(TrackChunk chunk)
        => chunk.Events.OfType<ChannelEvent>().FirstOrDefault() is { } ev ? (byte)ev.Channel : 0;

    private static bool IsConductorTrack(TrackChunk chunk)
        => chunk.Events.Count > 0 && !chunk.Events.OfType<ChannelEvent>().Any();

    private static int GetMaxActiveOverlappingNotes(IEnumerable<Note> notes)
    {
        var positiveLengthNotes = notes
            .Where(note => note.Length > 0)
            .ToArray();

        if (positiveLengthNotes.Length == 0)
            return 0;

        var active = 0;
        var maxActive = 0;
        foreach (var tickGroup in positiveLengthNotes
            .SelectMany(note => new[]
            {
                (Tick: note.Time, Delta: 1),
                (Tick: note.EndTime, Delta: -1)
            })
            .GroupBy(item => item.Tick)
            .OrderBy(group => group.Key))
        {
            active += tickGroup.Where(item => item.Delta < 0).Sum(item => item.Delta);
            active += tickGroup.Where(item => item.Delta > 0).Sum(item => item.Delta);
            maxActive = Math.Max(maxActive, active);
        }

        return maxActive;
    }

    private static string FormatNoteRange(MidiForgeTrackAnalysis analysis)
    {
        if (analysis.LowestNote == null || analysis.HighestNote == null)
            return "none";

        return $"{FormatMidiNoteName(analysis.LowestNote.Value)}-{FormatMidiNoteName(analysis.HighestNote.Value)} " +
            $"({analysis.LowestNote.Value}-{analysis.HighestNote.Value})";
    }
}
