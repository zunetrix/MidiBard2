using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Control.MidiControl.Editing;

public sealed class MidiForgeMapSettings
{
    public int Version { get; set; } = 1;
    public List<MidiForgeInstrumentMapSettings> InstrumentMaps { get; set; } = new();
    public List<MidiForgeDrumInstrumentMapSettings> DrumkitSourceMaps { get; set; } = new();
    public List<MidiForgeDrumTransposePresetSettings> DrumTransposePresets { get; set; } = new();
}

public sealed class MidiForgeInstrumentMapSettings
{
    public uint InstrumentId { get; set; }
    public string InstrumentName { get; set; } = string.Empty;
    public string TrackName { get; set; } = string.Empty;
    public int TrackOrder { get; set; }
    public List<int> MidiPrograms { get; set; } = new();
}

public sealed class MidiForgeDrumInstrumentMapSettings
{
    public string TrackName { get; set; } = string.Empty;
    public List<int> SourceNotes { get; set; } = new();
}

public sealed class MidiForgeDrumTransposePresetSettings
{
    public MidiForgeDrumTransposePreset Preset { get; set; }
    public List<MidiForgeDrumTransposeMapEntry> Entries { get; set; } = new();
}

public sealed class MidiForgeDrumTransposeMapEntry
{
    public string Category { get; set; } = string.Empty;
    public string DrumkitInstrument { get; set; } = string.Empty;
    public int InputNote { get; set; }
    public int OutputNote { get; set; }
}

public interface IEditorMidiMapProvider
{
    IReadOnlyList<MidiForgeInstrumentMapSettings> GetInstrumentMaps();
    IReadOnlyList<MidiForgeDrumInstrumentMap> GetDrumkitSourceMaps();
    IReadOnlyList<MidiForgeDrumTransposeTarget> GetDrumTransposeTargets(MidiForgeDrumTransposePreset preset);
    bool TryResolveInstrumentTrackName(SevenBitNumber programNumber, out string trackName);
    bool IsMappedDrumSourceNote(int noteNumber);
    int TransposeDrumNote(int noteNumber, MidiForgeDrumTransposePreset preset);
    string GetDrumkitInstrumentName(int noteNumber);
}

public sealed class DefaultEditorMidiMapProvider : IEditorMidiMapProvider
{
    public static DefaultEditorMidiMapProvider Instance { get; } = new();

    private readonly MidiForgeMapSettings settings = MidiForgeMapDefaults.CreateDefaultSettings();

    private DefaultEditorMidiMapProvider()
    {
        MidiForgeMapDefaults.Normalize(settings);
    }

    public IReadOnlyList<MidiForgeInstrumentMapSettings> GetInstrumentMaps()
        => settings.InstrumentMaps;

    public IReadOnlyList<MidiForgeDrumInstrumentMap> GetDrumkitSourceMaps()
        => MidiForgeMapDefaults.GetDrumkitSourceMaps(settings);

    public IReadOnlyList<MidiForgeDrumTransposeTarget> GetDrumTransposeTargets(MidiForgeDrumTransposePreset preset)
        => MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(settings, preset);

    public bool TryResolveInstrumentTrackName(SevenBitNumber programNumber, out string trackName)
        => MidiForgeMapDefaults.TryResolveInstrumentTrackName(settings, programNumber, out trackName);

    public bool IsMappedDrumSourceNote(int noteNumber)
        => MidiForgeMapDefaults.IsMappedDrumSourceNote(settings, noteNumber);

    public int TransposeDrumNote(int noteNumber, MidiForgeDrumTransposePreset preset)
        => MidiForgeMapDefaults.TransposeDrumNote(settings, noteNumber, preset);

    public string GetDrumkitInstrumentName(int noteNumber)
        => MidiForgeMapDefaults.GetDrumkitInstrumentName(noteNumber);
}

public sealed class ConfigurationEditorMidiMapProvider : IEditorMidiMapProvider
{
    private readonly MidiForgeMapSettings settings;

