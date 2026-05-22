using System;
using System.Collections.Generic;
using System.Linq;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard;

internal sealed record MidiEditorTrackNameOption(
    string DisplayName,
    uint IconId,
    uint? PickerInstrumentId = null);

internal static class MidiEditorTrackNameOptions
{
    public const uint DefaultIconId = 60042;
    public const string ProgramElectricGuitarTrackName = "Program: ElectricGuitar";

    public static IReadOnlyList<MidiEditorTrackNameOption> Build(
        IEditorMidiMapProvider mapProvider,
        IReadOnlyDictionary<string, uint> iconByTrackName,
        uint programElectricGuitarIcon)
    {
        mapProvider ??= DefaultEditorMidiMapProvider.Instance;
        iconByTrackName ??= new Dictionary<string, uint>();

        var options = new List<MidiEditorTrackNameOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var map in mapProvider.GetInstrumentMaps()
            .OrderBy(map => map.TrackOrder)
            .ThenBy(map => map.TrackName, StringComparer.OrdinalIgnoreCase))
        {
            var iconId = ResolveIconId(map, iconByTrackName);
            AddOption(
                options,
                seen,
                map.TrackName,
                iconId,
                ResolvePickerInstrumentId(map.TrackName, map.InstrumentName));
        }

        AddOption(options, seen, ProgramElectricGuitarTrackName, programElectricGuitarIcon);
        return options;
    }

    public static IReadOnlyList<MidiEditorTrackNameOption> GetQuickPickerOptions(
        IReadOnlyList<MidiEditorTrackNameOption> options)
        => options
            .Where(option => option.PickerInstrumentId.HasValue)
            .OrderBy(option => option.PickerInstrumentId!.Value)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static uint ResolveIconId(
        MidiForgeInstrumentMapSettings map,
        IReadOnlyDictionary<string, uint> iconByTrackName)
    {
        if (iconByTrackName.TryGetValue(map.TrackName, out var iconId) ||
            iconByTrackName.TryGetValue(map.InstrumentName, out iconId))
        {
            return iconId;
        }

        return DefaultIconId;
    }

    private static void AddOption(
        List<MidiEditorTrackNameOption> options,
        HashSet<string> seen,
        string displayName,
        uint iconId,
        uint? pickerInstrumentId = null)
    {
        displayName = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayName) || !seen.Add(displayName))
            return;

        options.Add(new MidiEditorTrackNameOption(displayName, iconId, pickerInstrumentId));
    }

    private static uint? ResolvePickerInstrumentId(params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (TrackInfo.GetInstrumentIdByName(name) is { } instrumentId)
                return instrumentId;
        }

        return null;
    }
}
