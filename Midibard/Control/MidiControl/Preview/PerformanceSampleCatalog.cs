using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using FFXIVClientStructs.FFXIV.Client.Sound;

namespace MidiBard.Control.MidiControl.Preview;

internal sealed record PerformanceSampleDefinition(
    uint InstrumentId,
    string InstrumentName,
    string FileName,
    string? Path = null,
    uint SoundNumber = 0,
    // Direct preview keeps SoundData alive so MIDI NoteOff can apply an explicit release fade.
    bool AutoRelease = false,
    int MidiNoteBase = 24,
    float Volume = 1.0f,
    uint FadeInDuration = 0,
    float Speed = 1.0f,
    int A9 = 0,
    SoundVolumeCategory VolumeCategory = SoundVolumeCategory.BypassVolumeRules,
    bool A13 = false,
    bool A15 = false,
    // PlaySound's built-in default fade is documented as 10 seconds; preview uses Stop(500).
    bool DefaultFadeOut = false,
    bool IsPositional = false,
    bool A18 = false)
{
    public uint GetSoundNumber(int gameNote)
        => SoundNumber;

    public int GetMidiNote(int gameNote)
        => MidiNoteBase + Math.Clamp(gameNote, 0, 36);
}

internal static class PerformanceSampleCatalog
{
    private static readonly IReadOnlyDictionary<uint, PerformanceSampleDefinition> CapturedDefinitionOverrides = new Dictionary<uint, PerformanceSampleDefinition>
    {
        [1] = DefineCaptured(1, "Harp", "047harp.scd", "sound/instruments/047harp.scd"),
        [2] = DefineCaptured(2, "Piano", "001grandpiano.scd", "sound/instruments/001grandpiano.scd"),
        [3] = DefineCaptured(3, "Lute", "026steelguitar.scd", "sound/instruments/026steelguitar.scd"),
        [4] = DefineCaptured(4, "Fiddle", "046pizzicato.scd", "sound/instruments/046pizzicato.scd"),
        [5] = DefineCaptured(5, "Flute", "074flute.scd", "sound/instruments/074flute.scd"),
        [6] = DefineCaptured(6, "Oboe", "069oboe.scd", "sound/instruments/069oboe.scd"),
        [7] = DefineCaptured(7, "Clarinet", "072clarinet.scd", "sound/instruments/072clarinet.scd"),
        [8] = DefineCaptured(8, "Fife", "073piccolo.scd", "sound/instruments/073piccolo.scd"),
        [9] = DefineCaptured(9, "Panpipes", "076panflute.scd", "sound/instruments/076panflute.scd"),
        [10] = DefineCaptured(10, "Timpani", "048timpani.scd", "sound/instruments/048timpani.scd"),
        [11] = DefineCaptured(11, "Bongo", "097bongo.scd", "sound/instruments/097bongo.scd"),
        [12] = DefineCaptured(12, "Bass Drum", "098bd.scd", "sound/instruments/098bd.scd"),
        [13] = DefineCaptured(13, "Snare Drum", "099snare.scd", "sound/instruments/099snare.scd"),
        [14] = DefineCaptured(14, "Cymbal", "100cymbal.scd", "sound/instruments/100cymbal.scd"),
        [15] = DefineCaptured(15, "Trumpet", "057trumpet.scd", "sound/instruments/057trumpet.scd"),
        [16] = DefineCaptured(16, "Trombone", "058trombone.scd", "sound/instruments/058trombone.scd"),
        [17] = DefineCaptured(17, "Tuba", "059tuba.scd", "sound/instruments/059tuba.scd"),
        [18] = DefineCaptured(18, "Horn", "061frenchhorn.scd", "sound/instruments/061frenchhorn.scd"),
        [19] = DefineCaptured(19, "Saxophone", "066altosax.scd", "sound/instruments/066altosax.scd"),
        [20] = DefineCaptured(20, "Violin", "041violin.scd", "sound/instruments/041violin.scd"),
        [21] = DefineCaptured(21, "Viola", "042viola.scd", "sound/instruments/042viola.scd"),
        [22] = DefineCaptured(22, "Cello", "043cello.scd", "sound/instruments/043cello.scd"),
        [23] = DefineCaptured(23, "Double Bass", "044contrabass.scd", "sound/instruments/044contrabass.scd"),
        [24] = DefineCaptured(24, "Electric Guitar: Overdriven", "030driveguitar.scd", "sound/instruments/030driveguitar.scd"),
        [25] = DefineCaptured(25, "Electric Guitar: Clean", "028cleanguitar.scd", "sound/instruments/028cleanguitar.scd"),
        [26] = DefineCaptured(26, "Electric Guitar: Muted", "029muteguitar.scd", "sound/instruments/029muteguitar.scd"),
        [27] = DefineCaptured(27, "Electric Guitar: Power Chords", "031powerguitar.scd", "sound/instruments/031powerguitar.scd"),
        [28] = DefineCaptured(28, "Electric Guitar: Special", "032fxguitar.scd", "sound/instruments/032FXguitar.scd"),
    };

