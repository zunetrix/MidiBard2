using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.List;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public sealed class InterfaceSettingsWidget : Widget
{
    public override string Title => "Interface";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Desktop;

    public InterfaceSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;

        // ── Main window info ──────────────────────────────────────────────────

        if (ImGui.Checkbox(Language.setting_label_show_now_playing_info, ref cfg.showNowPlayingInfo))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_label_show_now_playing_info);

        if (ImGui.Checkbox(Language.setting_label_hide_player_information_from_ui, ref cfg.hidePlayerInformationFromUi))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_label_hide_player_information_from_ui);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Show / hide elements ──────────────────────────────────────────────

        ImGui.Text(Language.setting_label_show_hide_in_main_window);
        ImGui.Spacing();

        if (ImGui.Checkbox("Track Selection", ref cfg.ShowTrackSelection))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.setting_label_auto_align_loaded_midi, ref cfg.UiShowAutoAlignMidi))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.setting_label_auto_adapt_notes, ref cfg.UiShowAdaptNotesOOR))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.setting_label_tone_mode, ref cfg.UiShowGuitarToneMode))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.setting_label_set_play_speed, ref cfg.UiShowPlaySpeed))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.setting_label_global_transpose, ref cfg.UiShowTransposeGlobal))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox("Show Ads Links", ref cfg.UiShowAdsLinks))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox("Ensemble Panel", ref cfg.UiShowEnsemblePanel))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox("Ensemble Start mode", ref cfg.PlayButtonShowEnsembleStart))
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Row counts ────────────────────────────────────────────────────────

        ImGui.Text("Playlist visible rows");
        ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 4f);
        if (ImGuiUtil.InputIntWithReset("##sw2PlaylistMaxRows", ref cfg.PlaylistMaxVisibleRows, 1, () => 15))
            cfg.PlaylistMaxVisibleRows = Math.Clamp(cfg.PlaylistMaxVisibleRows, 1, 20);
        ImGuiUtil.ToolTip("Number of songs visible in the main window playlist\nRight-click to reset (default: 15)");

        ImGui.Text("Track selection visible rows");
        ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 4f);
        if (ImGuiUtil.InputIntWithReset("##sw2TrackMaxRows", ref cfg.TrackSelectionMaxVisibleRows, 1, () => 8))
            cfg.TrackSelectionMaxVisibleRows = Math.Clamp(cfg.TrackSelectionMaxVisibleRows, 1, 20);
        ImGuiUtil.ToolTip("Number of tracks visible in the track selection panel\nRight-click to reset (default: 8)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Pinned import folders ─────────────────────────────────────────────

        DrawPinnedImportFolders();
    }

    private void DrawPinnedImportFolders()
    {
        if (!ImGui.CollapsingHeader(Language.favorite_import_folders, ImGuiTreeNodeFlags.NoAutoOpenOnLog)) return;

        ImGui.Indent();
        ImGui.Spacing();
        ImGui.Spacing();

        if (ImGui.Button(Language.add_folder))
            AddPinnedFolderDialog();
        ImGuiUtil.HelpMarker("Add favorite folders to be displayed in the import folders and files dialog (Drag to reorder)");

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var cfg = Context.Plugin.Config;

        if (ImGui.BeginTable("##SW2PinnedFoldersTable", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("#",       ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Folder",  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed);

            for (int i = 0; i < cfg.PinnedImportFolders.Count; i++)
            {
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"{i + 1:00}");

                ImGui.TableNextColumn();
                ImGui.Selectable($"{cfg.PinnedImportFolders[i]}");

                if (ImGui.BeginDragDropSource())
                {
                    unsafe
                    {
                        ImGui.SetDragDropPayload("DND_PINNED_IMPORT_FOLDERS", new System.ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                        ImGui.Button($"({i + 1}) {cfg.PinnedImportFolders[i]}");
                    }
                    ImGui.EndDragDropSource();
                }

                using (ImRaii.PushColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget))
                {
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("DND_PINNED_IMPORT_FOLDERS");
                        bool isDropping;
                        unsafe { isDropping = !payload.IsNull; }

                        if (isDropping && payload.IsDelivery())
                        {
                            int originalIndex;
                            unsafe { originalIndex = *(int*)payload.Data; }
                            int offset = i - originalIndex;
                            if (offset != 0 && originalIndex + offset >= 0)
                            {
                                cfg.PinnedImportFolders.MoveItemToIndex(originalIndex, originalIndex + offset);
                                Context.Plugin.IpcProvider.SyncAllSettings();
                                Context.Plugin.Ui.FileDialogService.OverwriteCustomPinnedFolders(cfg.PinnedImportFolders);
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                }

                ImGui.TableNextColumn();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, $"##SW2OpenPinned_{i}", "Open"))
                    WindowsApi.OpenFolder(cfg.PinnedImportFolders[i]);

                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##SW2RemovePinned_{i}", Language.ConfirmInstructionTooltip))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        cfg.PinnedImportFolders.SafeRemoveAt(i);
                        Context.Plugin.Ui.FileDialogService.OverwriteCustomPinnedFolders(cfg.PinnedImportFolders);
                    }
                }

                ImGui.PopID();
            }
            ImGui.EndTable();
            ImGui.Unindent();
        }
    }

    private void AddPinnedFolderDialog()
    {
        var cfg = Context.Plugin.Config;
        Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select pinned folder",
            (result, filePath) =>
            {
                if (!result) return;
                cfg.PinnedImportFolders.Add(filePath);
                Context.Plugin.IpcProvider.SyncAllSettings();
                Context.Plugin.Ui.FileDialogService.OverwriteCustomPinnedFolders(cfg.PinnedImportFolders);
            },
            cfg.lastOpenedFolderPath);
    }
}
