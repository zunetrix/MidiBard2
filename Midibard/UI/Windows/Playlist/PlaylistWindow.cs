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
    private List<Song> _playlistSongs = new();
    private Song? _selectedSong;
    private int _selectedSongIndex = -1;

    // PlaylistSong lookup - maps SongId to PlaylistSong for fast access
    private readonly Dictionary<int, PlaylistSong> _playlistSongLookup = new();

    // Form state
    private string _newPlaylistName = string.Empty;

    // Search
    private readonly List<int> _songSearchIndexes = new();
    private readonly List<int> _playlistSearchIndexes = new();
    private string _songSearch = string.Empty;
    private string _playlistSearch = string.Empty;

    // Panel resizing
    private float _leftPanelWidth = 200f;

    private bool _isLoading;

    // Import helper (progress tracking, dialog, cancellation)
    private readonly SongImportHelper _importHelper;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    // Column visibility (song table in right panel)
    private bool _showColName = true;
    private bool _showColArtist = true;
    private bool _showColYear = false;
    private bool _showColDuration = true;
    private bool _showColPlayCount = false;
    private bool _showColLastPlayed = false;
    private bool _showColPlayed = false;
    private bool _showColRating = false;
    private bool _showColTags = true;
    private bool _showColFilePath = false;
    private bool _showColFileModified = false;

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

    public PlaylistWindow(Plugin plugin) : base($"{Plugin.Name} {Language.PlaylistTitle}###PlaylistWindow")
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
        _playlistSongs.Clear();
        _selectedSong = null;
        _selectedSongIndex = -1;
        _playlistSongLookup.Clear();
        _songSearchIndexes.Clear();
        _playlistSearchIndexes.Clear();
    }

    public async Task LoadPlaylistsAsync()
    {
        if (Plugin.PlaylistManager == null) return;
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
        if (Plugin.PlaylistManager == null) return;
        _isLoading = true;
        try
        {
            _playlistSongs = await Plugin.PlaylistManager.GetPlaylistSongsAsync(playlistId);

            // Load PlaylistSong lookup for fast access to IsPlayed and AddedAt
            var playlistSongData = await Plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId);
            _playlistSongLookup.Clear();
            if (playlistSongData?.Songs != null)
            {
                foreach (var ps in playlistSongData.Songs)
                {
                    if (ps.Song?.Id > 0)
                    {
                        _playlistSongLookup[ps.Song.Id] = ps;
                    }
                }
            }

            // Only keep essential playlist info, don't load all songs
            if (_selectedPlaylist?.Id != playlistId)
            {
                _selectedPlaylist = new Playlist.Playlist { Id = playlistId };
            }
            if (playlistSongData != null)
            {
                _selectedPlaylist.Name = playlistSongData.Name;
                _selectedPlaylist.CreatedAt = playlistSongData.CreatedAt;
                _selectedPlaylist.UpdatedAt = playlistSongData.UpdatedAt;
            }

            _selectedSongIndex = -1;
            _selectedSong = null;
            SearchSongs();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool MatchesSongFilters(Song song)
    {
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
            var playlistSong = _playlistSongLookup.GetValueOrDefault(song.Id);
            var isPlayed = playlistSong?.IsPlayed ?? false;
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
            _playlistSongs
                .Select((song, index) => new { song, index })
                .Where(x => MatchesSongFilters(x.song))
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

        IOrderedEnumerable<Song> sorted = _sortCol.Value switch
        {
            SongSortColumn.Name => _sortAsc ? _playlistSongs.OrderBy(s => s.Name) : _playlistSongs.OrderByDescending(s => s.Name),
            SongSortColumn.Artist => _sortAsc ? _playlistSongs.OrderBy(s => s.Artist) : _playlistSongs.OrderByDescending(s => s.Artist),
            SongSortColumn.Year => _sortAsc ? _playlistSongs.OrderBy(s => s.ReleaseYear) : _playlistSongs.OrderByDescending(s => s.ReleaseYear),
            SongSortColumn.Duration => _sortAsc ? _playlistSongs.OrderBy(s => s.Duration) : _playlistSongs.OrderByDescending(s => s.Duration),
            SongSortColumn.PlayCount => _sortAsc ? _playlistSongs.OrderBy(s => s.PlayCount) : _playlistSongs.OrderByDescending(s => s.PlayCount),
            SongSortColumn.LastPlayed => _sortAsc ? _playlistSongs.OrderBy(s => s.LastPlayedAt) : _playlistSongs.OrderByDescending(s => s.LastPlayedAt),
            SongSortColumn.Rating => _sortAsc ? _playlistSongs.OrderBy(s => s.Rating) : _playlistSongs.OrderByDescending(s => s.Rating),
            SongSortColumn.FileModified => _sortAsc ? _playlistSongs.OrderBy(s => s.FileLastModifiedAt) : _playlistSongs.OrderByDescending(s => s.FileLastModifiedAt),
            _ => _playlistSongs.OrderBy(s => s.Id)
        };

        _playlistSongs = sorted.ToList();
        SearchSongs();
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
            ImGui.Text("Loading...");
            return;
        }

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
        var splitterId = "##PlaylistSplitter";
        var splitterWidth = 6f * ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
        ImGui.InvisibleButton(splitterId, new Vector2(splitterWidth, -1));
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var io = ImGui.GetIO();
            leftWidth += io.MouseDelta.X;
            leftWidth = MathF.Max(minWidth, MathF.Min(leftWidth, maxWidth));
        }
        ImGui.PopStyleVar();
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

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Music, "##SongsWindowBtn", "Songs"))
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
        ImGui.BeginChild("##PlaylistScrolavleArea");
        foreach (var idx in _playlistSearchIndexes)
        {
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
        DrawDeletePlaylistPopup();
        DrawClearPlaylistPopup();
    }

    private void DrawRightPanelHeader()
    {
        // Display message if there's one
        _messageDisplay.Draw();

        // Playlist header with delete button
        ImGui.Text($"Playlist: {_selectedPlaylist?.Name}");
        ImGui.SameLine();
        if (ImGui.Button("Delete Playlist"))
        {
            ImGui.OpenPopup("DeletePlaylistPopup");
        }
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
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Columns, "##PlaylistViewColumnsBtn", "Show/Hide Columns", size: Style.Dimensions.PlayerButton))
            ImGui.OpenPopup("PlaylistColumnsPopup");

        if (ImGui.BeginPopup("PlaylistColumnsPopup"))
        {
            ImGui.Text("Columns");
            ImGui.Separator();
            ImGui.Checkbox("Name", ref _showColName);
            ImGui.Checkbox("Artist", ref _showColArtist);
            ImGui.Checkbox("Year", ref _showColYear);
            ImGui.Checkbox("Duration", ref _showColDuration);
            ImGui.Checkbox("Play Count", ref _showColPlayCount);
            ImGui.Checkbox("Last Played", ref _showColLastPlayed);
            ImGui.Checkbox("Played", ref _showColPlayed);
            ImGui.Checkbox("Rating", ref _showColRating);
            ImGui.Checkbox("Tags", ref _showColTags);
            ImGui.Checkbox("File Path", ref _showColFilePath);
            ImGui.Checkbox("File Modified", ref _showColFileModified);
            ImGui.EndPopup();
        }
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
        if (_showColName) tableColumnCount++;
        if (_showColArtist) tableColumnCount++;
        if (_showColYear) tableColumnCount++;
        if (_showColDuration) tableColumnCount++;
        if (_showColPlayCount) tableColumnCount++;
        if (_showColLastPlayed) tableColumnCount++;
        if (_showColPlayed) tableColumnCount++;
        if (_showColRating) tableColumnCount++;
        if (_showColTags) tableColumnCount++;
        if (_showColFilePath) tableColumnCount++;
        if (_showColFileModified) tableColumnCount++;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX;

        if (ImGui.BeginTable("##SongTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);
            if (_showColName) ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (_showColArtist) ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            if (_showColYear) ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            if (_showColDuration) ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            if (_showColPlayCount) ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            if (_showColLastPlayed) ImGui.TableSetupColumn("Last Played", ImGuiTableColumnFlags.WidthFixed);
            if (_showColPlayed) ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed);
            if (_showColRating) ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            if (_showColTags) ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
            if (_showColFilePath) ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            if (_showColFileModified) ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);

            // Freeze 1 header row so it stays visible while scrolling
            ImGui.TableSetupScrollFreeze(0, 1);

            // --- Combined label + filter row ---
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

            ImGui.TableNextColumn();
            ImGui.Text("#");

            ImGui.TableNextColumn();
            ImGui.Text("Actions");

            if (_showColName)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Name", SongSortColumn.Name);
                ImGui.SameLine();
                ImGui.Text("Name");
                if (ImGui.InputTextWithHint("##PLfilterName", "Filter...", ref _filterName, 100))
                    SearchSongs();
            }
            if (_showColArtist)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Artist", SongSortColumn.Artist);
                ImGui.SameLine();
                ImGui.Text("Artist");
                if (ImGui.InputTextWithHint("##PLfilterArtist", "Filter...", ref _filterArtist, 100))
                    SearchSongs();
            }
            if (_showColYear)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Year", SongSortColumn.Year);
                ImGui.SameLine();
                ImGui.Text("Year");
                if (ImGui.InputTextWithHint("##PLfilterYear", "Filter...", ref _filterYear, 10))
                    SearchSongs();
            }
            if (_showColDuration)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Duration", SongSortColumn.Duration);
                ImGui.SameLine();
                ImGui.Text("Duration");
            }
            if (_showColPlayCount)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("PlayCount", SongSortColumn.PlayCount);
                ImGui.SameLine();
                ImGui.Text("Play Count");
            }
            if (_showColLastPlayed)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("LastPlayed", SongSortColumn.LastPlayed);
                ImGui.SameLine();
                ImGui.Text("Last Played");
            }
            if (_showColPlayed)
            {
                ImGui.TableNextColumn();
                DrawPlayedFilterButton();
                ImGui.SameLine();
                ImGui.Text("Played");
            }
            if (_showColRating)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Rating", SongSortColumn.Rating);
                ImGui.SameLine();
                ImGui.Text("Rating");
            }
            if (_showColTags)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Tags");
                if (ImGui.InputTextWithHint("##PLfilterTags", "Filter...", ref _filterTags, 100))
                    SearchSongs();
            }
            if (_showColFilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text("File Path");
                if (ImGui.InputTextWithHint("##PLfilterFilePath", "Filter...", ref _filterFilePath, 200))
                    SearchSongs();
            }
            if (_showColFileModified)
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
                    if (songIndex >= _playlistSongs.Count) continue;

                    var song = _playlistSongs[songIndex];
                    DrawSongEntry(i, song, songIndex);
                }
            }

            clipper.End();
            ImGui.EndTable();
        }
    }

    private void DrawSongEntry(int displayIndex, Song song, int songIndex)
    {
        ImGui.PushID($"##PlaylistSongEntry_{song.Id}");

        // Get PlaylistSong data from lookup (fast O(1) access instead of O(n) search)
        var playlistSong = _playlistSongLookup.GetValueOrDefault(song.Id);
        var isPlayed = playlistSong?.IsPlayed ?? false;

        // Determine text color based on HasValidFilePath
        var textColor = song.IsValid ? Vector4.One : Style.Colors.Yellow;

        // Table row
        ImGui.TableNextRow();

        // # column — always visible
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text($"{displayIndex + 1:0000}");
        ImGui.PopStyleColor();

        // Actions column — always visible
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemoveSongBtn_{song.Id}", Language.DeleteInstructionTooltip))
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
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, $"##LoadSongToPlaybackBtn_{song.Id}", "Load to Playback"))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            _ = PlaySongAsync();
        }

        if (_showColName)
        {
            ImGui.TableNextColumn();
            var isSelected = _selectedSongIndex == songIndex;
            if (ImGui.Selectable($"({song.Id}) {song.Name}##Song_{song.Id}", isSelected))
            {
                _selectedSongIndex = songIndex;
                _selectedSong = song;
            }
        }

        if (_showColArtist)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text(song.Artist ?? "-");
            ImGui.PopStyleColor();
        }

        if (_showColYear)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
            ImGui.PopStyleColor();
        }

        if (_showColDuration)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text(song.Duration.ToString(@"mm\:ss"));
            ImGui.PopStyleColor();
        }

        if (_showColPlayCount)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text(song.PlayCount.ToString());
            ImGui.PopStyleColor();
        }

        if (_showColLastPlayed)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text(song.LastPlayedAt?.ToString("dd/MM/yy HH:mm") ?? "-");
            ImGui.PopStyleColor();
        }

        if (_showColPlayed)
        {
            ImGui.TableNextColumn();
            var (icon, color) = isPlayed
                ? (FontAwesomeIcon.Check, Plugin.Config.playedSongColor)
                : (FontAwesomeIcon.Times, Style.Colors.RedVivid);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(icon.ToIconString());
            ImGui.PopFont();
            ImGui.PopStyleColor();
        }

        if (_showColRating)
        {
            ImGui.TableNextColumn();
            ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");
        }

        if (_showColTags)
        {
            ImGui.TableNextColumn();
            var tagsText = song.Tags.Count > 0 ? string.Join(", ", song.Tags.Select(t => t.Name)) : "-";
            ImGui.Text(tagsText);
        }

        if (_showColFilePath)
        {
            ImGui.TableNextColumn();
            ImGui.TextWrapped(song.FilePath);
        }

        if (_showColFileModified)
        {
            ImGui.TableNextColumn();
            ImGui.Text(song.FileLastModifiedAt.ToString("g"));
        }
        ImGui.PopID();
    }

    private void DrawMenuButtons()
    {
        ImGui.BeginGroup();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##PlaylistImportFileBtn", Language.icon_button_tooltip_import_file, size: Style.Dimensions.PlayerButton))
        {
            RunImportFileTask();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##PlaylistImportFolderBtn", Language.icon_button_tooltip_import_folder, size: Style.Dimensions.PlayerButton))
        {
            RunImportFolderTask();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Upload, "##PlaylistLoadBtn", "Load playlist", size: Style.Dimensions.PlayerButton))
        {
            if (_selectedPlaylist != null)
            {
                _ = LoadPlaylistToCurrentAsync(_selectedPlaylist.Id);
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##PlaylistCLear", "Clear (remove all songs)", size: Style.Dimensions.PlayerButton))
        {
            if (_selectedPlaylist != null)
            {
                ImGui.OpenPopup("ClearPlaylistPopup");
            }
        }

        ImGui.SameLine();
        DrawViewColumnsButton();

        ImGui.EndGroup();
    }

    public async void RunImportFileTask()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        var files = await _importHelper.GetMidiFilesFromFileDialogAsync(Plugin);
        if (files != null)
            StartPlaylistImport(files);
    }

    public async void RunImportFolderTask()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        var files = await _importHelper.GetMidiFilesFromFolderDialogAsync(Plugin);
        if (files != null)
            StartPlaylistImport(files);
    }

    private void CancelImport() => _importHelper.Cancel();

    private void StartPlaylistImport(IEnumerable<string> files)
    {
        if (_selectedPlaylist == null) return;

        var playlistId = _selectedPlaylist.Id;
        var existingSongIds = _playlistSongs.Select(s => s.Id).ToHashSet();
        var baseOrder = _playlistSongs.Count;

        _importHelper.OnImportCompleted = async () =>
        {
            _selectedPlaylist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId);
            await LoadPlaylistSongsAsync(playlistId);
        };

        _importHelper.StartImport(files, async (filePath, _) =>
        {
            var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
            var playlistRepo = ServiceContainer.GetServiceOrNull<IPlaylistRepository>();
            if (songRepo == null || playlistRepo == null) return;

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
        if (ImGui.BeginPopup("##NewPlaylistPopup"))
        {
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

            ImGui.EndPopup();
        }
    }

    private void DrawDeletePlaylistPopup()
    {
        if (ImGui.BeginPopup("DeletePlaylistPopup"))
        {
            ImGui.Text($"Are you sure you want to delete playlist '{_selectedPlaylist?.Name}'?");

            if (ImGui.Button("Delete Playlist"))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = DeletePlaylistAsync();
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGuiUtil.ToolTip(Language.DeleteInstructionTooltip);

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawClearPlaylistPopup()
    {
        if (ImGui.BeginPopup("ClearPlaylistPopup"))
        {
            ImGui.Text("Remove all songs?");
            ImGui.Separator();
            ImGui.TextColored(Style.Colors.Red, "This action is irreversible.");
            ImGui.Text($"Are you sure you want to remove all songs from playlist: {_selectedPlaylist?.Name}?");
            ImGui.Text($"The songs will remain in the library, they'll simply be detached from the current playlist.");
            ImGui.Text($"This will remove {_playlistSongs.Count} songs from the playlist.");
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
            ImGuiUtil.ToolTip(Language.DeleteInstructionTooltip);

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private async Task CreatePlaylistAsync(string name)
    {
        if (Plugin.PlaylistManager == null) return;
        await Plugin.PlaylistManager.CreatePlaylistAsync(name);
        await LoadPlaylistsAsync();
    }

    private async Task DeletePlaylistAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;
        await Plugin.PlaylistManager.DeletePlaylistAsync(_selectedPlaylist.Id);
        _selectedPlaylist = null;
        _playlistSongs.Clear();
        _songSearchIndexes.Clear();
        await LoadPlaylistsAsync();
    }

    private async Task DeleteSongAsync(int songId)
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;
        await Plugin.PlaylistManager.RemoveSongFromPlaylistAsync(_selectedPlaylist.Id, songId);
        _selectedSong = null;
        _selectedSongIndex = -1;
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }

    private async Task PlaySongAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null || _selectedPlaylist == null) return;
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
        if (Plugin.PlaylistManager == null) return;
        await Plugin.PlaylistManager.LoadPlaylistToCurrentAsync(playlistId);
        await LoadPlaylistSongsAsync(playlistId);
        _messageDisplay.ShowSuccess($"Loaded playlist: {_selectedPlaylist?.Name ?? ""}");
    }

    private async Task ClearPlaylistAsync(int playlistId)
    {
        if (Plugin.PlaylistManager == null) return;
        await Plugin.PlaylistManager.ClearPlaylistAsync(playlistId);
        await LoadPlaylistSongsAsync(playlistId);
        _messageDisplay.ShowSuccess("Playlist cleared!");
    }

    private void DrawImportProgress()
    {
        ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());

        if (ImGui.Button("Cancel Import"))
            CancelImport();
    }
}
