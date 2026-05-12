using System;
using System.Collections.Generic;
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
    private static readonly IReadOnlyDictionary<uint, string> CapturedPathOverrides = new Dictionary<uint, string>
    {
        [1] = "sound/instruments/047harp.scd",
        [2] = "sound/instruments/001grandpiano.scd",
        [3] = "sound/instruments/026steelguitar.scd",
        [4] = "sound/instruments/046pizzicato.scd",
        [5] = "sound/instruments/074flute.scd",
        [6] = "sound/instruments/069oboe.scd",
        [7] = "sound/instruments/072clarinet.scd",
        [8] = "sound/instruments/073piccolo.scd",
        [9] = "sound/instruments/076panflute.scd",
        [10] = "sound/instruments/048timpani.scd",
        [11] = "sound/instruments/097bongo.scd",
        [12] = "sound/instruments/098bd.scd",
        [13] = "sound/instruments/099snare.scd",
        [14] = "sound/instruments/100cymbal.scd",
        [15] = "sound/instruments/057trumpet.scd",
        [16] = "sound/instruments/058trombone.scd",
        [17] = "sound/instruments/059tuba.scd",
        [18] = "sound/instruments/061frenchhorn.scd",
        [19] = "sound/instruments/066altosax.scd",
        [20] = "sound/instruments/041violin.scd",
        [21] = "sound/instruments/042viola.scd",
        [22] = "sound/instruments/043cello.scd",
        [23] = "sound/instruments/044contrabass.scd",
        [24] = "sound/instruments/030driveguitar.scd",
        [25] = "sound/instruments/028cleanguitar.scd",
        [26] = "sound/instruments/029muteguitar.scd",
        [27] = "sound/instruments/031powerguitar.scd",
        [28] = "sound/instruments/032FXguitar.scd",
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
        .Select(def => CapturedPathOverrides.TryGetValue(def.InstrumentId, out var path)
            ? def with { Path = path }
            : def)
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
                var name = GetInstrumentName(entry.InstrumentId);
                return $"[{entry.InstrumentId}] = \"{Escape(entry.Path)}\", // {name}; captured soundNumber={entry.SoundNumber}, midiNote={entry.MidiNote}";
            });

        return "private static readonly IReadOnlyDictionary<uint, string> CapturedPathOverrides = new Dictionary<uint, string>\n{\n" +
               string.Join("\n", rows.Select(row => $"    {row}")) +
               "\n};";
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

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
}
