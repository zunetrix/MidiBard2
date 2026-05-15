using System;
using System.Collections.Generic;
using System.Linq;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Control.MidiControl.Editing;

internal sealed record MidiForgeProgramMapOption(
    int ProgramNumber,
    string Name,
    string Category,
    string Label);

internal sealed record MidiForgeDrumNoteMapOption(
    int NoteNumber,
    string NoteName,
    string DrumkitInstrument,
    string Category,
    string Label);

internal sealed record MidiForgeInstrumentTargetDisplay(
    uint InstrumentId,
    string InstrumentName,
    string TrackName,
    int TrackOrder);

internal static class MidiForgeMapOptionCatalog
{
    private static readonly string[] GeneralMidiCategories =
    [
        "Piano",
        "Chromatic Percussion",
        "Organ",
        "Guitar",
        "Bass",
        "Strings",
        "Ensemble",
        "Brass",
        "Reed",
        "Pipe",
        "Synth Lead",
        "Synth Pad",
        "Synth Effects",
        "Ethnic",
        "Percussive",
        "Sound Effects",
    ];

    private static readonly IReadOnlyDictionary<uint, string> InstrumentRangeLabels = new Dictionary<uint, string>
    {
        [0] = "C4-C7",
        [1] = "C3-C6",
        [2] = "C2-C5",
        [3] = "C2-C5",
        [4] = "C5-C8",
        [5] = "C4-C7",
        [6] = "C4-C7",
        [7] = "C3-C6",
        [8] = "C3-C6",
        [9] = "C3-C6",
        [10] = "C3-C6",
        [11] = "C2-C5",
        [12] = "C2-C5",
        [13] = "C1-C4",
        [14] = "C3-C6",
        [15] = "C3-C6",
        [16] = "C2-C5",
        [17] = "C1-C4",
        [18] = "C2-C5",
        [19] = "C2-C5",
        [20] = "C2-C5",
        [21] = "C1-C4",
        [22] = "C2-F2 | F2#-C3 | C#3-G3 | G#3-E5 | F5-C6",
        [33] = "C2-C5",
    };

    public static IReadOnlyList<MidiForgeProgramMapOption> ProgramOptions { get; } =
        Enumerable.Range(0, 128)
            .Select(program =>
            {
                var name = DryWetMidiExtensions.GetGMProgramName((byte)program);
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Program {program}";

                var category = GeneralMidiCategories[Math.Clamp(program / 8, 0, GeneralMidiCategories.Length - 1)];
                return new MidiForgeProgramMapOption(
                    program,
                    name,
                    category,
                    $"{program} - {name}");
            })
            .ToArray();

    public static IReadOnlyList<MidiForgeDrumNoteMapOption> DrumNoteOptions { get; } =
        Enumerable.Range(0, 128)
            .Select(note =>
            {
                var noteName = MidiForgeNotePrimitives.GetMidiNoteName(note);
                var instrument = MidiForgeMapDefaults.GetDrumkitInstrumentName(note);
                var category = note is >= 27 and <= 87 ? "GM Drumkit" : "Other MIDI Notes";
                var label = instrument == MidiForgeDrumMaps.UnknownDrumkitInstrumentName
                    ? $"{note} - {noteName}"
                    : $"{note} - {noteName} - {instrument}";

                return new MidiForgeDrumNoteMapOption(
                    note,
                    noteName,
                    instrument,
                    category,
                    label);
            })
            .ToArray();

    public static IReadOnlyList<MidiForgeInstrumentTargetDisplay> BuildInstrumentTargets(MidiForgeMapSettings settings)
    {
        MidiForgeMapDefaults.Normalize(settings);
        return settings.InstrumentMaps
            .OrderBy(map => map.TrackOrder)
            .ThenBy(map => map.TrackName, StringComparer.OrdinalIgnoreCase)
            .Select(map => new MidiForgeInstrumentTargetDisplay(
                map.InstrumentId,
                map.InstrumentName,
                map.TrackName,
                map.TrackOrder))
            .ToArray();
    }

