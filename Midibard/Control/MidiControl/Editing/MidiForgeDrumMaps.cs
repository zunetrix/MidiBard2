using System.Collections.Generic;
using System.Linq;

namespace MidiBard.Control.MidiControl.Editing;

public sealed record MidiForgeDrumInstrumentMap(string TrackName, IReadOnlySet<int> SourceNotes);

public sealed record MidiForgeDrumTransposeTarget(
    string Category,
    string DrumkitInstrument,
    int InputNote,
    int OutputNote);

public enum MidiForgeDrumTransposePreset
{
    Default,
    BardForge2,
    MogAmp,
}

public static class MidiForgeDrumMaps
{
    public const string RestTrackName = "Drumkit Rest";
    public const string UnknownDrumkitInstrumentName = "Drumkit Unknown";

    public static readonly IReadOnlyList<MidiForgeDrumInstrumentMap> DefaultDrumkitMappings =
    [
        new("BassDrum", new HashSet<int> { 35, 36, 41, 43, 45, 47, 48, 50 }),
        new("SnareDrum", new HashSet<int> { 38, 40 }),
        new("Cymbal", new HashSet<int> { 49, 52, 55, 57 }),
        new("Bongo", new HashSet<int> { 60, 61 }),
        new("Timpani", new HashSet<int>()),
    ];

    public static readonly IReadOnlyList<MidiForgeDrumTransposeTarget> DefaultTransposeTargets =
    [
        new("BassDrum", "Kick Drum 2", 35, 48),
        new("BassDrum", "Kick Drum 1", 36, 51),
        new("BassDrum", "Low Tom 2", 41, 56),
        new("BassDrum", "Low Tom 1", 43, 58),
        new("BassDrum", "Mid Tom 2", 45, 60),
        new("BassDrum", "Mid Tom 1", 47, 62),
        new("BassDrum", "High Tom 2", 48, 51),
        new("BassDrum", "High Tom 1", 50, 53),
        new("SnareDrum", "Snare Drum 1", 38, 62),
        new("SnareDrum", "Snare Drum 2", 40, 64),
        new("Cymbal", "Crash Cymbal", 49, 73),
        new("Cymbal", "Chinese Cymbal", 52, 76),
        new("Cymbal", "Splash Cymbal", 55, 79),
        new("Cymbal", "Crash Cymbal 2", 57, 81),
        new("Bongo", "High Bongo", 60, 60),
        new("Bongo", "Low Bongo", 61, 61),
    ];

    public static readonly IReadOnlyList<MidiForgeDrumTransposeTarget> BardForge2TransposeTargets =
    [
        new("BassDrum", "Kick Drum 2", 35, 53),
        new("BassDrum", "Kick Drum 1", 36, 55),
        new("BassDrum", "Low Tom 2", 41, 58),
        new("BassDrum", "Low Tom 1", 43, 61),
        new("BassDrum", "Mid Tom 2", 45, 65),
        new("BassDrum", "Mid Tom 1", 47, 68),
        new("BassDrum", "High Tom 2", 48, 71),
        new("BassDrum", "High Tom 1", 50, 74),
        new("SnareDrum", "Snare Drum 1", 38, 64),
        new("SnareDrum", "Snare Drum 2", 40, 66),
        new("Cymbal", "Crash Cymbal", 49, 71),
        new("Cymbal", "Chinese Cymbal", 52, 69),
        new("Cymbal", "Splash Cymbal", 55, 77),
        new("Cymbal", "Crash Cymbal 2", 57, 71),
        new("Bongo", "High Bongo", 60, 70),
        new("Bongo", "Low Bongo", 61, 67),
    ];

    public static readonly IReadOnlyList<MidiForgeDrumTransposeTarget> MogAmpTransposeTargets =
    [
        new("BassDrum", "Kick Drum 2", 35, 55),
        new("BassDrum", "Kick Drum 1", 36, 57),
        new("BassDrum", "Low Tom 2", 41, 63),
        new("BassDrum", "Low Tom 1", 43, 66),
        new("BassDrum", "Mid Tom 2", 45, 70),
        new("BassDrum", "Mid Tom 1", 47, 73),
        new("BassDrum", "High Tom 2", 48, 77),
        new("BassDrum", "High Tom 1", 50, 80),
        new("SnareDrum", "Snare Drum 1", 38, 67),
        new("SnareDrum", "Snare Drum 2", 40, 69),
        new("Cymbal", "Crash Cymbal", 49, 71),
        new("Cymbal", "Chinese Cymbal", 52, 69),
        new("Cymbal", "Splash Cymbal", 55, 77),
        new("Cymbal", "Crash Cymbal 2", 57, 71),
        new("Bongo", "High Bongo", 60, 70),
        new("Bongo", "Low Bongo", 61, 67),
    ];

    private static readonly IReadOnlySet<int> MappedSourceNotes = DefaultDrumkitMappings
        .SelectMany(mapping => mapping.SourceNotes)
        .ToHashSet();

    public static IReadOnlyList<MidiForgeDrumTransposeTarget> GetTransposeTargets(
        MidiForgeDrumTransposePreset preset)
        => MidiForgeMapDefaults.CreateDefaultTransposeEntries(preset);

    public static bool IsMappedSourceNote(int noteNumber)
        => MappedSourceNotes.Contains(noteNumber);

    public static int TransposeToDefaultOutputNote(int noteNumber)
        => TransposeToOutputNote(noteNumber, MidiForgeDrumTransposePreset.Default);

    public static int TransposeToOutputNote(int noteNumber, MidiForgeDrumTransposePreset preset)
        => GetTransposeTargets(preset)
            .FirstOrDefault(target => target.InputNote == noteNumber)
            ?.OutputNote
            ?? noteNumber;

    public static string GetDrumkitInstrumentName(int noteNumber)
        => DrumkitInstrumentNames.TryGetValue(noteNumber, out var instrumentName)
            ? instrumentName
            : UnknownDrumkitInstrumentName;

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

}
