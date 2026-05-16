using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Extensions.Dalamud;
using MidiBard.Util;

namespace MidiBard;

public sealed class MidiMapsSettingsWidget : Widget
{
    public override string Title => "MIDI Maps";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Map;

    private const string MapsOverviewHelp =
        "These settings tell MIDI editor operations how to recognize instruments and drum notes. Changing a map does not edit the current MIDI by itself. The maps are used by Map Instruments, Prepare for Playback, and drumkit split/transpose operations.";

    private const string InstrumentMapHelp =
        "Choose which existing track names and GM Program Change values should resolve to each Midibard track name. Used by Map Instruments and Prepare for Playback when Name source is Game instrument map.";

    private const string InstrumentTargetHelp =
        "The Midibard-supported track name that map operations write when this row matches. This target is fixed here.";

    private const string InstrumentProgramHelp =
        "GM programs that should map to this target when a source track has a Program Change event. Disabled programs are already assigned to another target.";

    private const string InstrumentAliasHelp =
        "Existing MIDI track names that should count as this target, such as Clean or Power Chords. Separate aliases with commas. Aliases do not change the target name; they help Map Instruments recognize source tracks.";

    private const string InstrumentOrderHelp =
        "Controls priority when more than one map could match. Earlier targets win.";

    private const string DrumkitSourceMapHelp =
        "Choose which source drum notes belong to each generated FFXIV drum track. Used by Map Instruments for drum-track naming and by drumkit split operations.";

    private const string DrumTargetHelp =
        "The generated Midibard drum track name for notes in this row.";

    private const string DrumSourceNotesHelp =
        "MIDI drum notes that should be treated as this drum target. A note can only belong to one target.";

    private const string DrumTransposeMapHelp =
        "Choose what note each source drum note becomes after drumkit split/transpose operations. Notes not changed by the preset are shown as unchanged.";

    private const string DrumTransposePresetHelp =
        "Select the drum transpose preset to edit. Reset buttons restore BardForge-compatible defaults.";

    private const string DrumTransposeOutputHelp =
        "The note written for this input drum note when this preset is used.";

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

    private readonly Dictionary<uint, string> instrumentAliasBuffers = new();
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