    public ConfigurationEditorMidiMapProvider(MidiForgeMapSettings settings)
    {
        this.settings = settings ?? MidiForgeMapDefaults.CreateDefaultSettings();
        MidiForgeMapDefaults.Normalize(this.settings);
    }

    public IReadOnlyList<MidiForgeInstrumentMapSettings> GetInstrumentMaps()
        => settings.InstrumentMaps;

    public IReadOnlyList<MidiForgeDrumInstrumentMap> GetDrumkitSourceMaps()
        => MidiForgeMapDefaults.GetDrumkitSourceMaps(settings);

    public IReadOnlyList<MidiForgeDrumTransposeTarget> GetDrumTransposeTargets(MidiForgeDrumTransposePreset preset)
        => MidiForgeMapDefaults.GetEffectiveDrumTransposeTargets(settings, preset);

    public bool TryResolveInstrumentTrackName(SevenBitNumber programNumber, out string trackName)
        => MidiForgeMapDefaults.TryResolveInstrumentTrackName(settings, programNumber, out trackName);

    public bool IsMappedDrumSourceNote(int noteNumber)
        => MidiForgeMapDefaults.IsMappedDrumSourceNote(settings, noteNumber);

    public int TransposeDrumNote(int noteNumber, MidiForgeDrumTransposePreset preset)
        => MidiForgeMapDefaults.TransposeDrumNote(settings, noteNumber, preset);

    public string GetDrumkitInstrumentName(int noteNumber)
        => MidiForgeMapDefaults.GetDrumkitInstrumentName(noteNumber);
}

public static class MidiForgeMapDefaults
{
    private const int CurrentVersion = 1;

    private static readonly (uint Id, string Name, string TrackName, int Order, int[] Programs)[] InstrumentDefaults =
    [
        (0, "Piano", "Piano", 13, [0, 1, 2, 3, 4, 5, 9, 10, 11, 12, 13]),
        (1, "Harp", "Harp", 14, [16, 17, 18, 19, 46]),
        (2, "Fiddle", "Fiddle", 15, [110, 45, 55]),
        (3, "Lute", "Lute", 16, [106, 107, 108, 6, 15]),
        (4, "Fife", "Fife", 1, [72, 78]),
        (5, "Flute", "Flute", 2, [73, 74, 91, 94, 103]),
        (6, "Oboe", "Oboe", 4, [68, 109]),
        (7, "Panpipes", "Panpipes", 3, [75, 76, 77, 79, 8, 14, 52, 53, 85, 88, 92, 102, 98]),
        (8, "Clarinet", "Clarinet", 5, [71, 54, 82, 83, 86, 97, 101]),
        (9, "Trumpet", "Trumpet", 7, [56, 59, 61, 62, 93]),
        (10, "Saxophone", "Saxophone", 6, [64, 65, 66, 67, 80, 100, 21, 22, 23, 20, 111]),
        (11, "Trombone", "Trombone", 8, [57, 63, 70]),
        (12, "Horn", "Horn", 9, [60, 69, 89, 90, 95, 96]),
        (13, "Tuba", "Tuba", 22, [58, 38, 39]),
        (14, "Violin", "Violin", 10, [40, 48, 50]),
        (15, "Viola", "Viola", 11, [41, 44]),
        (16, "Cello", "Cello", 12, [42, 49, 51]),
        (17, "Double Bass", "DoubleBass", 23, [43, 32, 33, 34, 35]),
        (18, "Electric Guitar Overdriven", "ElectricGuitarOverdriven", 21, [29, 30, 84, 104, 105, 81]),
        (19, "Electric Guitar Clean", "ElectricGuitarClean", 17, [24, 25, 26, 27, 36, 37, 87, 99, 7]),
        (20, "Electric Guitar Muted", "ElectricGuitarMuted", 19, [28]),
        (21, "Electric Guitar Power Chords", "ElectricGuitarPowerChords", 20, [31]),
        (22, "Electric Guitar Special", "ElectricGuitarSpecial", 18, [120, 121, 122, 123, 124, 125, 126, 127]),
        (23, "Bass Drum", "BassDrum", 80, []),
        (26, "Snare Drum", "SnareDrum", 81, []),
        (29, "Cymbal", "Cymbal", 82, [119]),
        (30, "Bongo", "Bongo", 83, [115]),
        (33, "Timpani", "Timpani", 84, [47]),
    ];

