using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

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
            ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
            return;
        }

        // Fixed header at top
        using (ImRaii.Group())
        {
            DrawHeader();
        }

        // Scrollable content area
        using (ImRaii.Child("##SongsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 0), false))
        {
            DrawSongTable();
        }
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

                if (ImGui.MenuItem("Tags"))
                {
                    Plugin.Ui.TagsWindow.Toggle();
                }

                if (ImGui.MenuItem("Columns"))
                {
                    OpenPopup("SongsColumnsPopup");
                }

                // var versionText = $"v{Plugin.Version}";
                // var textSize = ImGui.CalcTextSize(versionText);
                // var padding = ImGui.GetStyle().FramePadding.X + 5;
                // var regionMaxX = ImGui.GetWindowContentRegionMax().X;
                // // align to right
                // ImGui.SameLine(regionMaxX - textSize.X - (padding * 2));
                // ImGui.Text(versionText);
            }
        }
    }

    private void DrawFileMenu()
    {
        using var menu = ImRaii.Menu("File");
        if (!menu) return;

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileImport);
        ImGui.SameLine();
        if (ImGui.Selectable("Import Rules"))
        {
            Plugin.Ui.ExtractionRulesWindow.Toggle();
        }
        ImGuiUtil.ToolTip("Define rules to extract info from file name into song collection");

        ImGuiUtil.TextIcon(FontAwesomeIcon.FileExport);
        ImGui.SameLine();
        if (ImGui.Selectable("Export"))
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
        using var menu = ImRaii.Menu("Bulk Operations");
        if (!menu) return;

        // using (ImRaii.Disabled(_selectedSongIds.Count == 0))
        // {
        //     ImGuiUtil.TextIcon(FontAwesomeIcon.FileCirclePlus);
        //     ImGui.SameLine();
        //     if (ImGui.Selectable("Add selected songs to playlist"))
        //     {
        //         _ = LoadPlaylistTargetsAsync();
        //         OpenPopup("AddSelectedSongsToPlaylistPopup");
        //     }
        //     ImGuiUtil.ToolTip("Select songs with checkboxes, then add them to a playlist.");

        //     // ----------------------

        //     ImGuiUtil.TextIcon(FontAwesomeIcon.Tag);
        //     ImGui.SameLine();
        //     if (ImGui.Selectable("Tag Selected Songs"))
        //     {
        //         _ = LoadTagTargetsAsync();
        //         OpenPopup("BulkTagPopup");
        //     }
        // }

        // ----------------------

        ImGuiUtil.TextIcon(FontAwesomeIcon.ExchangeAlt);
        ImGui.SameLine();
        if (ImGui.Selectable("Bulk Replace File Path Prefix"))
        {
            Plugin.Ui.BulkReplaceWindow.Open(_songs);
        }
        ImGuiUtil.ToolTip("Use this option if you move the songs folder");

        ImGuiUtil.TextIcon(FontAwesomeIcon.Sync);
        ImGui.SameLine();
        if (ImGui.Selectable("Sync MIDI Files"))
        {
            SyncSongsFileData();
        }
        ImGuiUtil.ToolTip("Checks all file paths and recalculates song durations and last modified dates (invalid songs are highlighted)");

        using (ImRaii.Disabled(_selectedSongIds.Count == 0))
        {
            ImGuiUtil.TextIcon(FontAwesomeIcon.Sync);
            ImGui.SameLine();
            if (ImGui.Selectable("Sync Selected Songs"))
                SyncSelectedSongsFileData();
            ImGuiUtil.ToolTip("Sync file data only for the selected songs");

            ImGuiUtil.TextIcon(FontAwesomeIcon.Trash);
            ImGui.SameLine();
            if (ImGui.Selectable("Delete Selected Songs"))
                OpenPopup("DeleteSelectedSongsPopup");
            ImGuiUtil.ToolTip("Permanently delete the selected songs and remove them from all playlists");
        }

        ImGuiUtil.TextIcon(FontAwesomeIcon.Trash);
        ImGui.SameLine();
        if (ImGui.Selectable("Delete All Songs"))
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

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            if (ImGui.Button("Cancel"))
            {
                _importHelper.Cancel();
            }
        }
    }

    private void DrawHeader()
    {
        DrawMenuButtons();

        ImGui.Spacing();
        ImGui.Separator();

        // Fixed search input at top
        ImGui.SetNextItemWidth(400 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##SongsSearchInput", Language.SearchInputLabel, ref _search, 200))
        {
            Search();
        }

        if (_selectedSongIds.Count > 0)
        {
            ImGui.SameLine();
            var totalDuration = GetSelectedSongsDuration();
            var fmt = totalDuration.TotalDays >= 1 ? @"d\d\ h\:mm\:ss"
                    : totalDuration.TotalHours >= 1 ? @"h\:mm\:ss"
                    : @"m\:ss";
            using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
                        .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueNormal)
                        .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueNormal))
            {
                ImGui.Button($"Songs: {_selectedSongIds.Count}/{_songs.Count} - Duration {totalDuration.ToString(fmt)}##SelectionInfo");
            }
        }

        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawMenuButtons()
    {
        using (ImRaii.Group())
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##SongsImportFilesBtn", Language.icon_button_tooltip_import_file, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFileTask();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##SongsImportFolderBtn", Language.icon_button_tooltip_import_folder, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFolderTask();
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(_selectedSongIds.Count == 0))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.FileCirclePlus, "##SongsAddSelectedToPlaylistBtn", "Add selected songs to playlist", size: Style.Dimensions.ButtonLarge))
                {
                    _ = LoadPlaylistTargetsAsync();
                    OpenPopup("AddSelectedSongsToPlaylistPopup");
                }
            }
            ImGuiUtil.ToolTip("Select songs with checkboxes, then add them to a playlist.");
            DrawAddSelectedSongsToPlaylistPopup();

            ImGui.SameLine();
            using (ImRaii.Disabled(_selectedSongIds.Count == 0))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Tag, "##SongsBulkTagBtn", "Tag selected songs", size: Style.Dimensions.ButtonLarge))
                {
                    _ = LoadTagTargetsAsync();
                    ImGui.OpenPopup("BulkTagPopup");
                }
            }
            ImGuiUtil.ToolTip("Select songs with checkboxes, then assign or remove a tag.");
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

        ImGui.Text("Columns");
        ImGui.Separator();
        if (ImGui.Checkbox("Name", ref Plugin.Config.SongsWindowColumns.Name)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Artist", ref Plugin.Config.SongsWindowColumns.Artist)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Year", ref Plugin.Config.SongsWindowColumns.Year)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Duration", ref Plugin.Config.SongsWindowColumns.Duration)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Play Count", ref Plugin.Config.SongsWindowColumns.PlayCount)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Last Played", ref Plugin.Config.SongsWindowColumns.LastPlayed)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Rating", ref Plugin.Config.SongsWindowColumns.Rating)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("File Path", ref Plugin.Config.SongsWindowColumns.FilePath)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("File Modified", ref Plugin.Config.SongsWindowColumns.FileModified)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Comments", ref Plugin.Config.SongsWindowColumns.Comments)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Tags", ref Plugin.Config.SongsWindowColumns.Tags)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Valid", ref Plugin.Config.SongsWindowColumns.IsValid)) Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawColSortButton(string label, SongSortColumn colId)
    {
        var icon = _sortCol == colId
            ? (_sortAsc ? FontAwesomeIcon.SortAmountUp : FontAwesomeIcon.SortAmountDown)
            : FontAwesomeIcon.Sort;

        if (ImGuiUtil.IconButton(icon, $"##sortCol_{colId}", $"Sort by {label}"))
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
