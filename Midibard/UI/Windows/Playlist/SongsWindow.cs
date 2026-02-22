using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;

using SongModel = MidiBard.Playlist.Song;

namespace MidiBard;

public class SongsWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<SongModel> _songs = new();
    private SongModel? _selectedSong;
    private int _selectedSongIndex = -1;

    // Edit state
    private string _editFilePath = string.Empty;
    private string _editName = string.Empty;
    private string _editArtist = string.Empty;
    private int _editReleaseYear = 0;
    private int _editRating = 0;
    private string _editDuration = string.Empty;
    private int _editPlayCount = 0;
    private string _editLastPlayedAt = string.Empty;
    private string _editCreatedAt = string.Empty;
    private string _editUpdatedAt = string.Empty;
    private string _editTag = string.Empty;

    // Search
    private readonly List<int> _searchIndexes = new();
    private string _search = string.Empty;

    private bool _isLoading;

    public SongsWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SongsTitle}###SongsWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadSongsAsync();
    }

    private async Task LoadSongsAsync()
    {
        if (Plugin.PlaylistManager == null) return;
        _isLoading = true;
        try
        {
            var songRepo = MidiBard.Playlist.ServiceContainer.TryGet<Playlist.ISongRepository>();
            if (songRepo != null)
            {
                _songs = await songRepo.GetAllSongsAsync();
                _searchIndexes.Clear();
                _searchIndexes.AddRange(Enumerable.Range(0, _songs.Count));
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void Search()
    {
        _searchIndexes.Clear();

        if (string.IsNullOrWhiteSpace(_search))
        {
            _searchIndexes.AddRange(Enumerable.Range(0, _songs.Count));
            return;
        }

        _searchIndexes.AddRange(
            _songs
                .Select((song, index) => new { song, index })
                .Where(x =>
                    (x.song.Name?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.song.Artist?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.song.FilePath?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(x => x.index)
        );
    }

    public override void Draw()
    {
        if (_isLoading)
        {
            ImGui.Text("Loading...");
            return;
        }

        // Song table with clipper
        DrawSongTable();

        // Edit panel
        ImGui.Separator();
        ImGui.Text("Edit Selected Song:");

        if (_selectedSong != null)
        {
            DrawSongEditPanel();
        }
        else
        {
            ImGui.Text("Select a song to edit");
        }
    }

    private void DrawSongTable()
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();

        // Search bar
        if (ImGui.InputText("Search", ref _search, 200))
        {
            Search();
        }
        ImGui.Separator();

        // Table configuration
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                        ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV;
        var tableColumnCount = 7;

        if (ImGui.BeginTable("##SongsTable", tableColumnCount, tableFlags))
        {
            // Setup columns with headers
            ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

            // Draw header row
            ImGui.TableHeadersRow();

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

    private void DrawSongRow(int displayIndex, SongModel song, int songIndex)
    {
        ImGui.PushID($"##song_{songIndex}");

        var isSelected = _selectedSongIndex == songIndex;

        // Table row
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.Text($"{displayIndex + 1:00}");

        // Name column
        ImGui.TableNextColumn();
        if (ImGui.Selectable(song.Name ?? "Unknown", isSelected))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            LoadEditFields(song);
        }

        // Artist column
        ImGui.TableNextColumn();
        ImGui.Text(song.Artist ?? "-");

        // Year column
        ImGui.TableNextColumn();
        ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");

        // Duration column
        ImGui.TableNextColumn();
        ImGui.Text(song.Duration.ToString(@"mm\:ss"));

        // Rating column
        ImGui.TableNextColumn();
        ImGui.Text(song.Rate > 0 ? new string('★', song.Rate) : "-");

        // Actions column
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{songIndex}", "Edit"))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            LoadEditFields(song);
        }
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteSongBtn_{songIndex}", "Delete"))
        {
            _ = DeleteSongAsync(song.Id);
        }

        ImGui.PopID();
    }

    private void LoadEditFields(SongModel song)
    {
        _editFilePath = song.FilePath ?? "";
        _editName = song.Name ?? "";
        _editArtist = song.Artist ?? "";
        _editReleaseYear = song.ReleaseYear;
        _editRating = song.Rate;
        _editDuration = song.Duration.ToString(@"mm\:ss");
        _editPlayCount = song.PlayCount;
        _editLastPlayedAt = song.LastPlayedAt?.ToString("g") ?? "-";
        _editCreatedAt = song.CreatedAt.ToString("g");
        _editUpdatedAt = song.UpdatedAt.ToString("g");
    }

    private void DrawSongEditPanel()
    {
        // FilePath - with change button
        ImGui.Text("FilePath:");
        ImGui.TextWrapped(_editFilePath);
        if (ImGui.Button("Change File Path"))
        {
            ChangeFilePath();
        }

        ImGui.InputText("Name", ref _editName, 200);
        ImGui.InputText("Artist", ref _editArtist, 200);
        ImGui.InputInt("Year", ref _editReleaseYear);
        ImGui.SliderInt("Rating", ref _editRating, 1, 10);

        // Duration and PlayCount - read only
        ImGui.Text($"Duration: {_editDuration}");
        ImGui.Text($"PlayCount: {_editPlayCount}");
        ImGui.Text($"LastPlayed: {_editLastPlayedAt}");

        // Song timestamps
        ImGui.Text($"Created: {_editCreatedAt}");
        ImGui.Text($"Updated: {_editUpdatedAt}");

        ImGui.Text("Tags:");
        foreach (var tag in _selectedSong.Tags)
        {
            ImGui.SameLine();
            ImGui.Text($"[{tag}]");
        }

        ImGui.InputText("Add Tag", ref _editTag, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add##tag"))
        {
            _ = AddTagAsync();
        }

        ImGui.Separator();

        if (ImGui.Button("Save Changes"))
        {
            _ = SaveSongAsync();
        }

        ImGui.SameLine();

        if (ImGui.Button("Delete Song") && _selectedSong != null)
        {
            _ = DeleteSongAsync(_selectedSong.Id);
        }
    }

    private async Task SaveSongAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null) return;

        // Update song properties
        _selectedSong.Name = _editName;
        _selectedSong.Artist = _editArtist;
        _selectedSong.ReleaseYear = _editReleaseYear;
        _selectedSong.Rate = _editRating;

        // Save to database
        await Plugin.PlaylistManager.UpdateSongAsync(_selectedSong);

        // Reload songs list
        await LoadSongsAsync();
    }

    private async Task DeleteSongAsync(int songId)
    {
        if (Plugin.PlaylistManager == null) return;

        var songRepo = MidiBard.Playlist.ServiceContainer.TryGet<MidiBard.Playlist.ISongRepository>();
        if (songRepo != null)
        {
            await songRepo.DeleteAsync(songId);
        }

        _selectedSong = null;
        _selectedSongIndex = -1;
        await LoadSongsAsync();
    }

    private async Task AddTagAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null || string.IsNullOrWhiteSpace(_editTag)) return;
        await Plugin.PlaylistManager.AddTagToSongAsync(_selectedSong.Id, _editTag);
        _editTag = "";
        var updatedSong = await Plugin.PlaylistManager.GetSongByIdAsync(_selectedSong.Id);
        if (updatedSong != null)
        {
            _selectedSong = updatedSong;
        }
    }

    private void ChangeFilePath()
    {
        if (_selectedSong == null) return;

        if (Plugin.Config.useLegacyFileDialog)
        {
            MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
            {
                if (result == true && filePaths is { Length: > 0 })
                {
                    _selectedSong.FilePath = filePaths[0];
                    _editFilePath = filePaths[0];
                    // Auto-update name from filename
                    if (string.IsNullOrWhiteSpace(_editName))
                    {
                        _selectedSong.Name = Path.GetFileNameWithoutExtension(filePaths[0]);
                        _editName = _selectedSong.Name;
                    }
                }
            }, Path.GetDirectoryName(_selectedSong.FilePath));
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Select MIDI File",
                ".mid,.midi",
                (result, filePaths) =>
                {
                    if (result && filePaths.Count > 0)
                    {
                        _selectedSong.FilePath = filePaths[0];
                        _editFilePath = filePaths[0];
                        // Auto-update name from filename
                        if (string.IsNullOrWhiteSpace(_editName))
                        {
                            _selectedSong.Name = Path.GetFileNameWithoutExtension(filePaths[0]);
                            _editName = _selectedSong.Name;
                        }
                    }
                    tcs.TrySetResult(result);
                },
                1,
                Path.GetDirectoryName(_selectedSong.FilePath)
            );
        }
    }
}
