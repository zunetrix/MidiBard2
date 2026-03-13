using System.Collections.Generic;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.Dalamud.Texture;
using MidiBard.Extensions.General;
using MidiBard.Managers;

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

        // toolbar
        using (ImRaii.Disabled(!hasCurrentSong))
        {
            if (ImGui.Button("Save to song file"))
                SaveToSongFile(filePath!);
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
                Plugin.Config.ManualInstrumentCompensation = EnsembleManager.GetCompensationAver();
                Plugin.IpcProvider.SyncAllSettings();
            }
        }

        ImGuiUtil.ToolTip(isPerSongActive ? "Clear per-song override and revert to global values" : "Reset global values to defaults");

        // per-song indicator
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

            foreach (var instrument in Plugin.Instruments)
            {
                if (instrument.Row.RowId == 0) continue;
                var rowId = (int)instrument.Row.RowId;

                ImGui.TableNextColumn();
                DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight()));
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(SanitizeInstrumentName(instrument.FFXIVDisplayName));
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);

                var compensationMs = isPerSongActive
                    ? Plugin.EnsembleManager.PerSongCompensation![rowId]
                    : Plugin.Config.ManualInstrumentCompensation[rowId];

                if (ImGui.InputInt($"##{rowId}", ref compensationMs, 1, 1))
                {
                    compensationMs = compensationMs.Clamp(0, 500);
                    if (isPerSongActive)
                        Plugin.EnsembleManager.PerSongCompensation![rowId] = compensationMs;
                    else
                    {
                        Plugin.Config.ManualInstrumentCompensation[rowId] = compensationMs;
                        Plugin.IpcProvider.SyncAllSettings();
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    private void SaveToSongFile(string filePath)
    {
        var config = Plugin.MidiFileConfigManager.GetMidiConfigFromFile(filePath) ?? new MidiFileConfig();
        var source = Plugin.EnsembleManager.PerSongCompensation ?? Plugin.Config.ManualInstrumentCompensation;
        var dict = new Dictionary<string, int>();
        foreach (var instrument in Plugin.Instruments)
        {
            if (instrument.Row.RowId == 0) continue;
            dict[SanitizeInstrumentName(instrument.FFXIVDisplayName)] = source[(int)instrument.Row.RowId];
        }
        config.InstrumentCompensation = dict;
        Plugin.MidiFileConfigManager.Save(config, filePath);
        ImGuiUtil.AddNotification(NotificationType.Success, "Compensation saved to song file.");
    }

    private static string SanitizeInstrumentName(string input) => Regex.Replace(input, "[^a-zA-Z]", "");
}
