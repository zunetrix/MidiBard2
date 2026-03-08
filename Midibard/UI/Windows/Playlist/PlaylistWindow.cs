using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public class PlaylistWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<Playlist.Playlist> _playlists = new();
    private Playlist.Playlist? _selectedPlaylist;
    private Song? _selectedSong;
    private int _selectedSongIndex = -1;

    private static readonly List<PlaylistSong> _emptyPlaylistSongs = new();
    private List<PlaylistSong> PlaylistSongs => _selectedPlaylist?.Songs ?? _emptyPlaylistSongs;

    // Form state
    private string _newPlaylistName = string.Empty;
    private string _editPlaylistName = string.Empty;

    // Search
    private readonly List<int> _songSearchIndexes = new();
    private readonly List<int> _playlistSearchIndexes = new();
    private string _songSearch = string.Empty;
    private string _playlistSearch = string.Empty;

    // Panel resizing
    private float _leftPanelWidth = 200f;
    private bool _showPlaylistEditorLeftPanel = true;

    private bool _isLoading;

    // Import helper (progress tracking, dialog, cancellation)
    private readonly SongImportHelper _importHelper;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    // Per-column filters
    private string _filterName = string.Empty;
    private string _filterArtist = string.Empty;
    private string _filterYear = string.Empty;
    private string _filterTags = string.Empty;
    private string _filterFilePath = string.Empty;
    // 0 = all, 1 = played only, 2 = not played
    private int _filterPlayed = 0;

    // Sort state
    private SongSortColumn? _sortCol = null;
    private bool _sortAsc = true;

    public PlaylistWindow(Plugin plugin) : base($"{Plugin.Name} {Language.PlaylistTitle} Editor###PlaylistWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
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
        _ = LoadPlaylistsAsync();
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    private void ResetState()
    {
        _playlists.Clear();
        _selectedPlaylist = null;
        _selectedSong = null;
        _selectedSongIndex = -1;
        _songSearchIndexes.Clear();
        _playlistSearchIndexes.Clear();
    }

    public async Task LoadPlaylistsAsync()
    {
        _isLoading = true;
        try
        {
            _playlists = await Plugin.PlaylistManager.GetAllPlaylistsAsync();
            _playlistSearchIndexes.Clear();
            _playlistSearchIndexes.AddRange(Enumerable.Range(0, _playlists.Count));

            // Auto-select: keep existing selection if still valid, otherwise pick first
            var stillExists = _selectedPlaylist != null && _playlists.Any(p => p.Id == _selectedPlaylist.Id);
            if (!stillExists)
                _selectedPlaylist = _playlists.Count > 0 ? _playlists[0] : null;

            if (_selectedPlaylist != null)
                await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadPlaylistSongsAsync(int playlistId)
    {
        _isLoading = true;
        try
        {
            var playlist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId);
            _selectedPlaylist = playlist ?? new Playlist.Playlist { Id = playlistId };
            _selectedSongIndex = -1;
            _selectedSong = null;
            SearchSongs();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool MatchesSongFilters(PlaylistSong ps)
    {
        var song = ps.Song;
        if (song == null) return false;

        if (!string.IsNullOrWhiteSpace(_songSearch) &&
            !(song.Name?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false) &&
            !(song.Artist?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false))
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

        if (!string.IsNullOrWhiteSpace(_filterTags))
        {
            var hasTags = song.Tags.Count > 0 &&
                song.Tags.Any(t => t.Name?.Contains(_filterTags, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hasTags) return false;
        }

        if (_filterPlayed != 0)
        {
            var isPlayed = ps.IsPlayed;
            if (_filterPlayed == 1 && !isPlayed) return false;
            if (_filterPlayed == 2 && isPlayed) return false;
        }

        if (!string.IsNullOrWhiteSpace(_filterFilePath) &&
            !(song.FilePath?.Contains(_filterFilePath, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;

        return true;
    }

    private void SearchSongs()
    {
        _songSearchIndexes.Clear();
        _songSearchIndexes.AddRange(
            PlaylistSongs
                .Select((ps, index) => new { ps, index })
                .Where(x => MatchesSongFilters(x.ps))
                .Select(x => x.index)
        );
    }

    private void SearchPlaylists()
    {
        _playlistSearchIndexes.Clear();

        if (string.IsNullOrWhiteSpace(_playlistSearch))
        {
            _playlistSearchIndexes.AddRange(Enumerable.Range(0, _playlists.Count));
            return;
        }

        _playlistSearchIndexes.AddRange(
            _playlists
                .Select((playlist, index) => new { playlist, index })
                .Where(x => x.playlist.Name.Contains(_playlistSearch, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index)
        );
    }

    private void ApplySortPlaylistSongs()
    {
        if (_sortCol == null)
        {
            SearchSongs();
            return;
        }

        if (_selectedPlaylist == null) return;

        IOrderedEnumerable<PlaylistSong> sorted = _sortCol.Value switch
        {
            SongSortColumn.Name => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Name) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Name),
            SongSortColumn.Artist => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Artist) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Artist),
            SongSortColumn.Year => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.ReleaseYear) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.ReleaseYear),
            SongSortColumn.Duration => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Duration) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Duration),
            SongSortColumn.PlayCount => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.PlayCount) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.PlayCount),
            SongSortColumn.LastPlayed => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.LastPlayedAt) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.LastPlayedAt),
            SongSortColumn.Rating => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Rating) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.Rating),
            SongSortColumn.FileModified => _sortAsc ? _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.FileLastModifiedAt) : _selectedPlaylist.Songs.OrderByDescending(ps => ps.Song?.FileLastModifiedAt),
            _ => _selectedPlaylist.Songs.OrderBy(ps => ps.Song?.Id)
        };

        _selectedPlaylist.Songs = sorted.ToList();
        SearchSongs();
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

        if (_showPlaylistEditorLeftPanel)
        {
            // Calculate resizable panel width
            var totalAvail = ImGui.GetContentRegionAvail().X;
            var minPanelPx = 120f * ImGuiHelpers.GlobalScale;
            var maxPanelPx = Math.Max(minPanelPx, totalAvail - minPanelPx);
            _leftPanelWidth = MathF.Max(minPanelPx, MathF.Min(_leftPanelWidth, maxPanelPx));

            // Left panel - Playlist list
            ImGui.BeginChild("##PlaylistTabs", ImGuiHelpers.ScaledVector2(_leftPanelWidth, -1), true);
            DrawLeftPanel();
            ImGui.EndChild();

            // Splitter for resizing
            ImGui.SameLine();
            DrawSplitter(ref _leftPanelWidth, minPanelPx, maxPanelPx);

            ImGui.SameLine();
        }

        // Right panel - Playlist details
        ImGui.BeginChild("PlaylistDetails", ImGuiHelpers.ScaledVector2(-1, -1), true);
        if (_selectedPlaylist != null)
        {
            DrawRightPanel();
        }
        else
        {
            ImGui.Text("Select a playlist to view details");
        }

        ImGui.EndChild();
    }

    private void DrawSplitter(ref float leftWidth, float minWidth, float maxWidth)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(0, 0)))
        {
            ImGui.InvisibleButton("##PlaylistSplitter", ImGuiHelpers.ScaledVector2(5, -1));
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var io = ImGui.GetIO();
                leftWidth += io.MouseDelta.X;
                leftWidth = MathF.Max(minWidth, MathF.Min(leftWidth, maxWidth));
            }
        }
    }

    private void DrawLeftPanel()
    {
        // fixed header
        using (ImRaii.Group())
        {
            // New playlist button
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##NewPlaylistBtn", "New Playlist"))
            {
                ImGui.OpenPopup("##NewPlaylistPopup");
            }

            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Tags, "#TagsWindowBtn", "Tags"))
            {
                Plugin.Ui.TagsWindow.Toggle();
            }

            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Music, "##SongsWindowBtn", "Song Collection"))
            {
                Plugin.Ui.SongsWindow.Toggle();
            }

            ImGui.Separator();

            // Search playlists
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PlaylistSearchInput", Language.SearchInputLabel, ref _playlistSearch, 150))
            {
                SearchPlaylists();
            }
            DrawNewPlaylistPopup();
        }

        ImGuiHelpers.ScaledDummy(0, 5);

        // Draw playlist list using indexes
        ImGui.BeginChild("##PlaylistScrolableArea");
        for (var i = 0; i < _playlistSearchIndexes.Count; i++)
        {
            var idx = _playlistSearchIndexes[i];
            var playlist = _playlists[idx];
            if (ImGui.Selectable($"{playlist.Name}##Song_{playlist.Id}", _selectedPlaylist?.Id == playlist.Id))
            {
                _selectedPlaylist = playlist;
                _ = LoadPlaylistSongsAsync(playlist.Id);
            }
        }
        ImGui.EndChild();
    }

    private void DrawRightPanel()
    {
        // Fixed header at top
        using (ImRaii.Group())
        {
            DrawRightPanelHeader();
        }

        // Scrollable content area with songs table
        ImGui.BeginChild("##PlaylistSongsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 0), false);
        DrawRightPanelContent();
        ImGui.EndChild();

        // Popups must be outside BeginChild to work properly
        DrawClearPlaylistPopup();
        DrawEditPlaylistPopup();
    }

    private void DrawRightPanelHeader()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.List, "##ShowLeftPanelBtn", "Show/Hide Left Panel"))
        {
            _showPlaylistEditorLeftPanel = !_showPlaylistEditorLeftPanel;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "##DeletePlaylistBtn", Language.ConfirmInstructionTooltip))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                DeleteSelectedPlaylistAsync();
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##EditPlaylistBtn", "Edit Playlist Name"))
        {
            if (_selectedPlaylist != null)
            {
                _editPlaylistName = _selectedPlaylist.Name ?? string.Empty;
                ImGui.OpenPopup("##EditPlaylistPopup");
            }
        }

        ImGui.SameLine();
        // Playlist header with delete button
        ImGui.Text($"Playlist: {_selectedPlaylist?.Name}");
        ImGui.Separator();

        // Import buttons + column visibility button
        DrawMenuButtons();
        ImGui.Separator();

        // Search for songs
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##PlaylistSearchSongInput", Language.SearchInputLabel, ref _songSearch, 250))
        {
            SearchSongs();
        }
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawRightPanelContent()
    {
        DrawSongList();
    }

    private void DrawViewColumnsButton()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Columns, "##PlaylistViewColumnsBtn", "Show/Hide Columns", size: Style.Dimensions.ButtonLarge))
            ImGui.OpenPopup("PlaylistColumnsPopup");

        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("PlaylistColumnsPopup");
        if (!popUp) return;

        ImGui.Text("Columns");
        ImGui.Separator();
        if (ImGui.Checkbox("Name", ref Plugin.Config.PlaylistWindowColumns.Name)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Artist", ref Plugin.Config.PlaylistWindowColumns.Artist)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Year", ref Plugin.Config.PlaylistWindowColumns.Year)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Duration", ref Plugin.Config.PlaylistWindowColumns.Duration)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Play Count", ref Plugin.Config.PlaylistWindowColumns.PlayCount)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Last Played", ref Plugin.Config.PlaylistWindowColumns.LastPlayed)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Played", ref Plugin.Config.PlaylistWindowColumns.Played)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Rating", ref Plugin.Config.PlaylistWindowColumns.Rating)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Tags", ref Plugin.Config.PlaylistWindowColumns.Tags)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("File Path", ref Plugin.Config.PlaylistWindowColumns.FilePath)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("File Modified", ref Plugin.Config.PlaylistWindowColumns.FileModified)) Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawColSortButton(string label, SongSortColumn colId)
    {
        var icon = _sortCol == colId
            ? (_sortAsc ? FontAwesomeIcon.SortAmountUp : FontAwesomeIcon.SortAmountDown)
            : FontAwesomeIcon.Sort;

        if (ImGuiUtil.IconButton(icon, $"##sortPLCol_{colId}", $"Sort by {label}"))
        {
            if (_sortCol == colId)
                _sortAsc = !_sortAsc;
            else
            {
                _sortCol = colId;
                _sortAsc = true;
            }
            ApplySortPlaylistSongs();
        }
    }

    private void DrawPlayedFilterButton()
    {
        var (icon, color, tooltip) = _filterPlayed switch
        {
            1 => (FontAwesomeIcon.Check, (Vector4?)Plugin.Config.playedSongColor, "Filter: Played"),
            2 => (FontAwesomeIcon.Times, (Vector4?)Style.Colors.Red, "Filter: Not played"),
            _ => (FontAwesomeIcon.Music, (Vector4?)null, "Filter: All")
        };

        if (ImGuiUtil.IconButton(icon, "##filterPlayedBtn", tooltip, color))
        {
            _filterPlayed = (_filterPlayed + 1) % 3;
            SearchSongs();
        }
    }

    private void DrawSongList()
    {
        // Compute dynamic column count: # and Actions always visible
        var tableColumnCount = 2;
        if (Plugin.Config.PlaylistWindowColumns.Name) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Artist) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Year) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Duration) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.PlayCount) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.LastPlayed) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Played) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Rating) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Tags) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.FilePath) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.FileModified) tableColumnCount++;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX;

        if (ImGui.BeginTable("##SongTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Name) ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.Artist) ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.Year) ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Duration) ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.PlayCount) ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.LastPlayed) ImGui.TableSetupColumn("Last Played", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Played) ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Rating) ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Tags) ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.FilePath) ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.FileModified) ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);

            // Freeze 1 header row so it stays visible while scrolling
            ImGui.TableSetupScrollFreeze(0, 1);

            // --- Combined label + filter row ---
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

            ImGui.TableNextColumn();
            ImGui.Text("#");

            ImGui.TableNextColumn();
            ImGui.Text("Actions");

            if (Plugin.Config.PlaylistWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Name", SongSortColumn.Name);
                ImGui.SameLine();
                ImGui.Text("Name");
                if (ImGui.InputTextWithHint("##PLfilterName", "Filter...", ref _filterName, 100))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Artist", SongSortColumn.Artist);
                ImGui.SameLine();
                ImGui.Text("Artist");
                if (ImGui.InputTextWithHint("##PLfilterArtist", "Filter...", ref _filterArtist, 100))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Year", SongSortColumn.Year);
                ImGui.SameLine();
                ImGui.Text("Year");
                if (ImGui.InputTextWithHint("##PLfilterYear", "Filter...", ref _filterYear, 10))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Duration", SongSortColumn.Duration);
                ImGui.SameLine();
                ImGui.Text("Duration");
            }
            if (Plugin.Config.PlaylistWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("PlayCount", SongSortColumn.PlayCount);
                ImGui.SameLine();
                ImGui.Text("Play Count");
            }
            if (Plugin.Config.PlaylistWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("LastPlayed", SongSortColumn.LastPlayed);
                ImGui.SameLine();
                ImGui.Text("Last Played");
            }
            if (Plugin.Config.PlaylistWindowColumns.Played)
            {
                ImGui.TableNextColumn();
                DrawPlayedFilterButton();
                ImGui.SameLine();
                ImGui.Text("Played");
            }
            if (Plugin.Config.PlaylistWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Rating", SongSortColumn.Rating);
                ImGui.SameLine();
                ImGui.Text("Rating");
            }
            if (Plugin.Config.PlaylistWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Tags");
                if (ImGui.InputTextWithHint("##PLfilterTags", "Filter...", ref _filterTags, 100))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text("File Path");
                if (ImGui.InputTextWithHint("##PLfilterFilePath", "Filter...", ref _filterFilePath, 200))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("FileModified", SongSortColumn.FileModified);
                ImGui.SameLine();
                ImGui.Text("File Modified");
            }

            // Use clipper for performance with large lists
            var clipper = new ImGuiListClipper();
            clipper.Begin(_songSearchIndexes.Count);

            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    if (i >= _songSearchIndexes.Count) break;

                    var songIndex = _songSearchIndexes[i];
                    if (songIndex >= PlaylistSongs.Count) continue;

                    var ps = PlaylistSongs[songIndex];
                    DrawSongEntry(i, ps, songIndex);
                }
            }

            clipper.End();
            ImGui.EndTable();
        }
    }

    private void DrawSongEntry(int displayIndex, PlaylistSong ps, int songIndex)
    {
        var song = ps.Song;
        if (song == null) return;

        ImGui.PushID($"##PlaylistSongEntry_{song.Id}");
        var textColor = song.IsValid ? Vector4.One : Style.Colors.Red;
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            // Table row
            ImGui.TableNextRow();

            // # column — always visible
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text($"{displayIndex + 1:0000}");
            ImGui.PopStyleColor();

            // Actions column — always visible
            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemoveSongBtn_{song.Id}", Language.ConfirmInstructionTooltip))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = DeleteSongAsync(song.Id);
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{song.Id}", "Edit"))
            {
                _selectedSongIndex = songIndex;
                _selectedSong = song;
                Plugin.Ui.PlaylistSongEditWindow.EditPlaylistSong(_selectedPlaylist.Id, song.Id);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(Plugin.AgentMetronome.EnsembleModeRunning || Plugin.CurrentBardPlayback.IsRunning))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, $"##LoadSongToPlaybackBtn_{song.Id}", "Load to Playback"))
                {
                    _selectedSongIndex = songIndex;
                    _selectedSong = song;
                    _ = PlaySongAsync();
                }
            }

            if (Plugin.Config.PlaylistWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                var isSelected = _selectedSongIndex == songIndex;
                if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", isSelected))
                {
                    _selectedSongIndex = songIndex;
                    _selectedSong = song;
                }
                ImGuiUtil.ToolTip(song.FilePath);
            }

            if (Plugin.Config.PlaylistWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.Artist ?? "-");
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.Duration.ToString(@"mm\:ss"));
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.PlayCount.ToString());
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.LastPlayedAt?.ToString("g") ?? "-");
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.Played)
            {
                ImGui.TableNextColumn();
                if (ps.IsPlayed)
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonSuccessNormal)
                    .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonSuccessHovered)
                    .Push(ImGuiCol.ButtonActive, Style.Components.ButtonSuccessActive))
                    {
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.Check, $"ToggleIsPlayed_{song.Id}", "Click to toggle status"))
                        {
                            _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, false);
                        }
                    }
                }
                else
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
                   .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
                   .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
                    {
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.Times, $"ToggleIsPlayed_{song.Id}", "Click to toggle status"))
                        {
                            _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, true);
                        }
                    }
                }
            }

            if (Plugin.Config.PlaylistWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");
            }

            if (Plugin.Config.PlaylistWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                var tagsText = song.Tags.Count > 0 ? string.Join(", ", song.Tags.Select(t => t.Name)) : "-";
                ImGui.Text(tagsText);
            }

            if (Plugin.Config.PlaylistWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.TextWrapped(song.FilePath);
            }

            if (Plugin.Config.PlaylistWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FileLastModifiedAt.ToString("g"));
            }
        }
        ImGui.PopID();
    }

    private void DrawMenuButtons()
    {
        using (ImRaii.Group())
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##PlaylistImportFileBtn", Language.icon_button_tooltip_import_file, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFileTask();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##PlaylistImportFolderBtn", Language.icon_button_tooltip_import_folder, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFolderTask();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FileImport, "##SongsImportSettingsBtn", "Import Rules\nDefine rules to extract info from file name", size: Style.Dimensions.ButtonLarge))
            {
                Plugin.Ui.ExtractionRulesWindow.Toggle();
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(PlaylistSongs.Count == 0 || Plugin.AgentMetronome.EnsembleModeRunning || Plugin.CurrentBardPlayback.IsRunning))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Upload, "##PlaylistLoadBtn", "Load Playlist To Playback", size: Style.Dimensions.ButtonLarge))
                {
                    if (_selectedPlaylist != null)
                    {
                        _ = LoadPlaylistToCurrentAsync(_selectedPlaylist.Id);
                    }
                }

                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##PlaylistCLear", "Clear (remove all songs)", size: Style.Dimensions.ButtonLarge))
                {
                    if (_selectedPlaylist != null)
                    {
                        ImGui.OpenPopup("ClearPlaylistPopup");
                    }
                }
            }

            ImGui.SameLine();
            ImGuiUtil.IconButton(FontAwesomeIcon.Eraser, "##ResetPlaylistPlayedStatusBtn", Language.tooltip_reset_played_status, size: Style.Dimensions.ButtonLarge);
            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _ = ResetPlaylistSongsPlayedStatusAsync();
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##PlaylistExportBtn", "Export", size: Style.Dimensions.ButtonLarge))
            {
                if (_selectedPlaylist != null)
                    Plugin.Ui.ExportWindow.OpenForPlaylist(_selectedPlaylist.Name, PlaylistSongs);
            }


            ImGui.SameLine();
            DrawViewColumnsButton();
        }
    }

    public async void RunImportFileTask()
    {
        if (_selectedPlaylist == null) return;

        var files = await _importHelper.GetMidiFilesFromFileDialogAsync(Plugin);
        if (files != null)
            StartPlaylistImport(files);
    }

    public async void RunImportFolderTask()
    {
        if (_selectedPlaylist == null) return;

        var files = await _importHelper.GetMidiFilesFromFolderDialogAsync(Plugin);
        if (files != null)
            StartPlaylistImport(files);
    }

    private void CancelImport() => _importHelper.Cancel();

    private void StartPlaylistImport(IEnumerable<string> files)
    {
        if (_selectedPlaylist == null) return;

        var playlistId = _selectedPlaylist.Id;
        var existingSongIds = PlaylistSongs.Select(ps => ps.Song?.Id ?? 0).Where(id => id > 0).ToHashSet();
        var baseOrder = PlaylistSongs.Count;

        _importHelper.OnImportCompleted = async () =>
        {
            _selectedPlaylist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId);
            await LoadPlaylistSongsAsync(playlistId);
        };

        _importHelper.StartImport(files, async (filePath, _) =>
        {
            var songRepo = ServiceContainer.SongRepository;
            var playlistRepo = ServiceContainer.PlaylistRepository;

            var song = await songRepo.GetByFilePathAsync(filePath);
            if (song == null) return;

            if (!existingSongIds.Contains(song.Id))
            {
                var order = baseOrder + _importHelper.CurrentCount;
                await playlistRepo.AddSongToPlaylistAsync(playlistId, song.Id, order);
                existingSongIds.Add(song.Id);
            }
        });
    }

    private void DrawNewPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("##NewPlaylistPopup");
        if (!popUp) return;

        ImGui.Text("New Playlist");
        ImGui.InputTextWithHint("##NewPlaylistNameInput", "Playlist Name", ref _newPlaylistName, 100);

        if (ImGui.Button("Create"))
        {
            if (!string.IsNullOrWhiteSpace(_newPlaylistName))
            {
                _ = CreatePlaylistAsync(_newPlaylistName);
                _newPlaylistName = "";
            }
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawClearPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("ClearPlaylistPopup");
        if (!popUp) return;

        ImGui.Text("Remove all songs?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This action is irreversible.");
        ImGui.Text($"Are you sure you want to remove all songs from playlist: {_selectedPlaylist?.Name}?");
        ImGui.Text($"The songs will remain in the song collection, they'll simply be detached from the current playlist.");
        ImGui.Text($"This will remove {PlaylistSongs.Count} songs from the playlist.");
        ImGui.Spacing();
        if (ImGui.Button("Clear All Songs"))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                if (_selectedPlaylist != null)
                {
                    _ = ClearPlaylistAsync(_selectedPlaylist.Id);
                }
                ImGui.CloseCurrentPopup();
            }
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawEditPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("##EditPlaylistPopup");
        if (!popup) return;

        ImGui.Text("Edit Playlist");

        ImGui.InputTextWithHint("##EditPlaylistNameInput", "Playlist Name", ref _editPlaylistName, 100);

        if (ImGui.Button("Save##SavePlaylistRename"))
        {
            _ = RenameSelectedPlaylistAsync(_editPlaylistName);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##CancelPlaylistRename"))
            ImGui.CloseCurrentPopup();
    }

    private async Task CreatePlaylistAsync(string name)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _messageDisplay.ShowError("Playlist name cannot be empty.");
            return;
        }

        // Fast-path validation to avoid hitting repository unique-index errors.
        if (_playlists.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            _messageDisplay.ShowError("Playlist name is already in use.");
            return;
        }

        var created = await Plugin.PlaylistManager.CreatePlaylistAsync(trimmedName);
        if (created == null)
        {
            // Re-check after failure to cover race conditions where another source created it first.
            await LoadPlaylistsAsync();
            var nowExists = _playlists.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
            _messageDisplay.ShowError(nowExists
                ? "Playlist name is already in use."
                : "Failed to create playlist. Check log for details.");
            return;
        }

        await LoadPlaylistsAsync();
        _messageDisplay.ShowSuccess($"Playlist created: {trimmedName}");
    }

    private async Task DeleteSelectedPlaylistAsync()
    {
        if (_selectedPlaylist == null) return;

        await Plugin.PlaylistManager.DeletePlaylistAsync(_selectedPlaylist.Id);
        _selectedPlaylist = null;
        _songSearchIndexes.Clear();
        await LoadPlaylistsAsync();
    }

    private async Task RenameSelectedPlaylistAsync(string newName)
    {
        if (_selectedPlaylist == null) return;

        var trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _messageDisplay.ShowError("Playlist name cannot be empty.");
            return;
        }

        if (_playlists.Any(p => p.Id != _selectedPlaylist.Id && p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            _messageDisplay.ShowError("Playlist name is already in use.");
            return;
        }

        if (string.Equals(_selectedPlaylist.Name, trimmedName, StringComparison.Ordinal))
            return;

        _selectedPlaylist.Name = trimmedName;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            _messageDisplay.ShowError("Failed to rename playlist. Check log for details.");
            return;
        }

        await LoadPlaylistsAsync();
        _messageDisplay.ShowSuccess($"Playlist renamed to: {trimmedName}");
    }

    private async Task DeleteSongAsync(int songId)
    {
        if (_selectedPlaylist == null) return;
        await Plugin.PlaylistManager.RemoveSongFromPlaylistAsync(_selectedPlaylist.Id, songId);
        _selectedSong = null;
        _selectedSongIndex = -1;
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }

    private async Task UpdatePlaylistSongPlayedStatusAsync(int songIndex, bool isPlayed)
    {
        if (_selectedPlaylist == null)
            return;

        if (songIndex < 0 || songIndex >= _selectedPlaylist.Songs.Count)
            return;

        var playlistSong = _selectedPlaylist.Songs[songIndex];
        if (playlistSong.IsPlayed == isPlayed)
            return;

        // Preferred path: current-playlist flow handles local update + DB persist + IPC.
        if (Plugin.PlaylistManager.CurrentPlaylist?.Id == _selectedPlaylist.Id)
        {
            // Keep the table responsive even if current playlist instance differs from selected instance.
            playlistSong.IsPlayed = isPlayed;
            await Plugin.PlaylistManager.ChangeSongPlayedStatusAsync(songIndex, isPlayed);
            SearchSongs();
            return;
        }

        var previousValue = playlistSong.IsPlayed;

        // Fallback for non-current playlists: persist directly by playlist id and broadcast full reload.
        playlistSong.IsPlayed = isPlayed;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            playlistSong.IsPlayed = previousValue;
            _messageDisplay.ShowError("Failed to update played status.");
            return;
        }

        Plugin.IpcProvider.LoadPlaylist(_selectedPlaylist.Id);

        SearchSongs();
    }

    private async Task ResetPlaylistSongsPlayedStatusAsync()
    {
        if (_selectedPlaylist == null)
            return;

        if (Plugin.PlaylistManager.CurrentPlaylist?.Id == _selectedPlaylist.Id)
        {
            foreach (var song in _selectedPlaylist.Songs)
                song.IsPlayed = false;

            await Plugin.PlaylistManager.ResetAllSongsPlayedStatusAsync();

            // resets main window filter
            Plugin.Config.SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;
            SearchSongs();
            return;
        }

        var previousValues = _selectedPlaylist.Songs.Select(s => s.IsPlayed).ToArray();

        foreach (var song in _selectedPlaylist.Songs)
            song.IsPlayed = false;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            for (int i = 0; i < _selectedPlaylist.Songs.Count; i++)
                _selectedPlaylist.Songs[i].IsPlayed = previousValues[i];
            _messageDisplay.ShowError("Failed to reset played status.");
            return;
        }

        Plugin.IpcProvider.LoadPlaylist(_selectedPlaylist.Id);
        Plugin.Config.SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;
        SearchSongs();
    }

    private async Task PlaySongAsync()
    {
        if (_selectedSong == null || _selectedPlaylist == null) return;
        await Plugin.PlaylistManager.SwitchToPlaylistAsync(_selectedPlaylist.Id);
        var currentSongs = await Plugin.PlaylistManager.GetPlaylistSongsAsync(_selectedPlaylist.Id);
        var index = currentSongs.FindIndex(s => s.Id == _selectedSong.Id);
        if (index >= 0)
        {
            await Plugin.PlaylistManager.LoadPlayback(index, false);
        }
    }

    private async Task LoadPlaylistToCurrentAsync(int playlistId)
    {
        var playlist = await Plugin.PlaylistManager.LoadPlaylistToCurrentAsync(playlistId);
        if (playlist == null) return;

        _selectedPlaylist = playlist;
        _selectedSongIndex = -1;
        _selectedSong = null;
        SearchSongs();

        _messageDisplay.ShowSuccess($"Loaded playlist: {playlist.Name}");
    }

    private async Task ClearPlaylistAsync(int playlistId)
    {
        await Plugin.PlaylistManager.ClearPlaylistAsync(playlistId);
        await LoadPlaylistSongsAsync(playlistId);
        _messageDisplay.ShowSuccess("Playlist cleared!");
    }

    private void DrawImportProgress()
    {
        ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
        .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
        .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            if (ImGui.Button("Cancel"))
                CancelImport();
        }
    }
}
