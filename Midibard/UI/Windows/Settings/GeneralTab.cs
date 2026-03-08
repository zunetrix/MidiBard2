using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Extensions.List;
using MidiBard.Resources;
using MidiBard.Util.ImGuiExt;
using MidiBard.Util;

namespace MidiBard;

public partial class SettingsWindow
{
    static readonly (string Label, string Code)[] UiLanguages = {
        ("English",   "en"),
        ("简体中文",   "zh-Hans"),
        ("繁體中文",   "zh-Hant"),
        ("日本語",     "ja"),
        ("Deutsch",   "de"),
    };
    static readonly string[] UiLangLabels = UiLanguages.Select(l => l.Label).ToArray();

    static int GetLangIndex(string langCode)
    {
        for (int i = 0; i < UiLanguages.Length; i++)
        {
            if (UiLanguages[i].Code == langCode)
                return i;
        }
        return 0;
    }

    // Backing field populated by EnsureSettingsCacheValid() in PerformanceTab.cs.
    private static string[]? s_themeLabels;

    private void DrawGeneralSettings()
    {
        EnsureSettingsCacheValid();
        using (ImGuiGroupPanel.BeginGroupPanel(Language.setting_group_label_general_settings))
        {
            if (ImGui.Checkbox(Language.setting_label_auto_open_on_startup, ref Plugin.Config.OpenOnStartup))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_auto_open_on_startup);

            //-------------------

            if (ImGui.Checkbox(Language.setting_label_auto_open_when_performing, ref Plugin.Config.AutoOpenPlayerWhenPerforming))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_auto_open_when_performing);

