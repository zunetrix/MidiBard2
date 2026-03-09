using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public class SongsWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<Song> _songs = new();

    // Search
    private readonly List<int> _searchIndexes = new();
    public HashSet<int> _selectedSongIds = new();
    private bool _isGlobalSongsCheckboxChecked = false;
    private string _search = string.Empty;

    // Add selected songs to playlist
    private readonly List<Playlist.Playlist> _playlistTargets = new();
    private int _selectedPlaylistTargetIndex = 0;
    private bool _isLoadingPlaylistTargets = false;
    private bool _closeAddToPlaylistPopup = false;

    private bool _isLoading;

    // Import progress
    private readonly SongImportHelper _importHelper;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    // Per-column filters
    private string _filterName = string.Empty;
    private string _filterArtist = string.Empty;
    private string _filterYear = string.Empty;
    private string _filterFilePath = string.Empty;
    private string _filterComments = string.Empty;
    private string _filterTags = string.Empty;

    // Sort state
    private SongSortColumn? _sortCol = null;
    private bool _sortAsc = true;

    public SongsWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SongsTitle} Collection###SongsWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);
        _importHelper.OnImportCompleted = OnImportCompleted;
        _importHelper.OnSyncCompleted = OnSyncCompleted;
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private async Task OnImportCompleted()
    {
        await LoadSongsAsync();
    }

    private void OnSyncCompleted()
    {
        _ = LoadSongsAsync();
    }

    public override void PreDraw()
    {
        var WindowSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(350, 300),
            // MaximumSize = ImGuiHelpers.ScaledVector2(350, float.MaxValue)
        };

        SizeConstraints = WindowSizeConstraints;

        base.PreDraw();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadSongsAsync();
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    private void ResetState()
    {
        _songs.Clear();
        _searchIndexes.Clear();
        _search = string.Empty;
    }

    public async Task LoadSongsAsync()
    {
        _isLoading = true;
        try
        {
            _songs = await ServiceContainer.SongRepository.GetAllSongsAsync();
            Search();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool MatchesFilters(Song song)
    {
        if (!string.IsNullOrWhiteSpace(_search) &&
            !(song.Name?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !(song.Artist?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !(song.FilePath?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterName) &&
            !(song.Name?.Contains(_filterName, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterArtist) &&
            !(song.Artist?.Contains(_filterArtist, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterYear))
        {
            var yearStr = song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "";
            if (!yearStr.Contains(_filterYear, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(_filterFilePath) &&
            !(song.FilePath?.Contains(_filterFilePath, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterComments) &&
            !(song.Comments?.Contains(_filterComments, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        if (!string.IsNullOrWhiteSpace(_filterTags) &&
            !song.Tags.Any(t => t.Name?.Contains(_filterTags, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return true;
    }

    private void Search()
    {
        _searchIndexes.Clear();
        _searchIndexes.AddRange(
            _songs
                .Select((song, index) => new { song, index })
                .Where(x => MatchesFilters(x.song))
                .Select(x => x.index)
        );
    }

    private void ApplySortSongs()
    {
        if (_sortCol == null)
        {
            Search();
            return;
        }

        IOrderedEnumerable<Song> sorted = _sortCol.Value switch
        {
            SongSortColumn.Name => _sortAsc ? _songs.OrderBy(s => s.Name) : _songs.OrderByDescending(s => s.Name),
            SongSortColumn.Artist => _sortAsc ? _songs.OrderBy(s => s.Artist) : _songs.OrderByDescending(s => s.Artist),
            SongSortColumn.Year => _sortAsc ? _songs.OrderBy(s => s.ReleaseYear) : _songs.OrderByDescending(s => s.ReleaseYear),
            SongSortColumn.Duration => _sortAsc ? _songs.OrderBy(s => s.Duration) : _songs.OrderByDescending(s => s.Duration),
            SongSortColumn.PlayCount => _sortAsc ? _songs.OrderBy(s => s.PlayCount) : _songs.OrderByDescending(s => s.PlayCount),
            SongSortColumn.LastPlayed => _sortAsc ? _songs.OrderBy(s => s.LastPlayedAt) : _songs.OrderByDescending(s => s.LastPlayedAt),
            SongSortColumn.Rating => _sortAsc ? _songs.OrderBy(s => s.Rating) : _songs.OrderByDescending(s => s.Rating),
            SongSortColumn.FileModified => _sortAsc ? _songs.OrderBy(s => s.FileLastModifiedAt) : _songs.OrderByDescending(s => s.FileLastModifiedAt),
            SongSortColumn.IsValid => _sortAsc ? _songs.OrderBy(s => s.IsValid) : _songs.OrderByDescending(s => s.IsValid),
            _ => _songs.OrderBy(s => s.Id)
        };

        _songs = sorted.ToList();
        Search();
    }

    public override void Draw()
    {
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

    private void DrawImportProgress()
    {
        ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());

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

        // Fixed search input at top
        if (ImGui.InputTextWithHint("##SongsSearchInput", Language.SearchInputLabel, ref _search, 200))
        {
            Search();
        }

        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawMenuButtons()
    {
        ImGui.BeginGroup();
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
                ImGui.OpenPopup("AddSelectedSongsToPlaylistPopup");
            }
        }
        ImGuiUtil.ToolTip("Select songs with checkboxes, then add them to a playlist.");
        DrawAddSelectedSongsToPlaylistPopup();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FileImport, "##SongsImportSettingsBtn", "Import Rules\nDefine rules to extract info from file name", size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Ui.ExtractionRulesWindow.Toggle();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##SongsDeleteAllBtn", "Delete all Songs", size: Style.Dimensions.ButtonLarge))
        {
            ImGui.OpenPopup("DeleteAllSongsPopup");
        }
        DrawDeleteAllSongsPopup();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Sync, "##SongsSyncFileDataBtn", "Sync MIDI Files: Checks all file paths and recalculates song durations and last modified dates (invalid songs are highlighted).", size: Style.Dimensions.ButtonLarge))
        {
            SyncSongsFileData();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "##SongsBulkReplacePathBtn", "Bulk Replace File Path Prefix\nUse this option if you move the songs folder", size: Style.Dimensions.ButtonLarge))
            Plugin.Ui.BulkReplaceWindow.Open(_songs);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##SongsExportBtn", "Export", size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Ui.ExportWindow.OpenForSongs(_songs);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Database, "##SongsBackupBtn", "Backup", size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Ui.BackupWindow.Toggle();
        }


        ImGui.SameLine();
        DrawViewColumnsButton();

        ImGui.EndGroup();
    }

    private void DrawDeleteAllSongsPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("DeleteAllSongsPopup");
        if (!popUp) return;

        ImGui.Text("Delete all songs?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This action is irreversible.");
        ImGui.Text("All song metadata will be permanently lost.");
        ImGui.Text("Songs will also be removed from all playlists.");
        ImGui.Spacing();
        if (ImGui.Button("Delete All##DeleteAllSongsConfirmBtn"))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                _ = DeleteAllSongsAsync();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

    }

    private void DrawAddSelectedSongsToPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("AddSelectedSongsToPlaylistPopup");
        if (!popup) return;

        if (_closeAddToPlaylistPopup)
        {
            _closeAddToPlaylistPopup = false;
            ImGui.CloseCurrentPopup();
            return;
        }

        ImGui.Text("Add Selected Songs To Playlist");
        ImGui.Separator();
        ImGui.Text($"Selected songs: {_selectedSongIds.Count}");

        if (_isLoadingPlaylistTargets)
        {
            ImGui.TextDisabled("Loading playlists...");
            return;
        }

        if (_playlistTargets.Count == 0)
        {
            ImGui.TextDisabled("No playlists available.");
            if (ImGui.Button("Reload Playlists"))
                _ = LoadPlaylistTargetsAsync();

            ImGui.SameLine();
            if (ImGui.Button("Cancel##AddToPlaylistCancelEmpty"))
                ImGui.CloseCurrentPopup();
            return;
        }

        var labels = _playlistTargets
            .Select(p => string.IsNullOrWhiteSpace(p.Name) ? $"Playlist #{p.Id}" : p.Name)
            .ToArray();

        if (_selectedPlaylistTargetIndex >= labels.Length)
            _selectedPlaylistTargetIndex = 0;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##AddToPlaylistTargetCombo", ref _selectedPlaylistTargetIndex, labels, labels.Length);

        if (ImGui.Button("Add Selected Songs##AddSelectedSongsToPlaylistConfirm"))
            _ = AddSelectedSongsToPlaylistAsync();

        ImGui.SameLine();
        if (ImGui.Button("Cancel##AddToPlaylistCancel"))
            ImGui.CloseCurrentPopup();
    }

    private async Task LoadPlaylistTargetsAsync()
    {
        _isLoadingPlaylistTargets = true;
        try
        {
            var playlists = await Plugin.PlaylistManager.GetAllPlaylistsAsync();
            _playlistTargets.Clear();
            _playlistTargets.AddRange(playlists.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase));

            if (_selectedPlaylistTargetIndex >= _playlistTargets.Count)
                _selectedPlaylistTargetIndex = 0;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongsWindow] Failed to load playlist targets");
            _messageDisplay.ShowError("Failed to load playlists.");
        }
        finally
        {
            _isLoadingPlaylistTargets = false;
        }
    }

    private async Task AddSelectedSongsToPlaylistAsync()
    {
        if (_selectedSongIds.Count == 0)
        {
            _messageDisplay.ShowError("No songs selected.");
            return;
        }

        if (_playlistTargets.Count == 0 || _selectedPlaylistTargetIndex < 0 || _selectedPlaylistTargetIndex >= _playlistTargets.Count)
        {
            _messageDisplay.ShowError("No target playlist selected.");
            return;
        }

        var target = _playlistTargets[_selectedPlaylistTargetIndex];
        var playlist = await ServiceContainer.PlaylistRepository.GetByIdAsync(target.Id);
        if (playlist == null)
        {
            _messageDisplay.ShowError("Target playlist was not found.");
            return;
        }

        var existingSongIds = playlist.Songs
            .Where(ps => ps.Song?.Id > 0)
            .Select(ps => ps.Song!.Id)
            .ToHashSet();

        var selectedIds = _selectedSongIds.ToList();
        var idsToAdd = selectedIds.Where(id => !existingSongIds.Contains(id)).ToList();

        if (idsToAdd.Count == 0)
        {
            _messageDisplay.ShowError("All selected songs are already in the target playlist.");
            return;
        }

        var ok = await ServiceContainer.PlaylistSongService.BulkAddSongsAsync(target.Id, idsToAdd);
        if (!ok)
        {
            _messageDisplay.ShowError("Failed to add selected songs to playlist.");
            return;
        }

        var skipped = selectedIds.Count - idsToAdd.Count;
        _messageDisplay.ShowSuccess($"Added {idsToAdd.Count} song(s) to '{target.Name}'.{(skipped > 0 ? $" Skipped {skipped} duplicate(s)." : string.Empty)}");

        // Keep other windows/clients in sync.
        Plugin.IpcProvider.LoadPlaylist(target.Id);
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();

        _closeAddToPlaylistPopup = true;
    }

    private void DrawViewColumnsButton()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Columns, "##SongsViewColumnsBtn", "Show/Hide Columns", size: Style.Dimensions.ButtonLarge))
        {
            ImGui.OpenPopup("SongsColumnsPopup");
        }

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

    private void DrawSongTable()
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();

        // Compute dynamic column count: # and Actions are always visible
        var tableColumnCount = 3;
        if (Plugin.Config.SongsWindowColumns.Name) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Artist) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Year) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Duration) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.PlayCount) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.LastPlayed) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Rating) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.FilePath) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Comments) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Tags) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.FileModified) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.IsValid) tableColumnCount++;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX;

        if (ImGui.BeginTable("##SongsTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("##ColCheckbox", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("##ColNumber", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.Name) ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.SongsWindowColumns.Artist) ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.SongsWindowColumns.Year) ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.Duration) ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.PlayCount) ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.LastPlayed) ImGui.TableSetupColumn("Last Played", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.Rating) ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.FilePath) ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.SongsWindowColumns.Comments) ImGui.TableSetupColumn("Comments", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.SongsWindowColumns.Tags) ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.SongsWindowColumns.FileModified) ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.SongsWindowColumns.IsValid) ImGui.TableSetupColumn("Valid", ImGuiTableColumnFlags.WidthFixed);

            // Freeze 1 header row so it stays visible while scrolling
            ImGui.TableSetupScrollFreeze(0, 1);

            // Combined label + filter row
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

            ImGui.TableNextColumn();
            if (ImGui.Checkbox($"##GlobalMacroCheckbox", ref _isGlobalSongsCheckboxChecked))
            {
                if (_isGlobalSongsCheckboxChecked)
                    SelectAllSongs();
                else
                    ClearSongsSelection();
            }
            ImGuiUtil.ToolTip("Select / Unselect All");

            ImGui.TableNextColumn();
            ImGui.Text("#");

            ImGui.TableNextColumn();
            ImGui.Text("Actions");

            if (Plugin.Config.SongsWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Name", SongSortColumn.Name);
                ImGui.SameLine();
                ImGui.Text("Name");
                if (ImGui.InputTextWithHint("##filterName", "Filter...", ref _filterName, 100))
                    Search();
            }
            if (Plugin.Config.SongsWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Artist", SongSortColumn.Artist);
                ImGui.SameLine();
                ImGui.Text("Artist");
                if (ImGui.InputTextWithHint("##filterArtist", "Filter...", ref _filterArtist, 100))
                    Search();
            }
            if (Plugin.Config.SongsWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Year", SongSortColumn.Year);
                ImGui.SameLine();
                ImGui.Text("Year");
                if (ImGui.InputTextWithHint("##filterYear", "Filter...", ref _filterYear, 10))
                    Search();
            }
            if (Plugin.Config.SongsWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Duration", SongSortColumn.Duration);
                ImGui.SameLine();
                ImGui.Text("Duration");
            }
            if (Plugin.Config.SongsWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("PlayCount", SongSortColumn.PlayCount);
                ImGui.SameLine();
                ImGui.Text("Play Count");
            }
            if (Plugin.Config.SongsWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("LastPlayed", SongSortColumn.LastPlayed);
                ImGui.SameLine();
                ImGui.Text("Last Played");
            }
            if (Plugin.Config.SongsWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Rating", SongSortColumn.Rating);
                ImGui.SameLine();
                ImGui.Text("Rating");
            }
            if (Plugin.Config.SongsWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text("File Path");
                if (ImGui.InputTextWithHint("##filterFilePath", "Filter...", ref _filterFilePath, 200))
                    Search();
            }
            if (Plugin.Config.SongsWindowColumns.Comments)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Comments");
                if (ImGui.InputTextWithHint("##filterComments", "Filter...", ref _filterComments, 200))
                    Search();
            }
            if (Plugin.Config.SongsWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Tags");
                if (ImGui.InputTextWithHint("##filterTags", "Filter...", ref _filterTags, 100))
                    Search();
            }
            if (Plugin.Config.SongsWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("FileModified", SongSortColumn.FileModified);
                ImGui.SameLine();
                ImGui.Text("File Modified");
            }
            if (Plugin.Config.SongsWindowColumns.IsValid)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Valid", SongSortColumn.IsValid);
                ImGui.SameLine();
                ImGui.Text("Valid");
            }

            // Use clipper for performance with large lists
            var clipper = new ImGuiListClipper();
            clipper.Begin(_searchIndexes.Count, lineHeight);

            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    if (i >= _searchIndexes.Count) break;

                    var songIndex = _searchIndexes[i];
                    if (songIndex >= _songs.Count) continue;

                    var song = _songs[songIndex];
                    DrawSongRow(i, song, songIndex);
                }
            }

            clipper.End();
            ImGui.EndTable();
        }
    }

    private void DrawSongRow(int displayIndex, Song song, int songIndex)
    {
        ImGui.PushID($"##SongEntry_{song.Id}");

        var textColor = song.IsValid ? Vector4.One : Style.Colors.Red;
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            bool isChecked = _selectedSongIds.Contains(song.Id);
            if (ImGui.Checkbox($"##{song.Id}", ref isChecked))
            {
                if (isChecked)
                    _selectedSongIds.Add(song.Id);
                else
                    _selectedSongIds.Remove(song.Id);
            }

            // # column - always visible
            ImGui.TableNextColumn();
            ImGui.Text($"{displayIndex + 1:0000}");

            // Actions column - always visible
            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteSongBtn_{songIndex}", Language.ConfirmInstructionTooltip))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = DeleteSongAsync(song.Id);
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{songIndex}", "Edit"))
            {
                Plugin.Ui.SongEditWindow.EditSong(song.Id);
            }

            if (Plugin.Config.SongsWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                // ImGui.Text($"({song.Id}) ");
                // ImGui.SameLine();
                if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", false)) { }
                ImGuiUtil.ToolTip(song.FilePath);
            }

            if (Plugin.Config.SongsWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Artist ?? "-");
            }

            if (Plugin.Config.SongsWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
            }

            if (Plugin.Config.SongsWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Duration.ToString(@"mm\:ss"));
            }

            if (Plugin.Config.SongsWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.PlayCount.ToString());
            }

            if (Plugin.Config.SongsWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.LastPlayedAt.HasValue ? song.LastPlayedAt.Value.ToString("g") : "-");
            }

            if (Plugin.Config.SongsWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");
            }

            if (Plugin.Config.SongsWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FilePath);
            }

            if (Plugin.Config.SongsWindowColumns.Comments)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Comments ?? "-");
            }

            if (Plugin.Config.SongsWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                var tagsText = song.Tags.Count > 0 ? string.Join(", ", song.Tags.Select(t => t.Name)) : "-";
                ImGui.Text(tagsText);
            }

            if (Plugin.Config.SongsWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FileLastModifiedAt.ToString("g"));
            }

            if (Plugin.Config.SongsWindowColumns.IsValid)
            {
                ImGui.TableNextColumn();
                var (icon, color) = song.IsValid
                    ? (FontAwesomeIcon.Check, Plugin.Config.playedSongColor)
                    : (FontAwesomeIcon.Times, Style.Colors.Red);
                using (ImRaii.PushColor(ImGuiCol.Text, color))
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    ImGui.Text(icon.ToIconString());
            }
        }
        ImGui.PopID();
    }

    private void SyncSongsFileData()
    {
        if (_songs.Count == 0) return;

        var modified = new List<Song>();

        _importHelper.OnSyncCompleted = () =>
        {
            _importHelper.OnSyncCompleted = OnSyncCompleted; // restore default handler
            _ = FinalizeSyncAsync(modified);
        };

        _importHelper.StartSync(_songs, async song =>
        {
            if (await Plugin.PlaylistManager.ComputeSyncSongFileDataAsync(song))
                modified.Add(song);
        });
    }

    private async Task FinalizeSyncAsync(List<Song> modified)
    {
        if (modified.Count > 0)
            await ServiceContainer.SongService.BulkUpdateAsync(modified);
        _ = LoadSongsAsync();
    }

    private async Task DeleteSongAsync(int songId)
    {
        await ServiceContainer.SongService.DeleteAsync(songId);

        await LoadSongsAsync();
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();
    }

    private async Task DeleteAllSongsAsync()
    {
        var songRepo = ServiceContainer.SongRepository;
        var playlistRepo = ServiceContainer.PlaylistRepository;

        {
            // Clear all songs from all playlists first
            await playlistRepo.ClearAllPlaylistsAsync();

            // Then delete all songs from database
            await songRepo.DeleteAllAsync();
        }

        await LoadSongsAsync();
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();
    }

    public void SelectAllSongs()
    {
        _selectedSongIds.Clear();

        foreach (var song in _songs)
        {
            _selectedSongIds.Add(song.Id);
        }
    }

    public void ClearSongsSelection()
    {
        _selectedSongIds.Clear();
    }
}
