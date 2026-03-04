using System;
using System.Collections.Generic;
using System.IO;
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
    private string _search = string.Empty;

    private bool _isLoading;

    // Import progress
    private readonly SongImportHelper _importHelper;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    // Column visibility
    private bool _showColName = true;
    private bool _showColArtist = true;
    private bool _showColYear = false;
    private bool _showColDuration = true;
    private bool _showColPlayCount = false;
    private bool _showColRating = false;
    private bool _showColFilePath = true;
    private bool _showColFileModified = false;

    // Per-column filters
    private string _filterName = string.Empty;
    private string _filterArtist = string.Empty;
    private string _filterYear = string.Empty;
    private string _filterFilePath = string.Empty;

    // Sort state
    private int _sortCol = -1;
    private bool _sortAsc = true;
    private const int SortByName = 1, SortByArtist = 2, SortByYear = 3, SortByDuration = 4,
        SortByPlayCount = 5, SortByRating = 6, SortByFileModified = 7;

    public SongsWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SongsTitle}###SongsWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);
        _importHelper.OnImportCompleted = OnImportCompleted;
        _importHelper.OnSyncCompleted = OnSyncCompleted;
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private void OnImportCompleted()
    {
        _ = LoadSongsAsync();
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

    private async Task LoadSongsAsync()
    {
        if (Plugin.PlaylistManager == null) return;
        _isLoading = true;
        try
        {
            var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
            if (songRepo != null)
            {
                _songs = await songRepo.GetAllSongsAsync();
                Search();
            }
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
        if (_sortCol < 0)
        {
            Search();
            return;
        }

        IOrderedEnumerable<Song> sorted = _sortCol switch
        {
            SortByName => _sortAsc ? _songs.OrderBy(s => s.Name) : _songs.OrderByDescending(s => s.Name),
            SortByArtist => _sortAsc ? _songs.OrderBy(s => s.Artist) : _songs.OrderByDescending(s => s.Artist),
            SortByYear => _sortAsc ? _songs.OrderBy(s => s.ReleaseYear) : _songs.OrderByDescending(s => s.ReleaseYear),
            SortByDuration => _sortAsc ? _songs.OrderBy(s => s.Duration) : _songs.OrderByDescending(s => s.Duration),
            SortByPlayCount => _sortAsc ? _songs.OrderBy(s => s.PlayCount) : _songs.OrderByDescending(s => s.PlayCount),
            SortByRating => _sortAsc ? _songs.OrderBy(s => s.Rating) : _songs.OrderByDescending(s => s.Rating),
            SortByFileModified => _sortAsc ? _songs.OrderBy(s => s.FileLastModifiedAt) : _songs.OrderByDescending(s => s.FileLastModifiedAt),
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
        var progress = _importHelper.GetProgressValue();

        // Check if this is a sync operation by checking if OnSyncCompleted is set
        var progressText = _importHelper.OnSyncCompleted != null
            ? _importHelper.GetSyncProgressText()
            : _importHelper.GetProgressText();

        ImGui.ProgressBar(progress, ImGuiHelpers.ScaledVector2(-1, 20), progressText);

        if (ImGui.Button("Cancel"))
        {
            _importHelper.Cancel();
        }
    }

    private void DrawHeader()
    {
        // Display message if there's one
        _messageDisplay.Draw();

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
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##SongsImportFilesBtn", Language.icon_button_tooltip_import_file, size: Style.Dimensions.PlayerButton))
        {
            RunImportFileTask();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##SongsImportFolderBtn", Language.icon_button_tooltip_import_folder, size: Style.Dimensions.PlayerButton))
        {
            RunImportFolderTask();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Sync, "##SongsSyncFileDataBtn", "Sync Midi Files", size: Style.Dimensions.PlayerButton))
        {
            SyncSongsFileData();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "##SongsDeleteAllBtn", "Delete all Songs", size: Style.Dimensions.PlayerButton))
        {
            _ = DeleteAllSongsAsync();
        }

        ImGui.SameLine();
        DrawViewColumnsButton();

        ImGui.EndGroup();
    }

    private void DrawViewColumnsButton()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Columns, "##SongsViewColumnsBtn", "Show/Hide Columns", size: Style.Dimensions.PlayerButton))
            ImGui.OpenPopup("SongsColumnsPopup");

        if (ImGui.BeginPopup("SongsColumnsPopup"))
        {
            ImGui.Text("Columns");
            ImGui.Separator();
            ImGui.Checkbox("Name", ref _showColName);
            ImGui.Checkbox("Artist", ref _showColArtist);
            ImGui.Checkbox("Year", ref _showColYear);
            ImGui.Checkbox("Duration", ref _showColDuration);
            ImGui.Checkbox("Play Count", ref _showColPlayCount);
            ImGui.Checkbox("Rating", ref _showColRating);
            ImGui.Checkbox("File Path", ref _showColFilePath);
            ImGui.Checkbox("File Modified", ref _showColFileModified);
            ImGui.EndPopup();
        }
    }

    private void DrawColSortButton(string label, int colId)
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
        var tableColumnCount = 2;
        if (_showColName) tableColumnCount++;
        if (_showColArtist) tableColumnCount++;
        if (_showColYear) tableColumnCount++;
        if (_showColDuration) tableColumnCount++;
        if (_showColPlayCount) tableColumnCount++;
        if (_showColRating) tableColumnCount++;
        if (_showColFilePath) tableColumnCount++;
        if (_showColFileModified) tableColumnCount++;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX;

        if (ImGui.BeginTable("##SongsTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
            if (_showColName) ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (_showColArtist) ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            if (_showColYear) ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            if (_showColDuration) ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            if (_showColPlayCount) ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            if (_showColRating) ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            if (_showColFilePath) ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            if (_showColFileModified) ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

            // Freeze 1 header row so it stays visible while scrolling
            ImGui.TableSetupScrollFreeze(0, 1);

            // --- Combined label + filter row ---
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

            ImGui.TableNextColumn();
            ImGui.Text("#");

            if (_showColName)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Name", SortByName);
                ImGui.SameLine();
                ImGui.Text("Name");
                if (ImGui.InputTextWithHint("##filterName", "Filter...", ref _filterName, 100))
                    Search();
            }
            if (_showColArtist)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Artist", SortByArtist);
                ImGui.SameLine();
                ImGui.Text("Artist");
                if (ImGui.InputTextWithHint("##filterArtist", "Filter...", ref _filterArtist, 100))
                    Search();
            }
            if (_showColYear)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Year", SortByYear);
                ImGui.SameLine();
                ImGui.Text("Year");
                if (ImGui.InputTextWithHint("##filterYear", "Filter...", ref _filterYear, 10))
                    Search();
            }
            if (_showColDuration)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Duration", SortByDuration);
                ImGui.SameLine();
                ImGui.Text("Duration");
            }
            if (_showColPlayCount)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("PlayCount", SortByPlayCount);
                ImGui.SameLine();
                ImGui.Text("Play Count");
            }
            if (_showColRating)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Rating", SortByRating);
                ImGui.SameLine();
                ImGui.Text("Rating");
            }
            if (_showColFilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text("File Path");
                if (ImGui.InputTextWithHint("##filterFilePath", "Filter...", ref _filterFilePath, 200))
                    Search();
            }
            if (_showColFileModified)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("FileModified", SortByFileModified);
                ImGui.SameLine();
                ImGui.Text("File Modified");
            }
            ImGui.TableNextColumn();
            ImGui.Text("Actions");

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

        // Determine text color
        var textColor = song.IsValid ? Vector4.One : Style.Colors.Yellow;
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            // Table row
            ImGui.TableNextRow();

            // # column — always visible
            ImGui.TableNextColumn();
            ImGui.Text($"{displayIndex + 1:0000}");

            if (_showColName)
            {
                ImGui.TableNextColumn();
                ImGui.Text($"({song.Id}) ");
                ImGui.SameLine();
                if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", false)) { }
            }

            if (_showColArtist)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Artist ?? "-");
            }

            if (_showColYear)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
            }

            if (_showColDuration)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Duration.ToString(@"mm\:ss"));
            }

            if (_showColPlayCount)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.PlayCount.ToString());
            }

            if (_showColRating)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");
            }

            if (_showColFilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FilePath);
            }

            if (_showColFileModified)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FileLastModifiedAt.ToString("g"));
            }
        }

        // Actions column — always visible
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteSongBtn_{songIndex}", Language.DeleteInstructionTooltip))
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

        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, $"##ChangeSongFilePathTableBtn_{song.Id}", "Change File Path"))
        {
            _ = ChangeFilePathAsync(song.Id);
        }

        ImGui.PopID();
    }

    private async Task ChangeFilePathAsync(int songId)
    {
        if (Plugin.PlaylistManager == null) return;

        var song = await Plugin.PlaylistManager.GetSongByIdAsync(songId);
        if (song == null) return;

        var originalFilePath = song.FilePath;
        var newFilePath = await _importHelper.GetMidiFilePathAsync(Plugin, Path.GetDirectoryName(originalFilePath));

        if (!string.IsNullOrWhiteSpace(newFilePath) && newFilePath != originalFilePath)
        {
            song.FilePath = newFilePath;
            song.Name = Path.GetFileNameWithoutExtension(newFilePath);

            // Set HasValidFilePath to true since user selected a valid file
            song.IsValid = File.Exists(newFilePath);
            if (song.IsValid)
                song.FileLastModifiedAt = File.GetLastWriteTimeUtc(newFilePath);

            await Plugin.PlaylistManager.UpdateSongAsync(song);

            // Sync file data (recalculate duration)
            await Plugin.PlaylistManager.SyncSongFileDataAsync(song);

            // Reload songs to refresh the UI
            await LoadSongsAsync();
        }
    }

    private void SyncSongsFileData()
    {
        if (Plugin.PlaylistManager == null || _songs.Count == 0) return;

        // Use the import helper for background processing with progress
        _importHelper.StartSync(_songs, async song =>
        {
            await Plugin.PlaylistManager.SyncSongFileDataAsync(song);
        });
    }

    private async Task DeleteSongAsync(int songId)
    {
        if (Plugin.PlaylistManager == null) return;

        var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
        if (songRepo != null)
        {
            await songRepo.DeleteAsync(songId);
        }

        await LoadSongsAsync();
    }

    private async Task DeleteAllSongsAsync()
    {
        if (Plugin.PlaylistManager == null) return;

        var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
        var playlistRepo = ServiceContainer.GetServiceOrNull<IPlaylistRepository>();

        if (songRepo != null && playlistRepo != null)
        {
            // Clear all songs from all playlists first
            await playlistRepo.ClearAllPlaylistsAsync();

            // Then delete all songs from database
            await songRepo.DeleteAllAsync();
        }

        await LoadSongsAsync();
    }
}