    private static readonly (string TrackName, int[] SourceNotes)[] DrumkitSourceDefaults =
    [
        ("BassDrum", [35, 36, 41, 43, 45, 47, 48, 50]),
        ("SnareDrum", [38, 40]),
        ("Cymbal", [49, 52, 55, 57]),
        ("Bongo", [60, 61]),
        ("Timpani", []),
    ];

    private static readonly (string Category, string Instrument, int Input, int DefaultOutput, int BardForge2Output, int MogAmpOutput)[] DrumTransposeDefaults =
    [
        ("BassDrum", "Kick Drum 2", 35, 48, 53, 55),
        ("BassDrum", "Kick Drum 1", 36, 51, 55, 57),
        ("BassDrum", "Low Tom 2", 41, 56, 58, 63),
        ("BassDrum", "Low Tom 1", 43, 58, 61, 66),
        ("BassDrum", "Mid Tom 2", 45, 60, 65, 70),
        ("BassDrum", "Mid Tom 1", 47, 62, 68, 73),
        ("BassDrum", "High Tom 2", 48, 51, 71, 77),
        ("BassDrum", "High Tom 1", 50, 53, 74, 80),
        ("SnareDrum", "Snare Drum 1", 38, 62, 64, 67),
        ("SnareDrum", "Snare Drum 2", 40, 64, 66, 69),
        ("Cymbal", "Crash Cymbal", 49, 73, 71, 71),
        ("Cymbal", "Chinese Cymbal", 52, 76, 69, 69),
        ("Cymbal", "Splash Cymbal", 55, 79, 77, 77),
        ("Cymbal", "Crash Cymbal 2", 57, 81, 71, 71),
        ("Bongo", "High Bongo", 60, 60, 70, 70),
        ("Bongo", "Low Bongo", 61, 61, 67, 67),
    ];

    private static readonly IReadOnlyDictionary<int, string> DrumkitInstrumentNames = new Dictionary<int, string>
    {
        [27] = "High Q",
        [28] = "Slap",
        [29] = "Scratch Push",
        [30] = "Scratch Pull",
        [31] = "Sticks",
        [32] = "Square Click",
        [33] = "Metronome Click",
        [34] = "Metronome Bell",
        [35] = "Kick Drum 2",
        [36] = "Kick Drum 1",
        [37] = "Side Stick",
        [38] = "Snare Drum 1",
        [39] = "Hand Clap",
        [40] = "Snare Drum 2",
        [41] = "Low Tom 2",
        [42] = "Closed Hi-Hat",
        [43] = "Low Tom 1",
        [44] = "Pedal Hi-Hat",
        [45] = "Mid Tom 2",
        [46] = "Open Hi-Hat",
        [47] = "Mid Tom 1",
        [48] = "High Tom 2",
        [49] = "Crash Cymbal",
        [50] = "High Tom 1",
        [51] = "Ride Cymbal",
        [52] = "Chinese Cymbal",
        [53] = "Ride Bell",
        [54] = "Tambourine",
        [55] = "Splash Cymbal",
        [56] = "Cowbell",
        [57] = "Crash Cymbal 2",
        [58] = "Vibra-Slap",
        [59] = "Ride Cymbal 2",
        [60] = "High Bongo",
        [61] = "Low Bongo",
        [62] = "Mute Hi Conga",
        [63] = "Open Hi Conga",
        [64] = "Low Conga",
        [65] = "High Timbale",
        [66] = "Low Timbale",
        [67] = "High Agogo",
        [68] = "Low Agogo",
        [69] = "Cabasa",
        [70] = "Maracas",
        [71] = "Short Hi Whistle",
        [72] = "Long Lo Whistle",
        [73] = "Short Guiro",
        [74] = "Long Guiro",
        [75] = "Claves",
        [76] = "High Woodblock",
        [77] = "Low Woodblock",
        [78] = "Mute Cuica",
        [79] = "Open Cuica",
        [80] = "Mute Triangle",
        [81] = "Open Triangle",
        [82] = "Shaker",
        [83] = "Jingle Bell",
        [84] = "Belltree",
        [85] = "Castanets",
        [86] = "Closed Surdo",
        [87] = "Open Surdo",
    };

