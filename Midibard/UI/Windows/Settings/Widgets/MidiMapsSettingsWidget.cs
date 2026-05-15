using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard;

public sealed class MidiMapsSettingsWidget : Widget
{
    public override string Title => "MIDI Maps";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Map;

    private static readonly MidiForgeDrumTransposePreset[] DrumTransposePresets =
        Enum.GetValues<MidiForgeDrumTransposePreset>();

    private static readonly string[] DrumTransposePresetLabels = DrumTransposePresets
        .Select(preset => preset switch
        {
            MidiForgeDrumTransposePreset.BardForge2 => "BardForge 2",
            MidiForgeDrumTransposePreset.MogAmp => "MogAmp",
            _ => "BardForge Default",
        })
        .ToArray();

    private readonly Dictionary<uint, string> instrumentProgramBuffers = new();
    private readonly Dictionary<string, string> drumSourceBuffers = new(StringComparer.OrdinalIgnoreCase);
    private int transposePresetIndex;

    public MidiMapsSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var maps = Context.Plugin.Config.MidiForgeMaps;
        MidiForgeMapDefaults.Normalize(maps);

        DrawActions(maps);
        ImGui.Spacing();

        if (!ImGui.BeginTabBar("##MidiMapsTabs"))
            return;

        if (ImGui.BeginTabItem("Instrument Map"))
        {
            DrawInstrumentMap(maps);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Drumkit Source Map"))
        {
            DrawDrumkitSourceMap(maps);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Drum Transpose Map"))
        {
            DrawDrumTransposeMap(maps);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawActions(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Save MIDI Maps"))
            SaveMaps(maps);

        ImGui.SameLine();
        if (ImGui.Button("Reset All MIDI Maps"))
        {
            Context.Plugin.Config.MidiForgeMaps = MidiForgeMapDefaults.CreateDefaultSettings();
            ClearBuffers();
            SaveMaps(Context.Plugin.Config.MidiForgeMaps);
        }

        ImGui.TextWrapped("Instrument maps rename tracks from MIDI Program Change events. Drumkit source maps choose which drum notes become each generated drum track. Drum transpose maps choose the output note used when drum tracks are split.");
    }

    private void DrawInstrumentMap(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Reset Instrument Map"))
        {
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            maps.InstrumentMaps = defaults.InstrumentMaps;
            instrumentProgramBuffers.Clear();
            SaveMaps(maps);
        }

        ImGui.TextDisabled("Edit GM program aliases that should resolve to each FFXIV track name.");

        using var table = ImRaii.Table(
            "##InstrumentMapTable",
            4,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 320f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupColumn("Instrument", ImGuiTableColumnFlags.WidthFixed, 150f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Track Name", ImGuiTableColumnFlags.WidthFixed, 180f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Order", ImGuiTableColumnFlags.WidthFixed, 70f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("GM Programs", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var entry in maps.InstrumentMaps.OrderBy(entry => entry.TrackOrder).ThenBy(entry => entry.TrackName))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(entry.InstrumentName);

            ImGui.TableNextColumn();
            var trackName = entry.TrackName;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##instrumentTrackName{entry.InstrumentId}", ref trackName, 128))
            {
                entry.TrackName = trackName;
                SaveMaps(maps);
            }

            ImGui.TableNextColumn();
            var order = entry.TrackOrder;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt($"##instrumentTrackOrder{entry.InstrumentId}", ref order))
            {
                entry.TrackOrder = order;
                SaveMaps(maps);
            }

            ImGui.TableNextColumn();
            var buffer = GetInstrumentProgramBuffer(entry);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##instrumentPrograms{entry.InstrumentId}", ref buffer, 256))
            {
                instrumentProgramBuffers[entry.InstrumentId] = buffer;
                entry.MidiPrograms = ParseNumberList(buffer);
                SaveMaps(maps);
            }
        }
    }

    private void DrawDrumkitSourceMap(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Reset Drumkit Source Map"))
        {
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            maps.DrumkitSourceMaps = defaults.DrumkitSourceMaps;
            drumSourceBuffers.Clear();
            SaveMaps(maps);
        }

        ImGui.TextDisabled("Add source drum notes to decide which generated drum track receives them. Duplicates are kept only in the first matching row.");

        using var table = ImRaii.Table(
            "##DrumkitSourceMapTable",
            3,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn("Drum Track", ImGuiTableColumnFlags.WidthFixed, 160f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Source Notes", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Names", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var entry in maps.DrumkitSourceMaps)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var trackName = entry.TrackName;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##drumSourceName{entry.TrackName}", ref trackName, 128))
            {
                entry.TrackName = trackName;
                SaveMaps(maps);
            }

            ImGui.TableNextColumn();
            var buffer = GetDrumSourceBuffer(entry);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##drumSourceNotes{entry.TrackName}", ref buffer, 256))
            {
                drumSourceBuffers[entry.TrackName] = buffer;
                entry.SourceNotes = ParseNumberList(buffer);
                SaveMaps(maps);
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.Join(", ", entry.SourceNotes.Select(FormatDrumNote)));
        }
    }

    private void DrawDrumTransposeMap(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Reset Current Transpose Preset"))
        {
            var resetPreset = DrumTransposePresets[Math.Clamp(transposePresetIndex, 0, DrumTransposePresets.Length - 1)];
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            var defaultPreset = defaults.DrumTransposePresets.Single(item => item.Preset == resetPreset);
            var current = maps.DrumTransposePresets.Single(item => item.Preset == resetPreset);
            current.Entries = defaultPreset.Entries;
            SaveMaps(maps);
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset All Transpose Presets"))
        {
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            maps.DrumTransposePresets = defaults.DrumTransposePresets;
            SaveMaps(maps);
        }

        transposePresetIndex = Math.Clamp(transposePresetIndex, 0, DrumTransposePresetLabels.Length - 1);
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        ImGui.Combo(
            "Preset##midiMapsTransposePreset",
            ref transposePresetIndex,
            DrumTransposePresetLabels,
            DrumTransposePresetLabels.Length);

        var preset = DrumTransposePresets[transposePresetIndex];
        var provider = new ConfigurationEditorMidiMapProvider(maps);
        var targets = provider.GetDrumTransposeTargets(preset);
        var presetSettings = maps.DrumTransposePresets.Single(item => item.Preset == preset);

        ImGui.TextDisabled("Rows include BardForge defaults plus any notes assigned in the Drumkit Source Map. New source notes stay unchanged until edited.");

        using var table = ImRaii.Table(
            "##DrumTransposeMapTable",
            5,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 320f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed, 130f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthFixed, 160f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Output Note", ImGuiTableColumnFlags.WidthFixed, 100f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Output", ImGuiTableColumnFlags.WidthFixed, 160f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var target in targets.OrderBy(target => target.Category).ThenBy(target => target.InputNote))
        {
            var entry = GetOrCreateTransposeEntry(presetSettings, target);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(target.Category);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"{target.InputNote} - {target.DrumkitInstrument}");

            ImGui.TableNextColumn();
            var outputNote = entry.OutputNote;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt($"##transposeOutput{preset}{target.InputNote}", ref outputNote))
            {
                entry.OutputNote = Math.Clamp(outputNote, 0, 127);
                SaveMaps(maps);
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(FormatDrumNote(entry.OutputNote));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(entry.InputNote == entry.OutputNote ? "unchanged" : "mapped");
        }
    }

    private string GetInstrumentProgramBuffer(MidiForgeInstrumentMapSettings entry)
    {
        if (!instrumentProgramBuffers.TryGetValue(entry.InstrumentId, out var buffer))
        {
            buffer = string.Join(", ", entry.MidiPrograms);
            instrumentProgramBuffers[entry.InstrumentId] = buffer;
        }

        return buffer;
    }

    private string GetDrumSourceBuffer(MidiForgeDrumInstrumentMapSettings entry)
    {
        if (!drumSourceBuffers.TryGetValue(entry.TrackName, out var buffer))
        {
            buffer = string.Join(", ", entry.SourceNotes);
            drumSourceBuffers[entry.TrackName] = buffer;
        }

        return buffer;
    }

    private static MidiForgeDrumTransposeMapEntry GetOrCreateTransposeEntry(
        MidiForgeDrumTransposePresetSettings presetSettings,
        MidiForgeDrumTransposeTarget target)
    {
        var entry = presetSettings.Entries.FirstOrDefault(entry => entry.InputNote == target.InputNote);
        if (entry is not null)
            return entry;

        entry = new MidiForgeDrumTransposeMapEntry
        {
            Category = target.Category,
            DrumkitInstrument = target.DrumkitInstrument,
            InputNote = target.InputNote,
            OutputNote = target.OutputNote,
        };
        presetSettings.Entries.Add(entry);
        return entry;
    }

    private void SaveMaps(MidiForgeMapSettings maps)
    {
        MidiForgeMapDefaults.Normalize(maps);
        Context.Plugin.IpcProvider.SyncAllSettings();
    }

    private void ClearBuffers()
    {
        instrumentProgramBuffers.Clear();
        drumSourceBuffers.Clear();
    }

    private static List<int> ParseNumberList(string value)
        => (value ?? string.Empty)
            .Split([',', ' ', ';', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part.Trim(), out var number) ? Math.Clamp(number, 0, 127) : (int?)null)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .Distinct()
            .OrderBy(number => number)
            .ToList();

    private static string FormatDrumNote(int noteNumber)
        => $"{Math.Clamp(noteNumber, 0, 127)} - {MidiForgeMapDefaults.GetDrumkitInstrumentName(noteNumber)}";
}