    private static readonly IReadOnlyDictionary<uint, PerformanceSampleDefinition> Definitions =
        new[]
        {
            Define(1, "Harp", "047harp.scd"),
            Define(2, "Piano", "001grandpiano.scd"),
            Define(3, "Lute", "026steelguitar.scd"),
            Define(4, "Fiddle", "046pizzicato.scd"),
            Define(5, "Flute", "074flute.scd"),
            Define(6, "Oboe", "069oboe.scd"),
            Define(7, "Clarinet", "072clarinet.scd"),
            Define(8, "Fife", "073piccolo.scd"),
            Define(9, "Panpipes", "076panflute.scd"),
            Define(10, "Timpani", "048timpani.scd"),
            Define(11, "Bongo", "097bongo.scd"),
            Define(12, "Bass Drum", "098bd.scd"),
            Define(13, "Snare Drum", "099snare.scd"),
            Define(14, "Cymbal", "100cymbal.scd"),
            Define(15, "Trumpet", "057trumpet.scd"),
            Define(16, "Trombone", "058trombone.scd"),
            Define(17, "Tuba", "059tuba.scd"),
            Define(18, "Horn", "061frenchhorn.scd"),
            Define(19, "Saxophone", "066altosax.scd"),
            Define(20, "Violin", "041violin.scd"),
            Define(21, "Viola", "042viola.scd"),
            Define(22, "Cello", "043cello.scd"),
            Define(23, "Double Bass", "044contrabass.scd"),
            Define(24, "Electric Guitar: Overdriven", "030driveguitar.scd"),
            Define(25, "Electric Guitar: Clean", "028cleanguitar.scd"),
            Define(26, "Electric Guitar: Muted", "029muteguitar.scd"),
            Define(27, "Electric Guitar: Power Chords", "031powerguitar.scd"),
            Define(28, "Electric Guitar: Special", "032fxguitar.scd"),
        }
        .Select(ApplyCapturedDefinition)
        .ToDictionary(def => def.InstrumentId);

    private static readonly Dictionary<string, string?> ResolvedPathCache = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<PerformanceSampleDefinition> All => Definitions.Values;

    public static bool TryGet(uint instrumentId, out PerformanceSampleDefinition definition)
        => Definitions.TryGetValue(instrumentId, out definition);

