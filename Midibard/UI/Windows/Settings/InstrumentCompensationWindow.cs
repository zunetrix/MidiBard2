using System.Collections.Generic;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.Dalamud;
using MidiBard.Extensions.General;
using MidiBard.Managers;
using MidiBard.Util;

namespace MidiBard;

public class InstrumentCompensationWindow : Window
{
    private Plugin Plugin { get; }

    public InstrumentCompensationWindow(Plugin plugin) : base($"{Plugin.Name} Instrument Delay Compensation###InstrumentCompensationWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(400, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var filePath = Plugin.CurrentBardPlayback?.FilePath;
        var hasCurrentSong = !string.IsNullOrEmpty(filePath);
        var isPerSongActive = Plugin.EnsembleManager.PerSongCompensation != null;
        var defaults = EnsembleManager.GetCompensationAver();
        var source = isPerSongActive
            ? Plugin.EnsembleManager.PerSongCompensation!
            : Plugin.Config.InstrumentCompensationOverrides;

        // toolbar
        using (ImRaii.Disabled(!hasCurrentSong))
        {
            if (ImGui.Button("Save to song file"))
                SaveToSongFile(filePath!, source, defaults);
        }

        if (!hasCurrentSong)
            ImGuiUtil.ToolTip("No song loaded");
        else
            ImGuiUtil.ToolTip($"Save to:\n{Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(filePath!).FullName}");

        ImGui.SameLine();

        if (ImGui.Button("Reset to defaults"))
        {
            if (isPerSongActive)
                Plugin.EnsembleManager.ClearPerSongCompensation();
            else
            {
                Plugin.Config.InstrumentCompensationOverrides.Clear();
                Plugin.EnsembleManager.InvalidateCompensationCache();
                Plugin.IpcProvider.SyncAllSettings();
            }
        }

        ImGuiUtil.ToolTip(isPerSongActive
            ? "Clear per-song override and revert to global values"
            : "Reset global values to defaults");

        if (isPerSongActive)
        {
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Components.ButtonBlueNormal))
                ImGui.TextUnformatted("[Per-song override active]");
            ImGuiUtil.ToolTip("Values loaded from this song's JSON file.\nEdits apply to the per-song override.\nUse 'Save to song file' to persist changes.");
        }

        ImGui.Separator();

        if (ImGui.BeginTable("InstrumentCompensation", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Instrument", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Compensation(ms)", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var instrument in InstrumentHelper.Instruments)
            {
                if (instrument.Row.RowId == 0) continue;
                var rowId = (int)instrument.Row.RowId;
                var name = InstrumentHelper.SanitizeName(instrument.FFXIVDisplayName);
                var defaultMs = defaults[rowId];

                ImGui.TableNextColumn();
                DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight()));
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(name);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);

                var compensationMs = source.TryGetValue(name, out var ms) ? ms : defaultMs;

                if (ImGui.InputInt($"##{rowId}", ref compensationMs, 1, 1))
                {
                    compensationMs = compensationMs.Clamp(0, 500);
                    if (compensationMs == defaultMs)
                        source.Remove(name); // back to default - remove the override
                    else
                        source[name] = compensationMs;

                    Plugin.EnsembleManager.InvalidateCompensationCache();
                    if (!isPerSongActive)
                        Plugin.IpcProvider.SyncAllSettings();
                }
            }

            ImGui.EndTable();
        }
    }

    private void SaveToSongFile(string filePath, Dictionary<string, int> source, int[] defaults)
    {
        var config = Plugin.MidiFileConfigManager.GetMidiConfigFromFile(filePath) ?? new MidiFileConfig();
        var dict = new Dictionary<string, int>();

        foreach (var instrument in InstrumentHelper.Instruments)
        {
            if (instrument.Row.RowId == 0) continue;
            var name = InstrumentHelper.SanitizeName(instrument.FFXIVDisplayName);
            var effectiveMs = source.TryGetValue(name, out var ms) ? ms : defaults[(int)instrument.Row.RowId];
            if (effectiveMs != defaults[(int)instrument.Row.RowId])
                dict[name] = effectiveMs; // only persist non-default values
        }

        config.InstrumentCompensation = dict.Count > 0 ? dict : null;
        Plugin.MidiFileConfigManager.Save(config, filePath);
        ImGuiUtil.AddNotification(NotificationType.Success,
            dict.Count > 0 ? $"Compensation saved ({dict.Count} override(s))." : "Compensation cleared from song file.");
    }
}
