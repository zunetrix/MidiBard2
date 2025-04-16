using System;
using System.IO;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Util;

using static Dalamud.api;
using static MidiBard2.Resources.Language;

namespace MidiBard;

public partial class PluginUI
{
    private void DrawGeneralSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_general_settings);
        {
            if (ImGui.Checkbox(setting_label_auto_open_on_startup, ref MidiBard.config.AutoOpenOnStartup))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_auto_open_on_startup);

            //-------------------

            if (ImGui.Checkbox(setting_label_auto_open_when_performing, ref MidiBard.config.AutoOpenPlayerWhenPerforming))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_auto_open_when_performing);

            if (ImGui.Checkbox(setting_label_auto_close_when_performing, ref MidiBard.config.AutoClosePlayerWhenPerforming))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_auto_close_when_performing);

            //-------------------

            if (ImGui.Checkbox(setting_label_show_now_playing_info, ref MidiBard.config.showNowPlayingInfo))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_show_now_playing_info);

            //-------------------

            if (ImGui.Checkbox(setting_label_hide_player_information_from_ui, ref MidiBard.config.hidePlayerInformationFromUi))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_hide_player_information_from_ui);

            //-------------------

            //Checkbox(Low_latency_mode, ref MidiBard.config.LowLatencyMode);
            //ImGuiUtil.ToolTip(low_latency_mode_tooltip);

            //ImGui.Checkbox(checkbox_auto_restart_listening, ref MidiBard.config.autoRestoreListening);
            //ImGuiUtil.ToolTip(checkbox_auto_restart_listening_tooltip);

            //ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2);
            //ImGui.Checkbox("Auto listening new device".Localize(), ref MidiBard.config.autoStartNewListening);
            //ImGuiUtil.ToolTip("Auto start listening new midi input device when idle.".Localize());
            //ImGuiUtil.ColorPickerButton(1000, label_theme_color, ref MidiBard.config.themeColor,
            //    ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
            //if (ImGui.ColorEdit4("Theme color".Localize(), ref MidiBard.config.themeColor,
            //    ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs))
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.TextUnformatted(setting_label_theme_color);
            ImGui.Spacing();
            ImGui.ColorEdit4("##{setting_label_theme_color}", ref MidiBard.config.themeColor,
                ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##btnResetUIColor", "Reset"))
            {
                MidiBard.config.themeColor = Theme.Colors.Lavender;
                IPCHandles.SyncAllSettings();
            }
            //-------------------

            ImGui.Spacing();
            ImGui.TextUnformatted(setting_label_played_song_highlight_color);
            ImGui.Spacing();
            ImGui.ColorEdit4(setting_label_played_song_highlight_color, ref MidiBard.config.playedSongColor, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel);
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##btnResetSongHighlightColor", "Reset"))
            {
                MidiBard.config.playedSongColor = Theme.Colors.Cyan;
                IPCHandles.SyncAllSettings();
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Combo(setting_label_select_ui_language, ref MidiBard.config.uiLang, uilangStrings,
                    uilangStrings.Length))
            {
                MidiBard.ConfigureLanguage(MidiBard.GetCultureCodeString((MidiBard.CultureCode)MidiBard.config.uiLang));
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text($"Default Performer Folder:");
            ImGui.TextUnformatted(Path.ChangeExtension(MidiBard.config.defaultPerformerFolder, null).EllipsisString(40));

            var btnChangeText = "Change";
            // var btnChangeSize = ImGuiHelpers.GetButtonSize(btnChangeText);
            // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnChangeSize.X);
            ImGui.SameLine();
            if (ImGui.Button(btnChangeText))
            {
                RunSetDefaultPerformerFolderImGui();
            }

            ImGui.Spacing();

            //-------------------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Button("Open Settings Folder"))
            {
                Util.Extensions.OpenFolder(api.PluginInterface.ConfigDirectory.FullName);
                ImGuiUtil.AddNotification(NotificationType.Success, $"Settings exported");
            }

            ImGui.Spacing();
            ImGui.Spacing();
        }

        ImGuiGroupPanel.EndGroupPanel();

        ImGui.Spacing();
        ImGui.Spacing();

        DrawPinnedImportFoldersSettings();
    }

    private void DrawPinnedImportFoldersSettings()
    {
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Current.Header.Normal);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Current.Header.Hovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Current.Header.Active);
        if (ImGui.CollapsingHeader("Favorite Import Folders", ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Indent();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Button("Add folder"))
            {
                AddPinnedFolderImGui();
            }
            ImGuiUtil.HelpMarker("Add favorite folders to be displayed in the import folders and files dialog");

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.BeginTable("##PinnedImportFoldersTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Folder", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed);

                for (int i = 0; i < MidiBard.config.PinnedImportFolders.Count; i++)
                {
                    ImGui.PushID(i);
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted($"{i + 1:000}");

                    ImGui.TableNextColumn();
                    ImGui.Selectable($"{MidiBard.config.PinnedImportFolders[i]}");

                    if (ImGui.BeginDragDropSource())
                    {
                        unsafe
                        {
                            ImGui.SetDragDropPayload("DND_PINNED_IMPORT_FOLDERS", new IntPtr(&i), sizeof(int));
                            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Current.Button.Active);
                            ImGui.Button($"({i + 1}) {MidiBard.config.PinnedImportFolders[i]}");
                            ImGui.PopStyleColor();
                        }

                        // PluginLog.Warning($"Drag start [{i}]: {MidiBard.config.PinnedImportFolders[i]}");
                        ImGui.EndDragDropSource();
                    }

                    ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Theme.Current.Overlay.DragDropTarget);
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_PINNED_IMPORT_FOLDERS");

                        bool isDropping = false;
                        unsafe
                        {
                            isDropping = dragDropPayload.NativePtr != null;
                        }

                        if (isDropping)
                        {
                            unsafe
                            {
                                int originalIndex = *(int*)dragDropPayload.Data;

                                int offset = i - originalIndex;
                                if (offset != 0 && originalIndex + offset >= 0)
                                {
                                    int targetIndex = originalIndex + offset;
                                    // PluginLog.Warning($"Drag end [{i}]: [{originalIndex}, {targetIndex}] {offset}");
                                    MidiBard.config.MovePinnedImportFolderToIndex(originalIndex, targetIndex);
                                    fileDialogService.SetPinnedFolders(MidiBard.config.PinnedImportFolders);
                                }
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.PopStyleColor();

                    ImGui.TableNextColumn();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, $" X ##OpenPinnedFolder_{i}", "Open"))
                    {
                        Util.Extensions.OpenFolder(MidiBard.config.PinnedImportFolders[i]);
                    }

                    ImGui.SameLine();

                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $" X ##RemovePinnedFolder_{i}", "Delete"))
                    {
                        MidiBard.config.RemovePinnedImportFolder(i);
                        fileDialogService.SetPinnedFolders(MidiBard.config.PinnedImportFolders);
                        MidiBard.SaveConfig();
                    }
                    ImGui.PopID();
                }

                ImGui.EndTable();
                ImGui.PopStyleColor(3);
                ImGui.Unindent();
            }
        }
    }

    private void RunSetDefaultPerformerFolderImGui()
    {
        fileDialogManager.OpenFolderDialog("Set Default Performer Folder", (result, filePath) =>
        {
            // PluginLog.Debug($"dialog result: {result}\n{string.Join("\n", filePath)}");
            if (result)
            {
                MidiFileConfigManager.SetDefaultPerformerFolder(filePath);
                MidiBard.SaveConfig();
                IPCHandles.SyncAllSettings();
                IPCHandles.UpdateDefaultPerformer();
            }
        }, MidiBard.config.defaultPerformerFolder);
    }

    private void AddPinnedFolderImGui()
    {
        fileDialogManager.OpenFolderDialog("Select pinned folder", (result, filePath) =>
        {
            if (result)
            {
                MidiBard.config.PinnedImportFolders.Add(filePath);
                MidiBard.SaveConfig();
                fileDialogService.SetPinnedFolders(MidiBard.config.PinnedImportFolders);
            }
        }, MidiBard.config.lastOpenedFolderPath);
    }
}