    public static MidiForgeMapSettings CreateDefaultSettings()
    {
        var settings = new MidiForgeMapSettings
        {
            Version = CurrentVersion,
            InstrumentMaps = InstrumentDefaults
                .Select(item => new MidiForgeInstrumentMapSettings
                {
                    InstrumentId = item.Id,
                    InstrumentName = item.Name,
                    TrackName = item.TrackName,
                    TrackOrder = item.Order,
                    MidiPrograms = item.Programs.ToList(),
                })
                .ToList(),
            DrumkitSourceMaps = DrumkitSourceDefaults
                .Select(item => new MidiForgeDrumInstrumentMapSettings
                {
                    TrackName = item.TrackName,
                    SourceNotes = item.SourceNotes.ToList(),
                })
                .ToList(),
        };

        settings.DrumTransposePresets = Enum.GetValues<MidiForgeDrumTransposePreset>()
            .Select(preset => new MidiForgeDrumTransposePresetSettings
            {
                Preset = preset,
                Entries = CreateDefaultTransposeEntries(preset)
                    .Select(target => new MidiForgeDrumTransposeMapEntry
                    {
                        Category = target.Category,
                        DrumkitInstrument = target.DrumkitInstrument,
                        InputNote = target.InputNote,
                        OutputNote = target.OutputNote,
                    })
                    .ToList(),
            })
            .ToList();

        return settings;
    }

    public static void Normalize(MidiForgeMapSettings settings)
    {
        if (settings is null)
            return;

        settings.Version = Math.Max(settings.Version, CurrentVersion);
        settings.InstrumentMaps ??= new List<MidiForgeInstrumentMapSettings>();
        settings.DrumkitSourceMaps ??= new List<MidiForgeDrumInstrumentMapSettings>();
        settings.DrumTransposePresets ??= new List<MidiForgeDrumTransposePresetSettings>();

        EnsureInstrumentDefaults(settings);
        EnsureDrumkitSourceDefaults(settings);
        EnsureTransposeDefaults(settings);

        foreach (var entry in settings.InstrumentMaps)
        {
            entry.TrackName = CleanName(entry.TrackName, entry.InstrumentName);
            entry.InstrumentName = CleanName(entry.InstrumentName, entry.TrackName);
            entry.MidiPrograms = CleanNumberList(entry.MidiPrograms);
        }

        var assignedDrumNotes = new HashSet<int>();
        foreach (var map in settings.DrumkitSourceMaps)
        {
            map.TrackName = CleanName(map.TrackName, "Drumkit");
            map.SourceNotes ??= new List<int>();
            var cleaned = new List<int>();
            foreach (var note in map.SourceNotes.Select(ClampNote).Distinct().OrderBy(note => note))
            {
                if (assignedDrumNotes.Add(note))
                    cleaned.Add(note);
            }

            map.SourceNotes = cleaned;
        }

        foreach (var preset in settings.DrumTransposePresets)
        {
            preset.Entries ??= new List<MidiForgeDrumTransposeMapEntry>();
            preset.Entries = preset.Entries
                .GroupBy(entry => ClampNote(entry.InputNote))
                .Select(group =>
                {
                    var entry = group.Last();
                    entry.InputNote = ClampNote(entry.InputNote);
                    entry.OutputNote = ClampNote(entry.OutputNote);
                    entry.Category = NormalizeDrumCategory(entry.Category);
                    entry.DrumkitInstrument = CleanName(
                        entry.DrumkitInstrument,
                        GetDrumkitInstrumentName(entry.InputNote));
                    return entry;
                })
                .OrderBy(entry => entry.InputNote)
                .ToList();
        }
    }