    public static bool IsPerformanceInstrumentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("sound/instruments/", StringComparison.OrdinalIgnoreCase)
            && Definitions.Values.Any(sample => normalized.EndsWith(sample.FileName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryResolvePath(PerformanceSampleDefinition definition, out string path)
    {
        var cacheKey = definition.Path ?? definition.FileName;
        if (ResolvedPathCache.TryGetValue(cacheKey, out var cached))
        {
            path = cached ?? string.Empty;
            return cached != null;
        }

        foreach (var candidate in GetCandidatePaths(definition))
        {
            if (!DalamudApi.DataManager.FileExists(candidate)) continue;

            ResolvedPathCache[cacheKey] = candidate;
            path = candidate;
            return true;
        }

        ResolvedPathCache[cacheKey] = null;
        path = string.Empty;
        return false;
    }

    public static string BuildSourceRows(IEnumerable<PerformanceSampleProbeEntry> entries)
    {
        var rows = entries
            .Where(entry => entry.InstrumentId > 0 && IsPerformanceInstrumentPath(entry.Path))
            .GroupBy(entry => entry.InstrumentId)
            .Select(group => group.OrderByDescending(entry => entry.TimestampUtc).First())
            .OrderBy(entry => entry.InstrumentId)
            .Select(entry =>
            {
                var definition = Definitions.TryGetValue(entry.InstrumentId, out var existing)
                    ? existing
                    : Define(entry.InstrumentId, GetInstrumentName(entry.InstrumentId), GetFileName(entry.Path));
                return FormatCapturedDefinitionRow(entry, definition);
            });

        return "private static readonly IReadOnlyDictionary<uint, PerformanceSampleDefinition> CapturedDefinitionOverrides = new Dictionary<uint, PerformanceSampleDefinition>\n{\n" +
               string.Join("\n", rows.Select(row => $"    {row}")) +
               "\n};";
    }

    private static string FormatCapturedDefinitionRow(PerformanceSampleProbeEntry entry, PerformanceSampleDefinition definition)
    {
        var midiNoteBase = entry.GameNote is { } gameNote
            ? entry.MidiNote - Math.Clamp(gameNote, 0, 36)
            : definition.MidiNoteBase;
        var gameNoteText = entry.GameNote?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

        return $"[{entry.InstrumentId}] = DefineCaptured({entry.InstrumentId}, \"{Escape(definition.InstrumentName)}\", \"{Escape(definition.FileName)}\", \"{Escape(entry.Path)}\", " +
               $"soundNumber: {entry.SoundNumber}u, autoRelease: {FormatBool(entry.AutoRelease)}, midiNoteBase: {midiNoteBase}, " +
               $"volume: {FormatFloat(entry.Volume)}, fadeInDuration: {entry.FadeInDuration}u, speed: {FormatFloat(entry.Speed)}, a9: {entry.A9}, " +
               $"volumeCategory: {FormatVolumeCategory(entry.VolumeCategory)}, a13: {FormatBool(entry.A13)}, a15: {FormatBool(entry.A15)}, " +
               $"defaultFadeOut: {FormatBool(entry.DefaultFadeOut)}, isPositional: {FormatBool(entry.IsPositional)}, a18: {FormatBool(entry.A18)}), " +
               $"// {definition.InstrumentName}; captured midiNote={entry.MidiNote}, gameNote={gameNoteText}";
    }

    private static string FormatBool(bool value)
        => value ? "true" : "false";

    private static string FormatFloat(float value)
        => value.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static string FormatVolumeCategory(SoundVolumeCategory volumeCategory)
    {
        var name = Enum.GetName(typeof(SoundVolumeCategory), volumeCategory);
        return name == null
            ? $"(SoundVolumeCategory){Convert.ToInt32(volumeCategory)}"
            : $"SoundVolumeCategory.{name}";
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
    }

    private static string GetInstrumentName(uint instrumentId)
    {
        var names = MidiBard.Util.InstrumentHelper.InstrumentStrings;
        if (names != null && instrumentId < (uint)names.Length && !string.IsNullOrWhiteSpace(names[instrumentId]))
            return names[instrumentId];

        return Definitions.TryGetValue(instrumentId, out var definition)
            ? definition.InstrumentName
            : $"Instrument {instrumentId}";
    }

    private static IEnumerable<string> GetCandidatePaths(PerformanceSampleDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Path))
            yield return definition.Path;

        yield return definition.FileName;
        yield return $"sound/{definition.FileName}";
        yield return $"sound/ffxiv/{definition.FileName}";
        yield return $"sound/ffxiv/performance/{definition.FileName}";
        yield return $"sound/performance/{definition.FileName}";
        yield return $"sound/perform/{definition.FileName}";
    }

    private static PerformanceSampleDefinition Define(uint instrumentId, string instrumentName, string fileName)
        => new(instrumentId, instrumentName, fileName);

    private static PerformanceSampleDefinition DefineCaptured(
        uint instrumentId,
        string instrumentName,
        string fileName,
        string path,
        uint soundNumber = 0,
        bool autoRelease = false,
        int midiNoteBase = 24,
        float volume = 1.0f,
        uint fadeInDuration = 0,
        float speed = 1.0f,
        int a9 = 0,
        SoundVolumeCategory volumeCategory = SoundVolumeCategory.BypassVolumeRules,
        bool a13 = false,
        bool a15 = false,
        bool defaultFadeOut = false,
        bool isPositional = false,
        bool a18 = false)
        => new(
            instrumentId,
            instrumentName,
            fileName,
            path,
            soundNumber,
            autoRelease,
            midiNoteBase,
            volume,
            fadeInDuration,
            speed,
            a9,
            volumeCategory,
            a13,
            a15,
            defaultFadeOut,
            isPositional,
            a18);

    private static PerformanceSampleDefinition ApplyCapturedDefinition(PerformanceSampleDefinition definition)
        => CapturedDefinitionOverrides.TryGetValue(definition.InstrumentId, out var captured)
            ? definition with
            {
                Path = captured.Path,
                SoundNumber = captured.SoundNumber,
                AutoRelease = captured.AutoRelease,
                MidiNoteBase = captured.MidiNoteBase,
                Volume = captured.Volume,
                FadeInDuration = captured.FadeInDuration,
                Speed = captured.Speed,
                A9 = captured.A9,
                VolumeCategory = captured.VolumeCategory,
                A13 = captured.A13,
                A15 = captured.A15,
                DefaultFadeOut = captured.DefaultFadeOut,
                IsPositional = captured.IsPositional,
                A18 = captured.A18,
            }
            : definition;
}