        ImGui.TextWrapped(MapsOverviewHelp);
    }

    private void DrawInstrumentMap(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Reset Instrument Map"))
        {
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            maps.InstrumentMaps = defaults.InstrumentMaps;
            instrumentAliasBuffers.Clear();
            SaveMaps(maps);
        }

        ImGui.TextDisabled(InstrumentMapHelp);

        using var table = ImRaii.Table(
            "##InstrumentMapTable",
            4,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 320f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupColumn("Order", ImGuiTableColumnFlags.WidthFixed, 48f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("FFXIV Target", ImGuiTableColumnFlags.WidthFixed, 320f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("GM Programs", ImGuiTableColumnFlags.WidthFixed, 260f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Source Name Aliases", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var orderedEntries = maps.InstrumentMaps
            .OrderBy(entry => entry.TrackOrder)
            .ThenBy(entry => entry.TrackName)
            .Select((entry, index) => (entry, index))
            .ToArray();
        foreach (var (entry, rowIndex) in orderedEntries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawInstrumentOrderControls(maps, entry, rowIndex, orderedEntries.Length);

            ImGui.TableNextColumn();
            DrawInstrumentMapTarget(entry);

            ImGui.TableNextColumn();
            DrawProgramMultiSelect(maps, entry, rowIndex);

            ImGui.TableNextColumn();
            var aliasBuffer = GetInstrumentAliasBuffer(entry);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##instrumentAliases{entry.InstrumentId}", ref aliasBuffer, 512))
            {
                instrumentAliasBuffers[entry.InstrumentId] = aliasBuffer;
                entry.TrackNameAliases = ParseTextList(aliasBuffer);
                SaveMaps(maps);
            }
            ImGuiUtil.ToolTip(InstrumentAliasHelp);
        }
    }

    private static void DrawInstrumentMapTarget(MidiForgeInstrumentMapSettings entry)
    {
        DrawInstrumentIcon(ResolveInstrumentIconId(entry));
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        var targetName = string.IsNullOrWhiteSpace(entry.TrackName)
            ? entry.InstrumentName
            : entry.TrackName;
        ImGui.TextUnformatted(targetName);

        var tooltip = InstrumentTargetHelp;
        if (!NamesMatchIgnoringSpacing(entry.InstrumentName, targetName))
            tooltip += $"\nDisplay name: {entry.InstrumentName}";
        if (MidiForgeMapOptionCatalog.TryGetInstrumentRangeLabel(entry.InstrumentId, out var rangeLabel))
            tooltip += $"\nApproximate sounding range: {rangeLabel}\nInformational only; map commands do not transpose to this range.";

        ImGuiUtil.ToolTip(tooltip);
    }

    private static bool NamesMatchIgnoringSpacing(string left, string right)
        => string.Equals(
            NormalizeNameForDisplayComparison(left),
            NormalizeNameForDisplayComparison(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeNameForDisplayComparison(string value)
        => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());

    private void DrawInstrumentOrderControls(
        MidiForgeMapSettings maps,
        MidiForgeInstrumentMapSettings entry,
        int rowIndex,
        int rowCount)
    {
        var buttonSize = new System.Numerics.Vector2(
            ImGui.GetTextLineHeightWithSpacing(),
            ImGui.GetTextLineHeightWithSpacing());
        using var spacing = ImRaii.PushStyle(
            ImGuiStyleVar.ItemSpacing,
            new System.Numerics.Vector2(2f * ImGuiHelpers.GlobalScale, 0));

        using (ImRaii.Disabled(rowIndex <= 0))
        {
            if (ImGui.Button($"↑##moveInstrumentMapUp{entry.InstrumentId}", buttonSize))
            {
                if (MidiForgeMapOptionCatalog.MoveInstrumentTarget(maps, entry.InstrumentId, -1))
                    SaveMaps(maps);
            }
            ImGuiUtil.ToolTip($"Move earlier.\n{InstrumentOrderHelp}");
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(rowIndex >= rowCount - 1))
        {
            if (ImGui.Button($"↓##moveInstrumentMapDown{entry.InstrumentId}", buttonSize))
            {
                if (MidiForgeMapOptionCatalog.MoveInstrumentTarget(maps, entry.InstrumentId, 1))
                    SaveMaps(maps);
            }
            ImGuiUtil.ToolTip($"Move later.\n{InstrumentOrderHelp}");
        }
    }

    private void DrawProgramMultiSelect(
        MidiForgeMapSettings maps,
        MidiForgeInstrumentMapSettings entry,
        int rowIndex)
    {
        var selectedPrograms = entry.MidiPrograms?.ToHashSet() ?? new HashSet<int>();
        var preview = FormatProgramSelectionPreview(selectedPrograms);

        ImGui.SetNextItemWidth(-1);
        var comboOpen = ImGui.BeginCombo(
            $"##instrumentPrograms{entry.InstrumentId}_{rowIndex}",
            preview,
            ImGuiComboFlags.HeightLarge);
        ImGuiUtil.ToolTip(InstrumentProgramHelp);
        if (!comboOpen)
            return;

        string? currentCategory = null;
        foreach (var option in MidiForgeMapOptionCatalog.ProgramOptions)
        {
            if (!string.Equals(currentCategory, option.Category, StringComparison.Ordinal))
            {
                currentCategory = option.Category;
                DrawComboSectionLabel(currentCategory);
            }

            var selected = selectedPrograms.Contains(option.ProgramNumber);
            var disabled = MidiForgeMapOptionCatalog.ShouldDisableProgramOption(
                maps,
                entry,
                option.ProgramNumber);

            using (ImRaii.Disabled(disabled))
            {
                var label = $"{(selected ? "[x]" : "[ ]")} {option.Label}##gmProgram{entry.InstrumentId}_{option.ProgramNumber}";
                if (!disabled && ImGui.Selectable(label, selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    if (selected)
                        selectedPrograms.Remove(option.ProgramNumber);
                    else
                        selectedPrograms.Add(option.ProgramNumber);

                    entry.MidiPrograms = selectedPrograms.OrderBy(program => program).ToList();
                    SaveMaps(maps);
                }
            }
        }

        ImGui.EndCombo();
    }

    private static string FormatProgramSelectionPreview(HashSet<int> selectedPrograms)
    {
        if (selectedPrograms.Count == 0)
            return "No GM programs";

        var names = MidiForgeMapOptionCatalog.ProgramOptions
            .Where(option => selectedPrograms.Contains(option.ProgramNumber))
            .OrderBy(option => option.ProgramNumber)
            .Select(option => option.Name)
            .ToArray();

        var preview = string.Join(", ", names);
        return names.Length <= 2 && preview.Length <= 42
            ? preview
            : $"{names.Length} GM programs selected";
    }

    private static uint ResolveInstrumentIconId(MidiForgeInstrumentMapSettings entry)
    {
        var instruments = InstrumentHelper.Instruments;
        if (instruments is null || instruments.Length == 0)
            return MidiEditorTrackNameOptions.DefaultIconId;

        var defaultEntry = MidiForgeMapDefaults.CreateDefaultSettings()
            .InstrumentMaps
            .FirstOrDefault(map => map.InstrumentId == entry.InstrumentId);

        var candidates = new[]
            {
                entry.TrackName,
                entry.InstrumentName,
                defaultEntry?.TrackName,
                defaultEntry?.InstrumentName,
                InstrumentHelper.SanitizeName(entry.TrackName ?? string.Empty),
                InstrumentHelper.SanitizeName(entry.InstrumentName ?? string.Empty),
                InstrumentHelper.SanitizeName(defaultEntry?.TrackName ?? string.Empty),
                InstrumentHelper.SanitizeName(defaultEntry?.InstrumentName ?? string.Empty),
            }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var instrument in instruments)
        {
            var displayName = instrument.FFXIVDisplayName ?? string.Empty;
            var sanitizedDisplayName = InstrumentHelper.SanitizeName(displayName);
            if (candidates.Any(candidate =>
                    string.Equals(candidate, displayName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate, sanitizedDisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                return instrument.IconId;
            }
        }

        return MidiEditorTrackNameOptions.DefaultIconId;
    }

    private static void DrawInstrumentIcon(uint iconId)
        => DalamudApi.TextureProvider.DrawIcon(
            iconId,
            ImGuiHelpers.ScaledVector2(ImGui.GetTextLineHeightWithSpacing()));

    private static void DrawComboSectionLabel(string label)
    {
        ImGui.Separator();
        ImGui.TextDisabled(label);
    }

    private void DrawDrumkitSourceMap(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Reset Drumkit Source Map"))
        {
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            maps.DrumkitSourceMaps = defaults.DrumkitSourceMaps;
            SaveMaps(maps);
        }

        ImGui.TextDisabled(DrumkitSourceMapHelp);

        using var table = ImRaii.Table(
            "##DrumkitSourceMapTable",
            3,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 320f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupColumn("Drum Track", ImGuiTableColumnFlags.WidthFixed, 220f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Source Notes", ImGuiTableColumnFlags.WidthFixed, 300f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Selected Notes", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var (entry, rowIndex) in maps.DrumkitSourceMaps.Select((entry, index) => (entry, index)))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawDrumTarget(entry);

            ImGui.TableNextColumn();
            DrawDrumNoteMultiSelect(maps, entry, rowIndex);

            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.Join(", ", entry.SourceNotes.Select(FormatDrumNote)));
        }

    }

    private static void DrawDrumTarget(MidiForgeDrumInstrumentMapSettings entry)
    {
        var iconId = ResolveInstrumentIconId(new MidiForgeInstrumentMapSettings
        {
            InstrumentId = entry.InstrumentId,
            InstrumentName = entry.TrackName,
            TrackName = entry.TrackName,
        });

        DrawInstrumentIcon(iconId);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(entry.TrackName);
        ImGuiUtil.ToolTip(DrumTargetHelp);
    }

    private void DrawDrumNoteMultiSelect(
        MidiForgeMapSettings maps,
        MidiForgeDrumInstrumentMapSettings entry,
        int rowIndex)
    {
        var selectedNotes = entry.SourceNotes?.ToHashSet() ?? new HashSet<int>();
        var preview = selectedNotes.Count == 0
            ? "No source notes"
            : $"{selectedNotes.Count} source note(s)";

        ImGui.SetNextItemWidth(-1);
        var comboOpen = ImGui.BeginCombo(
            $"##drumSourceNotes{rowIndex}_{entry.InstrumentId}",
            preview,
            ImGuiComboFlags.HeightLarge);
        ImGuiUtil.ToolTip(DrumSourceNotesHelp);
        if (!comboOpen)
            return;

        string? currentCategory = null;
        foreach (var option in MidiForgeMapOptionCatalog.DrumNoteOptions)
        {
            if (!string.Equals(currentCategory, option.Category, StringComparison.Ordinal))
            {
                currentCategory = option.Category;
                DrawComboSectionLabel(currentCategory);
            }

            var selected = selectedNotes.Contains(option.NoteNumber);
            var disabled = MidiForgeMapOptionCatalog.ShouldDisableDrumNoteOption(
                maps,
                entry,
                option.NoteNumber);

            using (ImRaii.Disabled(disabled))
            {
                var label = $"{(selected ? "[x]" : "[ ]")} {option.Label}##sourceDrumNote{rowIndex}_{option.NoteNumber}";
                if (!disabled && ImGui.Selectable(label, selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    if (selected)
                        selectedNotes.Remove(option.NoteNumber);
                    else
                        selectedNotes.Add(option.NoteNumber);

                    entry.SourceNotes = selectedNotes.OrderBy(note => note).ToList();
                    SaveMaps(maps);
                }
            }
        }

        ImGui.EndCombo();
    }

    private void DrawDrumTransposeMap(MidiForgeMapSettings maps)
    {
        if (ImGui.Button("Reset Current Transpose Preset"))
        {
            var resetPreset = DrumTransposePresets[Math.Clamp(transposePresetIndex, 0, DrumTransposePresets.Length - 1)];
            var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
            var defaultPreset = defaults.DrumTransposePresets.First(item => item.Preset == resetPreset);
            maps.DrumTransposePresets.RemoveAll(item => item.Preset == resetPreset);
            maps.DrumTransposePresets.Add(CloneTransposePreset(defaultPreset));
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
        ImGuiUtil.ToolTip(DrumTransposePresetHelp);

        var preset = DrumTransposePresets[transposePresetIndex];
        var provider = new ConfigurationEditorMidiMapProvider(maps);
        var targets = provider.GetDrumTransposeTargets(preset);
        var presetSettings = GetOrCreatePresetSettings(maps, preset);

        ImGui.TextDisabled(DrumTransposeMapHelp);

        using var table = ImRaii.Table(
            "##DrumTransposeMapTable",
            5,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 320f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed, 130f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthFixed, 160f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Output Note", ImGuiTableColumnFlags.WidthFixed, 260f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Output", ImGuiTableColumnFlags.WidthFixed, 180f * ImGuiHelpers.GlobalScale);
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
            DrawOutputDrumNoteCombo(maps, entry, preset);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(FormatDrumNote(entry.OutputNote));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(entry.InputNote == entry.OutputNote ? "unchanged" : "mapped");
        }
    }

    private void DrawOutputDrumNoteCombo(
        MidiForgeMapSettings maps,
        MidiForgeDrumTransposeMapEntry entry,
        MidiForgeDrumTransposePreset preset)
    {
        var selectedNote = Math.Clamp(entry.OutputNote, 0, 127);
        var preview = MidiForgeMapOptionCatalog.DrumNoteOptions
            .First(option => option.NoteNumber == selectedNote)
            .Label;

        ImGui.SetNextItemWidth(-1);
        var comboOpen = ImGui.BeginCombo(
            $"##transposeOutput{preset}{entry.InputNote}",
            preview,
            ImGuiComboFlags.HeightLarge);
        ImGuiUtil.ToolTip(DrumTransposeOutputHelp);
        if (!comboOpen)
            return;

        string? currentCategory = null;
        foreach (var option in MidiForgeMapOptionCatalog.DrumNoteOptions)
        {
            if (!string.Equals(currentCategory, option.Category, StringComparison.Ordinal))
            {
                currentCategory = option.Category;
                DrawComboSectionLabel(currentCategory);
            }

            var selected = option.NoteNumber == selectedNote;
            if (ImGui.Selectable($"{option.Label}##transposeOutputNote{preset}_{entry.InputNote}_{option.NoteNumber}", selected))
            {
                entry.OutputNote = option.NoteNumber;
                SaveMaps(maps);
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private string GetInstrumentAliasBuffer(MidiForgeInstrumentMapSettings entry)
    {
        if (!instrumentAliasBuffers.TryGetValue(entry.InstrumentId, out var buffer))
        {
            buffer = string.Join(", ", entry.TrackNameAliases);
            instrumentAliasBuffers[entry.InstrumentId] = buffer;
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

    private static MidiForgeDrumTransposePresetSettings GetOrCreatePresetSettings(
        MidiForgeMapSettings maps,
        MidiForgeDrumTransposePreset preset)
    {
        var presetSettings = maps.DrumTransposePresets.FirstOrDefault(item => item.Preset == preset);
        if (presetSettings is not null)
            return presetSettings;

        var defaults = MidiForgeMapDefaults.CreateDefaultSettings();
        presetSettings = CloneTransposePreset(defaults.DrumTransposePresets.First(item => item.Preset == preset));
        maps.DrumTransposePresets.Add(presetSettings);
        MidiForgeMapDefaults.Normalize(maps);
        return maps.DrumTransposePresets.First(item => item.Preset == preset);
    }

    private static MidiForgeDrumTransposePresetSettings CloneTransposePreset(
        MidiForgeDrumTransposePresetSettings source)
        => new()
        {
            Preset = source.Preset,
            Entries = (source.Entries ?? new List<MidiForgeDrumTransposeMapEntry>())
                .Select(entry => new MidiForgeDrumTransposeMapEntry
                {
                    Category = entry.Category,
                    DrumkitInstrument = entry.DrumkitInstrument,
                    InputNote = entry.InputNote,
                    OutputNote = entry.OutputNote,
                })
                .ToList(),
        };

    private void SaveMaps(MidiForgeMapSettings maps)
    {
        MidiForgeMapDefaults.Normalize(maps);
        Context.Plugin.IpcProvider.SyncAllSettings();
    }

    private void ClearBuffers()
    {
        instrumentAliasBuffers.Clear();
    }

    private static List<string> ParseTextList(string value)
        => (value ?? string.Empty)
            .Split([',', ';', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(part => part, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string FormatDrumNote(int noteNumber)
        => $"{Math.Clamp(noteNumber, 0, 127)} - {MidiForgeMapDefaults.GetDrumkitInstrumentName(noteNumber)}";
}