    public static IReadOnlyList<MidiForgeDrumInstrumentMap> GetDrumkitSourceMaps(MidiForgeMapSettings settings)
    {
        Normalize(settings);
        return settings.DrumkitSourceMaps
            .Select(map => new MidiForgeDrumInstrumentMap(
                map.TrackName,
                map.SourceNotes.ToHashSet()))
            .ToArray();
    }

    public static IReadOnlyList<MidiForgeDrumTransposeTarget> GetEffectiveDrumTransposeTargets(
        MidiForgeMapSettings settings,
        MidiForgeDrumTransposePreset preset)
    {
        Normalize(settings);

        var presetEntries = settings.DrumTransposePresets
            .FirstOrDefault(item => item.Preset == preset)
            ?.Entries
            ?.ToDictionary(entry => entry.InputNote)
            ?? new Dictionary<int, MidiForgeDrumTransposeMapEntry>();

        var targets = new List<MidiForgeDrumTransposeTarget>();
        var included = new HashSet<int>();

        foreach (var sourceMap in settings.DrumkitSourceMaps)
        {
            foreach (var inputNote in sourceMap.SourceNotes)
            {
                included.Add(inputNote);
                targets.Add(CreateEffectiveTarget(
                    inputNote,
                    sourceMap.TrackName,
                    presetEntries.TryGetValue(inputNote, out var entry) ? entry : null));
            }
        }

        foreach (var entry in presetEntries.Values.OrderBy(entry => entry.InputNote))
        {
            if (included.Contains(entry.InputNote))
                continue;

            targets.Add(CreateEffectiveTarget(entry.InputNote, entry.Category, entry));
        }

        return targets;
    }

    public static bool TryResolveInstrumentTrackName(
        MidiForgeMapSettings settings,
        SevenBitNumber programNumber,
        out string trackName)
    {
        Normalize(settings);
        var program = (byte)programNumber;
        var match = settings.InstrumentMaps
            .Where(map => map.MidiPrograms.Contains(program))
            .OrderBy(map => map.TrackOrder)
            .FirstOrDefault();

        trackName = match?.TrackName ?? string.Empty;
        return !string.IsNullOrWhiteSpace(trackName);
    }

    public static bool IsMappedDrumSourceNote(MidiForgeMapSettings settings, int noteNumber)
    {
        Normalize(settings);
        var note = ClampNote(noteNumber);
        return settings.DrumkitSourceMaps.Any(map => map.SourceNotes.Contains(note));
    }

    public static int TransposeDrumNote(
        MidiForgeMapSettings settings,
        int noteNumber,
        MidiForgeDrumTransposePreset preset)
    {
        var note = ClampNote(noteNumber);
        var target = GetEffectiveDrumTransposeTargets(settings, preset)
            .FirstOrDefault(item => item.InputNote == note);

        return target?.OutputNote ?? note;
    }

    public static string GetDrumkitInstrumentName(int noteNumber)
        => DrumkitInstrumentNames.TryGetValue(ClampNote(noteNumber), out var instrumentName)
            ? instrumentName
            : MidiForgeDrumMaps.UnknownDrumkitInstrumentName;

    public static IReadOnlyList<MidiForgeDrumTransposeTarget> CreateDefaultTransposeEntries(
        MidiForgeDrumTransposePreset preset)
    {
        var targets = DrumTransposeDefaults
            .Select(item => new MidiForgeDrumTransposeTarget(
                item.Category,
                item.Instrument,
                item.Input,
                preset switch
                {
                    MidiForgeDrumTransposePreset.BardForge2 => item.BardForge2Output,
                    MidiForgeDrumTransposePreset.MogAmp => item.MogAmpOutput,
                    _ => item.DefaultOutput,
                }))
            .ToList();

        var mappedInputs = targets.Select(target => target.InputNote).ToHashSet();
        targets.AddRange(Enumerable.Range(27, 61)
            .Where(note => !mappedInputs.Contains(note))
            .Select(note => new MidiForgeDrumTransposeTarget(
                MidiForgeDrumMaps.RestTrackName,
                GetDrumkitInstrumentName(note),
                note,
                note)));

        return targets;
    }

