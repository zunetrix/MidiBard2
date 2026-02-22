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

using MidiBard.Extensions.DryWetMidi;
using MidiBard.Resources;

using PlaylistModel = MidiBard.Playlist.Playlist;
using SongModel = MidiBard.Playlist.Song;

namespace MidiBard;

public class PlaylistWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<PlaylistModel> _playlists = new();
    private PlaylistModel? _selectedPlaylist;
    private List<SongModel> _playlistSongs = new();
    private SongModel? _selectedSong;
    private int _selectedSongIndex = -1;
    private Playlist.PlaylistSong? _selectedPlaylistSong;

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
    private string _editAddedAt = string.Empty;
    private bool _editIsPlayed = false;
    private string _editTag = string.Empty;

    // New playlist
    private string _newPlaylistName = string.Empty;

    // Search
    private readonly List<int> _songSearchIndexes = new();
    private readonly List<int> _playlistSearchIndexes = new();
    private string _songSearch = string.Empty;
    private string _playlistSearch = string.Empty;

    // Panel resizing
    private float _leftPanelWidth = 200f;

    private bool _isLoading;

    public PlaylistWindow(Plugin plugin) : base($"{Plugin.Name} {Language.PlaylistTitle}###PlaylistWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => base.PreDraw();

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadPlaylistsAsync();
    }

    public override void OnClose() => base.OnClose();

    private async Task LoadPlaylistsAsync()
    {
        if (Plugin.PlaylistManager == null) return;
        _isLoading = true;
        try
        {
            _playlists = await Plugin.PlaylistManager.GetAllPlaylistsAsync();
            _playlistSearchIndexes.Clear();
            _playlistSearchIndexes.AddRange(Enumerable.Range(0, _playlists.Count));
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
            _selectedSongIndex = -1;
            _selectedSong = null;
            _songSearchIndexes.Clear();
            _songSearchIndexes.AddRange(Enumerable.Range(0, _playlistSongs.Count));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SearchSongs()
    {
        _songSearchIndexes.Clear();

        if (string.IsNullOrWhiteSpace(_songSearch))
        {
            _songSearchIndexes.AddRange(Enumerable.Range(0, _playlistSongs.Count));
            return;
        }

        _songSearchIndexes.AddRange(
            _playlistSongs
                .Select((song, index) => new { song, index })
                .Where(x =>
                    (x.song.Name?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.song.Artist?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false))
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

    public override void Draw()
    {
        if (_isLoading)
        {
            DrawSpinner("PlaylistLoading");
            ImGui.SameLine();
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
        // New playlist button
        if (ImGui.Button("+ New Playlist"))
        {
            ImGui.OpenPopup("NewPlaylistPopup");
        }

        ImGui.Separator();

        // Search playlists
        if (ImGui.InputText("Search", ref _playlistSearch, 100))
        {
            SearchPlaylists();
        }

        // Draw playlist list using indexes
        foreach (var idx in _playlistSearchIndexes)
        {
            var playlist = _playlists[idx];
            if (ImGui.Selectable(playlist.Name, _selectedPlaylist?.Id == playlist.Id))
            {
                _selectedPlaylist = playlist;
                _ = LoadPlaylistSongsAsync(playlist.Id);
            }
        }

        DrawNewPlaylistPopup();
    }

    private void DrawRightPanel()
    {
        // Playlist header
        ImGui.Text($"Playlist: {_selectedPlaylist.Name}");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(0, 10);
        ImGui.SameLine();
        if (ImGui.Button("Delete Playlist"))
        {
            ImGui.OpenPopup("DeletePlaylistPopup");
        }
        ImGui.Separator();

        // Import buttons
        DrawImportButtons();
        ImGui.Separator();

        // Search songs
        if (ImGui.InputText("Search songs", ref _songSearch, 100))
        {
            SearchSongs();
        }

        // Song list with clipper for performance
        DrawSongList();

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

        DrawDeletePlaylistPopup();
    }

    private void DrawSongList()
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();

        ImGui.BeginChild("SongList", ImGuiHelpers.ScaledVector2(-1, 400), true);

        // Table configuration
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                        ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV;
        var tableColumnCount = 6;

        if (ImGui.BeginTable("##SongTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

            // Use clipper for performance with large lists
            var clipper = new ImGuiListClipper();
            clipper.Begin(_songSearchIndexes.Count, lineHeight);

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

        ImGui.EndChild();
    }



    private void DrawSongEntry(int displayIndex, SongModel song, int songIndex)
    {
        ImGui.PushID($"##song_{songIndex}");

        // Get PlaylistSong data
        var playlistSong = _selectedPlaylist?.Songs.FirstOrDefault(ps => ps.Song?.Id == song.Id);
        var isPlayed = playlistSong?.IsPlayed ?? false;

        // Table row
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.Text($"{displayIndex + 1:00}");

        // Name column
        ImGui.TableNextColumn();
        var isSelected = _selectedSongIndex == songIndex;
        if (ImGui.Selectable(song.Name ?? "Unknown", isSelected))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            _selectedPlaylistSong = playlistSong;
            LoadEditFields(song);
        }

        // Artist column
        ImGui.TableNextColumn();
        ImGui.Text(song.Artist ?? "-");

        // Duration column
        ImGui.TableNextColumn();
        ImGui.Text(song.Duration.ToString(@"mm\:ss"));

        // Played column
        ImGui.TableNextColumn();
        ImGui.Text(isPlayed ? "✓" : "-");

        // Actions column
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, $"##PlaySongBtn_{songIndex}", "Play"))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            _selectedPlaylistSong = playlistSong;
            _ = PlaySongAsync();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{songIndex}", "Edit"))
        {
            LoadEditFields(song);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteSongBtn_{songIndex}", "Delete"))
        {
            _ = DeleteSongAsync(song.Id);
        }

        ImGui.PopID();
    }

    private void DrawSongEditPanel()
    {
        // FilePath - read only
        ImGui.Text("FilePath:");
        ImGui.TextWrapped(_editFilePath);

        ImGui.InputText("Name", ref _editName, 200);
        ImGui.InputText("Artist", ref _editArtist, 200);
        ImGui.InputInt("Year", ref _editReleaseYear);
        ImGui.SliderInt("Rating", ref _editRating, 1, 10);

        // Duration and PlayCount - read only
        ImGui.Text($"Duration: {_editDuration}");
        ImGui.Text($"PlayCount: {_editPlayCount}");
        ImGui.Text($"LastPlayed: {_editLastPlayedAt}");

        // Playlist-specific info
        ImGui.Text($"IsPlayed: {(_editIsPlayed ? "Yes" : "No")}");
        ImGui.Text($"Added: {_editAddedAt}");

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

        ImGui.SameLine();

        if (ImGui.Button("Play"))
        {
            _ = PlaySongAsync();
        }
    }

    private void DrawImportButtons()
    {
        ImGui.BeginGroup();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##btnPlaylistImportFile", Language.icon_button_tooltip_import_file, size: Style.Dimensions.PlayerButton))
        {
            RunImportFileTask();
        }
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##btnPlaylistImportFolder", Language.icon_button_tooltip_import_folder, size: Style.Dimensions.PlayerButton))
        {
            RunImportFolderTask();
        }
        ImGui.EndGroup();
    }

    public async void RunImportFileTask()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        _isLoading = true;
        try
        {
            CheckLastOpenedFolderPath();
            if (Plugin.Config.useLegacyFileDialog)
                await RunImportFileTaskWin32Async();
            else
                await RunImportFileTaskImGuiAsync();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when importing files: {e}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    public async void RunImportFolderTask()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        _isLoading = true;
        try
        {
            CheckLastOpenedFolderPath();
            if (Plugin.Config.useLegacyFileDialog)
                await RunImportFolderTaskWin32Async();
            else
                await RunImportFolderTaskImGuiAsync();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error during folder import: {e}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private Task RunImportFileTaskWin32Async()
    {
        var tcs = new TaskCompletionSource<bool>();
        MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true && filePaths is { Length: > 0 })
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ImportFilesAsync(filePaths);
                        Plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during file import: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, initialDirectory: Plugin.Config.lastOpenedFolderPath);
        return tcs.Task;
    }

    private async Task RunImportFileTaskImGuiAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        void OnFileDialogResult(bool result, List<string> filePaths)
        {
            if (result && filePaths.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ImportFilesAsync(filePaths);
                        Plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during file import: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }
        try
        {
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog("Open", ".mid,.midi", OnFileDialogResult, 0, Plugin.Config.lastOpenedFolderPath);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Failed to open file dialog: {e}");
            tcs.TrySetException(e);
        }
        await tcs.Task;
    }

    private async Task RunImportFolderTaskWin32Async()
    {
        var tcs = new TaskCompletionSource<bool>();
        MidiBard.Win32.FileDialogs.FolderPicker((result, folderPath) =>
        {
            if (result == true && !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await ImportFilesAsync(files);
                        Plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during folder import: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, initialDirectory: Plugin.Config.lastOpenedFolderPath);
        await tcs.Task;
    }

    private async Task RunImportFolderTaskImGuiAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Open folder", (result, folderPath) =>
        {
            if (result && Directory.Exists(folderPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await ImportFilesAsync(files);
                        Plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during folder import: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, Plugin.Config.lastOpenedFolderPath);
        await tcs.Task;
    }

    private async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        if (_selectedPlaylist == null) return;

        var playlistId = _selectedPlaylist.Id;
        var songRepo = MidiBard.Playlist.ServiceContainer.TryGet<MidiBard.Playlist.ISongRepository>();
        var playlistRepo = MidiBard.Playlist.ServiceContainer.TryGet<MidiBard.Playlist.IPlaylistRepository>();

        if (songRepo == null || playlistRepo == null) return;

        foreach (var filePath in filePaths)
        {
            try
            {
                var duration = TimeSpan.Zero;
                var midiFile = Plugin.PlaylistManager.LoadSongFile(filePath);
                if (midiFile != null)
                {
                    duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                }

                var song = await songRepo.CreateOrGetSongAsync(
                    filePath,
                    Path.GetFileNameWithoutExtension(filePath),
                    "", 0, duration);

                var order = _playlistSongs.Count;
                await playlistRepo.AddSongToPlaylistAsync(playlistId, song.Id, order);
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Warning(e, $"Error adding song: {filePath}");
            }
        }

        // Reload the selected playlist to get the updated songs from database
        _selectedPlaylist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(_selectedPlaylist.Id);
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }

    private void CheckLastOpenedFolderPath()
    {
        if (!Directory.Exists(Plugin.Config.lastOpenedFolderPath))
        {
            Plugin.Config.lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    private void DrawNewPlaylistPopup()
    {
        if (ImGui.BeginPopup("NewPlaylistPopup"))
        {
            ImGui.Text("New Playlist");
            ImGui.InputText("Name", ref _newPlaylistName, 100);

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
            ImGui.Text($"Are you sure you want to delete '{_selectedPlaylist?.Name}'?");

            if (ImGui.Button("Yes, Delete"))
            {
                _ = DeletePlaylistAsync();
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

    private async Task SaveSongAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null || _selectedPlaylist == null) return;

        // Update song properties
        _selectedSong.Name = _editName;
        _selectedSong.Artist = _editArtist;
        _selectedSong.ReleaseYear = _editReleaseYear;
        _selectedSong.Rating = _editRating;

        // Save to database
        await Plugin.PlaylistManager.UpdateSongAsync(_selectedSong);

        // Reload playlist to get fresh data from database
        _selectedPlaylist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(_selectedPlaylist.Id);
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }

    private async Task DeleteSongAsync(int songId)
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;
        await Plugin.PlaylistManager.RemoveSongFromPlaylistAsync(_selectedPlaylist.Id, songId);
        _selectedSong = null;
        _selectedSongIndex = -1;
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
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

    private async Task PlaySongAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null || _selectedPlaylist == null) return;
        await Plugin.PlaylistManager.SwitchToPlaylistAsync(_selectedPlaylist.Id);
        var currentSongs = await Plugin.PlaylistManager.GetPlaylistSongsAsync(_selectedPlaylist.Id);
        var index = currentSongs.FindIndex(s => s.Id == _selectedSong.Id);
        if (index >= 0)
        {
            await Plugin.PlaylistManager.LoadPlayback(index, true);
        }
    }

    private void LoadEditFields(SongModel song)
    {
        var playlistSong = _selectedPlaylist?.Songs.FirstOrDefault(ps => ps.Song?.Id == song.Id);

        _editFilePath = song.FilePath ?? "";
        _editName = song.Name ?? "";
        _editArtist = song.Artist ?? "";
        _editReleaseYear = song.ReleaseYear;
        _editRating = song.Rating;
        _editDuration = song.Duration.ToString(@"mm\:ss");
        _editPlayCount = song.PlayCount;
        _editLastPlayedAt = song.LastPlayedAt?.ToString("g") ?? "-";
        _editCreatedAt = song.CreatedAt.ToString("g");
        _editUpdatedAt = song.UpdatedAt.ToString("g");
        _editAddedAt = playlistSong?.AddedAt.ToString("g") ?? "-";
        _editIsPlayed = playlistSong?.IsPlayed ?? false;
    }

    private void DrawSpinner(string id)
    {
        var spinnerLabel = $"##Spinner_{id}";
        // var spinnerRadius = ImGui.GetTextLineHeight() / 4;
        var spinnerRadius = ImGui.GetTextLineHeight();
        var spinnerThickness = 5 * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + spinnerRadius);
        ImGuiUtil.Spinner(spinnerLabel, spinnerRadius, spinnerThickness, Style.Colors.Blue);
    }
}
