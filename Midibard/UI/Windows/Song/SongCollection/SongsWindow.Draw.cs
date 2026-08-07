using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Time;
using MidiBard.Resources;

namespace MidiBard;

public partial class SongsWindow
{
    public override void Draw()
    {
        DrawMenuBar();

        if (_pendingPopup != null) { ImGui.OpenPopup(_pendingPopup); _pendingPopup = null; }

        // Show import progress if importing
        if (_importHelper.IsImporting)
        {
            DrawImportProgress();
        }

        // Display message if there's one
        _messageDisplay.Draw();

        if (_isLoading)
        {
            // ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
            return;
        }

        // Fixed header at top
        using (ImRaii.Group())
        {
            DrawHeader();
        }

        DrawSongTable();
    }

    private void DrawMenuBar()
    {
        using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
            {
                using var menuBar = ImRaii.MenuBar();
                if (!menuBar) return;

                DrawFileMenu();

                DrawBulkOperationsMenu();

                if (ImGui.MenuItem(Language.playlist_menu_tags))
                {
                    Plugin.Ui.TagsWindow.Toggle();
                }

                if (ImGui.MenuItem(Language.playlist_menu_columns))
                {
                    OpenPopup("SongsColumnsPopup");
                }
            }
        }
    }

    private void DrawFileMenu()
    {
        using var menu = ImRaii.Menu(Language.playlist_menu_file);
        if (!menu) return;

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileImport);
        ImGui.SameLine();
        if (ImGui.Selectable(Language.playlist_menu_import_rules))
        {
            Plugin.Ui.ExtractionRulesWindow.Toggle();
        }
        ImGuiUtil.ToolTip(Language.playlist_menu_import_rules_tooltip);

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileExport);
        ImGui.SameLine();
        if (ImGui.Selectable(Language.playlist_menu_export))
        {
            Plugin.Ui.ExportWindow.OpenForSongs(_songs);
        }

        ImGuiUtil.TextIcon(FontAwesomeIcon.Database);
        ImGui.SameLine();
        if (ImGui.Selectable("Backup"))
        {
            Plugin.Ui.BackupWindow.Toggle();
        }
    }

    private void DrawBulkOperationsMenu()
    {
        using var menu = ImRaii.Menu(Language.songs_menu_bulk_operations);
        if (!menu) return;

        // Sync by File ID toggle
        var useSyncById = Plugin.Config.UseSyncByFileId;
        if (ImGui.Checkbox("##UseSyncByFileId", ref useSyncById))
        {
            Plugin.Config.UseSyncByFileId = useSyncById;
            Plugin.Config.Save();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(Language.songs_bulk_sync_by_file_id);
        ImGuiUtil.ToolTip(Language.songs_bulk_sync_by_file_id_tooltip);

        ImGui.Separator();

        // Stamp IDs
        using (ImRaii.Disabled(!Plugin.Config.UseSyncByFileId))
        {
            ImGuiUtil.TextIcon(FontAwesomeIcon.Stamp);
            ImGui.SameLine();
            if (ImGui.Selectable(Language.songs_bulk_stamp_ids))
                OpenPopup("StampIdsPopup");
            ImGuiUtil.ToolTip(Language.songs_bulk_stamp_ids_tooltip);
        }

        ImGui.Separator();

        ImGuiUtil.TextIcon(FontAwesomeIcon.ExchangeAlt);
        ImGui.SameLine();
        if (ImGui.Selectable(Language.songs_bulk_replace_path))
        {
            Plugin.Ui.BulkReplaceWindow.Open(_songs);
        }
        ImGuiUtil.ToolTip(Language.songs_bulk_replace_path_tooltip);

        ImGuiUtil.TextIcon(FontAwesomeIcon.Sync);
        ImGui.SameLine();
        if (ImGui.Selectable(Language.songs_bulk_sync_all))
        {
            SyncSongsFileData();
        }
        ImGuiUtil.ToolTip(Language.songs_bulk_sync_all_tooltip);

        using (ImRaii.Disabled(_selectedSongIds.Count == 0))
        {
            ImGuiUtil.TextIcon(FontAwesomeIcon.Sync);
            ImGui.SameLine();
            if (ImGui.Selectable(Language.songs_bulk_sync_selected))
                SyncSelectedSongsFileData();
            ImGuiUtil.ToolTip(Language.songs_bulk_sync_selected_tooltip);

            ImGuiUtil.TextIcon(FontAwesomeIcon.Trash);
            ImGui.SameLine();
            if (ImGui.Selectable(Language.songs_bulk_delete_selected))
                OpenPopup("DeleteSelectedSongsPopup");
            ImGuiUtil.ToolTip(Language.songs_bulk_delete_selected_tooltip);
        }

        ImGuiUtil.TextIcon(FontAwesomeIcon.Trash);
        ImGui.SameLine();
        if (ImGui.Selectable(Language.songs_bulk_delete_all))
        {
            OpenPopup("DeleteAllSongsPopup");
        }
    }

    private void DrawImportProgress()
    {
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, Style.Colors.GrassGreen))
        {
            ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());
        }

        if (ImGuiUtil.DangerButton(Language.common_action_cancel))
        {
            _importHelper.Cancel();
        }
    }

    private void DrawHeader()
    {
        DrawMenuButtons();

        ImGui.Spacing();
        ImGui.Separator();

        // Fixed search input at top
        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##ReloadSongsBtn", Language.playlist_tooltip_reload_songs))
        {
            _ = LoadSongsAsync();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##SongsSearchInput", Language.common_label_search, ref _search, 200, ImGuiInputTextFlags.AutoSelectAll))
            Search();

        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawSongCounter()
    {
        if (_selectedSongIds.Count > 0)
        {
            ImGui.SameLine();
            var totalDuration = GetSelectedSongsDuration();
            var btnLabel = string.Format(Language.songs_counter_format, _selectedSongIds.Count, _songs.Count, totalDuration.GetDurationString());
            var btnWidth = ImGui.CalcTextSize(btnLabel).X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - btnWidth - 10 * ImGuiHelpers.GlobalScale);
            using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
                .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueNormal)
                .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueNormal))
            {
                ImGui.Button($"{btnLabel}##SelectionInfo");
            }
        }
    }

    private void DrawMenuButtons()
    {
        using (ImRaii.Group())
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##SongsImportFilesBtn", Language.main_btn_import_file, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFileTask();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##SongsImportFolderBtn", Language.main_btn_import_folder, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFolderTask();
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(_selectedSongIds.Count == 0))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.FileCirclePlus, "##SongsAddSelectedToPlaylistBtn", Language.songs_btn_add_to_playlist, size: Style.Dimensions.ButtonLarge))
                {
                    _ = LoadPlaylistTargetsAsync();
                    OpenPopup("AddSelectedSongsToPlaylistPopup");
                }
            }
            ImGuiUtil.ToolTip(Language.songs_btn_add_to_playlist_tooltip);
            DrawAddSelectedSongsToPlaylistPopup();

            ImGui.SameLine();
            using (ImRaii.Disabled(_selectedSongIds.Count == 0))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Tag, "##SongsBulkTagBtn", Language.songs_btn_tag_selected, size: Style.Dimensions.ButtonLarge))
                {
                    _ = LoadTagTargetsAsync();
                    ImGui.OpenPopup("BulkTagPopup");
                }
            }
            ImGuiUtil.ToolTip(Language.songs_btn_tag_selected_tooltip);
            DrawBulkTagPopup();

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.FileImport, "##SongsImportSettingsBtn", "Import Rules\nDefine rules to extract info from file name", size: Style.Dimensions.ButtonLarge))
            // {
            //     Plugin.Ui.ExtractionRulesWindow.Toggle();
            // }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##SongsDeleteAllBtn", "Delete all Songs", size: Style.Dimensions.ButtonLarge))
            // {
            //     ImGui.OpenPopup("DeleteAllSongsPopup");
            // }
            DrawDeleteAllSongsPopup();
            DrawDeleteSelectedSongsPopup();

            ImGui.SameLine();
            using (ImRaii.Disabled(!HasActiveFiltersOrSort))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.FilterCircleXmark, "##SongsClearFiltersBtn", Language.songs_btn_clear_filters, size: Style.Dimensions.ButtonLarge))
                    ClearFiltersAndSort();
            }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.Sync, "##SongsSyncFileDataBtn", "Sync MIDI Files: Checks all file paths and recalculates song durations and last modified dates (invalid songs are highlighted).", size: Style.Dimensions.ButtonLarge))
            // {
            //     SyncSongsFileData();
            // }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "##SongsBulkReplacePathBtn", "Bulk Replace File Path Prefix\nUse this option if you move the songs folder", size: Style.Dimensions.ButtonLarge))
            //     Plugin.Ui.BulkReplaceWindow.Open(_songs);

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##SongsExportBtn", "Export", size: Style.Dimensions.ButtonLarge))
            // {
            //     Plugin.Ui.ExportWindow.OpenForSongs(_songs);
            // }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.Database, "##SongsBackupBtn", "Backup", size: Style.Dimensions.ButtonLarge))
            // {
            //     Plugin.Ui.BackupWindow.Toggle();
            // }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.Tags, "#TagsWindowBtn", "Tags", size: Style.Dimensions.ButtonLarge))
            // {
            //     Plugin.Ui.TagsWindow.Toggle();
            // }
            DrawViewColumnsPopup();
            DrawStampIdsPopup();
            DrawSyncFileDataPopup();

            DrawSongCounter();
        }
    }

    private void DrawViewColumnsPopup()
    {
        // if (ImGuiUtil.IconButton(FontAwesomeIcon.Columns, "##SongsViewColumnsBtn", "Show/Hide Columns", size: Style.Dimensions.ButtonLarge))
        // {
        //     ImGui.OpenPopup("SongsColumnsPopup");
        // }

        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("SongsColumnsPopup");
        if (!popUp) return;

        ImGui.Text(Language.playlist_col_popup_title);
        ImGui.Separator();
        if (ImGui.Checkbox(Language.common_label_name, ref Plugin.Config.SongsWindowColumns.Name)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_artist, ref Plugin.Config.SongsWindowColumns.Artist)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_year, ref Plugin.Config.SongsWindowColumns.Year)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_duration, ref Plugin.Config.SongsWindowColumns.Duration)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_play_count, ref Plugin.Config.SongsWindowColumns.PlayCount)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.playlist_col_last_played, ref Plugin.Config.SongsWindowColumns.LastPlayed)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_rating, ref Plugin.Config.SongsWindowColumns.Rating)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_file_path, ref Plugin.Config.SongsWindowColumns.FilePath)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.playlist_col_file_modified, ref Plugin.Config.SongsWindowColumns.FileModified)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_comments, ref Plugin.Config.SongsWindowColumns.Comments)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_tags, ref Plugin.Config.SongsWindowColumns.Tags)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.songs_col_valid, ref Plugin.Config.SongsWindowColumns.IsValid)) Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawColSortButton(string label, SongSortColumn colId)
    {
        var icon = _sortCol == colId
            ? (_sortAsc ? FontAwesomeIcon.SortAmountUp : FontAwesomeIcon.SortAmountDown)
            : FontAwesomeIcon.Sort;

        if (ImGuiUtil.IconButton(icon, $"##sortCol_{colId}", string.Format(Language.playlist_tooltip_sort_by, label)))
        {
            if (_sortCol == colId)
                _sortAsc = !_sortAsc;
            else
            {
                _sortCol = colId;
                _sortAsc = true;
            }
            ApplySortSongs();
        }
    }

    private async void RunImportFileTask()
    {
        await _importHelper.ShowAndImportFilesAsync(Plugin);
    }

    private async void RunImportFolderTask()
    {
        await _importHelper.ShowAndImportFolderAsync(Plugin);
    }
}