    private static void EnsureInstrumentDefaults(MidiForgeMapSettings settings)
    {
        foreach (var item in InstrumentDefaults)
        {
            var existing = settings.InstrumentMaps.FirstOrDefault(map => map.InstrumentId == item.Id);
            if (existing is null)
            {
                settings.InstrumentMaps.Add(new MidiForgeInstrumentMapSettings
                {
                    InstrumentId = item.Id,
                    InstrumentName = item.Name,
                    TrackName = item.TrackName,
                    TrackOrder = item.Order,
                    MidiPrograms = item.Programs.ToList(),
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(existing.InstrumentName))
                existing.InstrumentName = item.Name;
            if (string.IsNullOrWhiteSpace(existing.TrackName))
                existing.TrackName = item.TrackName;
        }
    }

    private static void EnsureDrumkitSourceDefaults(MidiForgeMapSettings settings)
    {
        foreach (var item in DrumkitSourceDefaults)
        {
            var existing = settings.DrumkitSourceMaps.FirstOrDefault(
                map => string.Equals(map.TrackName, item.TrackName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                settings.DrumkitSourceMaps.Add(new MidiForgeDrumInstrumentMapSettings
                {
                    TrackName = item.TrackName,
                    SourceNotes = item.SourceNotes.ToList(),
                });
            }
        }
    }

    private static void EnsureTransposeDefaults(MidiForgeMapSettings settings)
    {
        foreach (var preset in Enum.GetValues<MidiForgeDrumTransposePreset>())
        {
            var existing = settings.DrumTransposePresets.FirstOrDefault(item => item.Preset == preset);
            var defaultEntries = CreateDefaultTransposeEntries(preset);
            if (existing is null)
            {
                settings.DrumTransposePresets.Add(new MidiForgeDrumTransposePresetSettings
                {
                    Preset = preset,
                    Entries = defaultEntries
                        .Select(target => new MidiForgeDrumTransposeMapEntry
                        {
                            Category = target.Category,
                            DrumkitInstrument = target.DrumkitInstrument,
                            InputNote = target.InputNote,
                            OutputNote = target.OutputNote,
                        })
                        .ToList(),
                });
                continue;
            }

            existing.Entries ??= new List<MidiForgeDrumTransposeMapEntry>();
            foreach (var target in defaultEntries)
            {
                if (existing.Entries.Any(entry => ClampNote(entry.InputNote) == target.InputNote))
                    continue;

                existing.Entries.Add(new MidiForgeDrumTransposeMapEntry
                {
                    Category = target.Category,
                    DrumkitInstrument = target.DrumkitInstrument,
                    InputNote = target.InputNote,
                    OutputNote = target.OutputNote,
                });
            }
        }
    }

    private static MidiForgeDrumTransposeTarget CreateEffectiveTarget(
        int inputNote,
        string category,
        MidiForgeDrumTransposeMapEntry entry)
        => new(
            NormalizeDrumCategory(category),
            CleanName(entry?.DrumkitInstrument, GetDrumkitInstrumentName(inputNote)),
            ClampNote(inputNote),
            ClampNote(entry?.OutputNote ?? inputNote));

    private static List<int> CleanNumberList(IEnumerable<int> source)
        => (source ?? Enumerable.Empty<int>())
            .Select(ClampNote)
            .Distinct()
            .OrderBy(number => number)
            .ToList();

    private static int ClampNote(int noteNumber)
        => Math.Clamp(noteNumber, 0, 127);

    private static string CleanName(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeDrumCategory(string value)
    {
        value = CleanName(value, MidiForgeDrumMaps.RestTrackName);
        return value switch
        {
            "Bass Drum" => "BassDrum",
            "Snare Drum" => "SnareDrum",
            _ => value,
        };
    }
}