    public static bool MoveInstrumentTarget(MidiForgeMapSettings settings, uint instrumentId, int direction)
    {
        if (direction == 0)
            return false;

        MidiForgeMapDefaults.Normalize(settings);
        var ordered = settings.InstrumentMaps
            .OrderBy(map => map.TrackOrder)
            .ThenBy(map => map.TrackName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = ordered.FindIndex(map => map.InstrumentId == instrumentId);
        if (index < 0)
            return false;

        var targetIndex = Math.Clamp(index + Math.Sign(direction), 0, ordered.Count - 1);
        if (targetIndex == index)
            return false;

        var orderValues = ordered.Select(map => map.TrackOrder).OrderBy(value => value).ToArray();
        var moving = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(targetIndex, moving);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].TrackOrder = orderValues[i];

        settings.InstrumentMaps = ordered;
        return true;
    }

    public static bool TryGetInstrumentRangeLabel(uint instrumentId, out string rangeLabel)
    {
        if (InstrumentRangeLabels.TryGetValue(instrumentId, out var value) &&
            !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "-", StringComparison.Ordinal))
        {
            rangeLabel = value;
            return true;
        }

        rangeLabel = string.Empty;
        return false;
    }

    public static bool IsProgramAssignedToAnotherTarget(
        MidiForgeMapSettings settings,
        MidiForgeInstrumentMapSettings currentTarget,
        int programNumber)
    {
        MidiForgeMapDefaults.Normalize(settings);
        var program = Math.Clamp(programNumber, 0, 127);
        return settings.InstrumentMaps.Any(map =>
            !ReferenceEquals(map, currentTarget) &&
            map.MidiPrograms.Contains(program));
    }

    public static bool ShouldDisableProgramOption(
        MidiForgeMapSettings settings,
        MidiForgeInstrumentMapSettings currentTarget,
        int programNumber)
    {
        var program = Math.Clamp(programNumber, 0, 127);
        return !(currentTarget.MidiPrograms?.Contains(program) ?? false) &&
               IsProgramAssignedToAnotherTarget(settings, currentTarget, program);
    }

    public static bool IsDrumNoteAssignedToAnotherTarget(
        MidiForgeMapSettings settings,
        MidiForgeDrumInstrumentMapSettings currentTarget,
        int noteNumber)
    {
        MidiForgeMapDefaults.Normalize(settings);
        var note = Math.Clamp(noteNumber, 0, 127);
        return settings.DrumkitSourceMaps.Any(map =>
            !ReferenceEquals(map, currentTarget) &&
            map.SourceNotes.Contains(note));
    }

    public static bool ShouldDisableDrumNoteOption(
        MidiForgeMapSettings settings,
        MidiForgeDrumInstrumentMapSettings currentTarget,
        int noteNumber)
    {
        var note = Math.Clamp(noteNumber, 0, 127);
        return !(currentTarget.SourceNotes?.Contains(note) ?? false) &&
               IsDrumNoteAssignedToAnotherTarget(settings, currentTarget, note);
    }

    public static bool IsPercussionTarget(MidiForgeInstrumentMapSettings target)
        => target.InstrumentId is 23 or 26 or 29 or 30 or 33 ||
           IsPercussionTrackName(target.TrackName) ||
           IsPercussionTrackName(target.InstrumentName);

    private static bool IsPercussionTrackName(string value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return sanitized.Equals("BassDrum", StringComparison.OrdinalIgnoreCase) ||
               sanitized.Equals("SnareDrum", StringComparison.OrdinalIgnoreCase) ||
               sanitized.Equals("Cymbal", StringComparison.OrdinalIgnoreCase) ||
               sanitized.Equals("Bongo", StringComparison.OrdinalIgnoreCase) ||
               sanitized.Equals("Timpani", StringComparison.OrdinalIgnoreCase);
    }
}