            if (ImGui.Checkbox(Language.setting_label_auto_close_when_performing, ref Plugin.Config.AutoClosePlayerWhenPerforming))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_auto_close_when_performing);

            //-------------------

            if (ImGui.Checkbox(Language.setting_label_show_now_playing_info, ref Plugin.Config.showNowPlayingInfo))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_show_now_playing_info);

            //-------------------

            if (ImGui.Checkbox(Language.setting_label_hide_player_information_from_ui, ref Plugin.Config.hidePlayerInformationFromUi))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_label_hide_player_information_from_ui);

            //-------------------

            if (ImGui.Checkbox(Language.w32_file_dialog, ref Plugin.Config.useLegacyFileDialog))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }

            //-------------------

            if (ImGui.Checkbox(Language.setting_label_save_config_after_sync, ref Plugin.Config.SaveConfigAfterSync))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("Enable for accounts with individual config file");

            //-------------------

            //Checkbox(Low_latency_mode, ref MidiBard.Plugin.Config.LowLatencyMode);
            //ImGuiUtil.ToolTip(low_latency_mode_tooltip);

            //ImGui.Checkbox(checkbox_auto_restart_listening, ref MidiBard.Plugin.Config.autoRestoreListening);
            //ImGuiUtil.ToolTip(checkbox_auto_restart_listening_tooltip);

            //ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2);
            //ImGui.Checkbox("Auto listening new device".Localize(), ref MidiBard.Plugin.Config.autoStartNewListening);
            //ImGuiUtil.ToolTip("Auto start listening new midi input device when idle.".Localize());
            //ImGuiUtil.ColorPickerButton(1000, label_theme_color, ref MidiBard.Plugin.Config.themeColor,
            //    ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
            //if (ImGui.ColorEdit4("Theme color".Localize(), ref MidiBard.Plugin.Config.themeColor,
            //    ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs))
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.Text(Language.setting_label_theme_color);
            ImGui.Spacing();
            ImGui.ColorEdit4("##settingLabelThemeColor", ref Plugin.Config.themeColor,
                ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##btnResetUIColor", "Reset"))
            {
                Plugin.Config.themeColor = Style.Colors.Lavender;
                Plugin.IpcProvider.SyncAllSettings();
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Text(Language.setting_label_played_song_highlight_color);
            ImGui.Spacing();
            ImGui.ColorEdit4("##settingLabelPlayedSongHighlightColor", ref Plugin.Config.playedSongColor, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel);
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##btnResetSongHighlightColor", "Reset"))
            {
                Plugin.Config.playedSongColor = Style.Colors.Cyan;
                Plugin.IpcProvider.SyncAllSettings();
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(Language.setting_label_theme);
            if (ImGuiUtil.EnumCombo($"##comboThemeVariantType", ref Plugin.Config.CurrentTheme, labelsOverride: s_themeLabels))
            {
                ThemeManager.SetTheme(Plugin.Config.CurrentTheme);
                Plugin.IpcProvider.SyncAllSettings();
            }

            //-------------------

            ImGui.Spacing();
            int uiLangIndex = GetLangIndex(Plugin.Config.UiLanguage);
            ImGui.Text(Language.setting_label_select_ui_language);
            if (ImGui.Combo($"##settingUiLang", ref uiLangIndex, UiLangLabels, UiLangLabels.Length))
            {
                Plugin.Config.UiLanguage = UiLanguages[uiLangIndex].Code;
                Plugin.OnLanguageChange(Plugin.Config.UiLanguage);
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Button(Language.open_plugin_folder))
            {
                WindowsApi.OpenFolder(DalamudApi.PluginInterface.ConfigDirectory.FullName);
            }

            ImGui.SameLine();
            ImGui.Spacing();

            ImGui.SameLine();
            if (ImGui.Button(Language.open_plugin_config_file))
            {
                WindowsApi.OpenFile(DalamudApi.PluginInterface.ConfigFile.FullName);
            }

            ImGui.Spacing();
            ImGui.Spacing();
        }

        ImGui.Spacing();
        ImGui.Spacing();

        DrawPinnedImportFoldersSettings();
    }

    private void DrawPinnedImportFoldersSettings()
    {
        if (ImGui.CollapsingHeader(Language.favorite_import_folders, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Indent();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Button(Language.add_folder))
            {
                AddCustomPinnedFolderImGui();
            }
            ImGuiUtil.HelpMarker("Add favorite folders to be displayed in the import folders and files dialog (Drag to reorder)");

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.BeginTable("##PinnedImportFoldersTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed);

                for (int i = 0; i < Plugin.Config.PinnedImportFolders.Count; i++)
                {
                    ImGui.PushID(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{i + 1:00}");

                    ImGui.TableNextColumn();
                    ImGui.Selectable($"{Plugin.Config.PinnedImportFolders[i]}");

                    if (ImGui.BeginDragDropSource())
                    {
                        unsafe
                        {
                            ImGui.SetDragDropPayload("DND_PINNED_IMPORT_FOLDERS", new ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                            ImGui.Button($"({i + 1}) {Plugin.Config.PinnedImportFolders[i]}");
                        }

                        // DalamudApi.PluginLog.Warning($"Drag start [{i}]: {MidiBard.Plugin.Config.PinnedImportFolders[i]}");
                        ImGui.EndDragDropSource();
                    }

                    ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget);
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_PINNED_IMPORT_FOLDERS");

                        bool isDropping = false;
                        unsafe
                        {
                            isDropping = !dragDropPayload.IsNull;
                        }

                        if (isDropping && dragDropPayload.IsDelivery())
                        {
                            unsafe
                            {
                                int originalIndex = *(int*)dragDropPayload.Data;

                                int offset = i - originalIndex;
                                if (offset != 0 && originalIndex + offset >= 0)
                                {
                                    int targetIndex = originalIndex + offset;
                                    // DalamudApi.PluginLog.Warning($"Drag end [{i}]: [{originalIndex}, {targetIndex}] {offset}");
                                    Plugin.Config.PinnedImportFolders.MoveItemToIndex(originalIndex, targetIndex);
                                    Plugin.SaveConfig();
                                    Plugin.IpcProvider.SyncAllSettings();
                                    Plugin.Ui.FileDialogService.OverwriteCustomPinnedFolders(Plugin.Config.PinnedImportFolders);
                                }
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.PopStyleColor();

                    ImGui.TableNextColumn();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, $"##OpenPinnedFolder_{i}", "Open"))
                    {
                        WindowsApi.OpenFolder(Plugin.Config.PinnedImportFolders[i]);
                    }

                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemovePinnedFolder_{i}", "Remove"))
                    {
                        Plugin.Config.PinnedImportFolders.SafeRemoveAt(i);
                        Plugin.Ui.FileDialogService.OverwriteCustomPinnedFolders(Plugin.Config.PinnedImportFolders);
                        Plugin.SaveConfig();
                    }
                    ImGui.PopID();
                }
                ImGui.EndTable();
                ImGui.Unindent();
            }
        }
    }

    private void AddCustomPinnedFolderImGui()
    {
        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Select pinned folder", (result, filePath) =>
        {
            if (result)
            {
                Plugin.Config.PinnedImportFolders.Add(filePath);
                Plugin.SaveConfig();
                Plugin.IpcProvider.SyncAllSettings();
                Plugin.Ui.FileDialogService.OverwriteCustomPinnedFolders(Plugin.Config.PinnedImportFolders);
            }
        }, Plugin.Config.lastOpenedFolderPath);
    }
}
