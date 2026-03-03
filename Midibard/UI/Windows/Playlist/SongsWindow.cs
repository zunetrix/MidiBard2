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

        ImGui.EndGroup();
    }

    private void RunImportFileTask()
    {
        CheckLastOpenedFolderPath();

        if (Plugin.Config.useLegacyFileDialog)
        {
            MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
            {
                if (result == true && filePaths is { Length: > 0 })
                {
                    _importHelper.StartImport(filePaths, AddSongCallback);
                }
            }, Plugin.Config.lastOpenedFolderPath);
        }
        else
        {
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Open MIDI Files",
                ".mid,.midi",
                (result, filePaths) =>
                {
                    if (result && filePaths.Count > 0)
                    {
                        _importHelper.StartImport(filePaths, AddSongCallback);
                    }
                },
                0,
                Plugin.Config.lastOpenedFolderPath
            );
        }
    }

    private void RunImportFolderTask()
    {
        CheckLastOpenedFolderPath();

        if (Plugin.Config.useLegacyFileDialog)
        {
            MidiBard.Win32.FileDialogs.FolderPicker((result, folderPath) =>
            {
                if (result == true && !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                {
                    var allowedExtensions = new[] { ".mid", ".midi" };
                    var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                        .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                    _importHelper.StartImport(files, AddSongCallback);
                    Plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                }
            }, Plugin.Config.lastOpenedFolderPath);
        }
        else
        {
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Open folder", (result, folderPath) =>
            {
                if (result && Directory.Exists(folderPath))
                {
                    var allowedExtensions = new[] { ".mid", ".midi" };
                    var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                        .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                    _importHelper.StartImport(files, AddSongCallback);
                    Plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                }
            }, Plugin.Config.lastOpenedFolderPath);
        }
    }

    // Callback for adding song - just returns completed task since songs are already created in the helper
    private Task AddSongCallback(string filePath, TimeSpan duration)
    {
        // Songs are already added to database in the helper
        // Just reload after import completes
        return Task.CompletedTask;
    }

    private void CheckLastOpenedFolderPath()
    {
        if (!Directory.Exists(Plugin.Config.lastOpenedFolderPath))
        {
            Plugin.Config.lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    private void DrawSongTable()
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var tableColumnCount = 10;
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable;

        if (ImGui.BeginTable("##SongsTable", tableColumnCount, tableFlags))
        {
            // Setup columns with headers
            ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);


            ImGui.TableSetupScrollFreeze(0, 1);

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

    private void DrawSongRow(int displayIndex, Song song, int songIndex)
    {
        ImGui.PushID($"##song_{song.Id}");

        // Determine text color
        var textColor = song.HasValidFilePath ? Vector4.One : Style.Colors.Yellow;
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            // Table row
            ImGui.TableNextRow();

            // # column
            ImGui.TableNextColumn();

            ImGui.Text($"{displayIndex + 1:0000}");

            // Name column
            ImGui.TableNextColumn();
            ImGui.Text($"({song.Id}) ");
            ImGui.SameLine();

            if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", false))
            {
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

            // Play Count column
            ImGui.TableNextColumn();
            ImGui.Text(song.PlayCount.ToString());

            // Rating column
            ImGui.TableNextColumn();
            ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");

            // FilePath column
            ImGui.TableNextColumn();
            ImGui.Text(song.FilePath);

            // File Modified column
            ImGui.TableNextColumn();
            ImGui.Text(song.FileLastModifiedAt?.ToString("g") ?? "-");

        }

        // Actions column
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
            Plugin.Ui.EditSongWindow.EditSong(song.Id);
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
        var newFilePath = await ShowFileDialogAsync(song.FilePath);

        if (!string.IsNullOrWhiteSpace(newFilePath) && newFilePath != originalFilePath)
        {
            song.FilePath = newFilePath;
            song.Name = Path.GetFileNameWithoutExtension(newFilePath);

            // Set HasValidFilePath to true since user selected a valid file
            song.HasValidFilePath = File.Exists(newFilePath);

            await Plugin.PlaylistManager.UpdateSongAsync(song);

            // Sync file data (recalculate duration)
            await Plugin.PlaylistManager.SyncSongFileDataAsync(song);

            // Reload songs to refresh the UI
            await LoadSongsAsync();
        }
    }

    private Task<string?> ShowFileDialogAsync(string currentFilePath)
    {
        var tcs = new TaskCompletionSource<string?>();

        if (Plugin.Config.useLegacyFileDialog)
        {
            MidiBard.Win32.FileDialogs.OpenMidiFileDialog((result, filePaths) =>
            {
                if (result == true && filePaths is { Length: > 0 })
                {
                    tcs.TrySetResult(filePaths[0]);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            }, Path.GetDirectoryName(currentFilePath));
        }
        else
        {
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Select MIDI File",
                ".mid,.midi",
                (result, filePaths) =>
                {
                    if (result && filePaths.Count > 0)
                    {
                        tcs.TrySetResult(filePaths[0]);
                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }
                },
                1,
                Path.GetDirectoryName(currentFilePath)
            );
        }

        return tcs.Task;
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
