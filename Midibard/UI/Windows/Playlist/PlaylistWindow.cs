using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using System.Threading;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.DryWetMidi;
using MidiBard.Resources;
using MidiBard.Playlist;
using MidiBard.Playlist.Services;

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

    // Import progress tracking
    private int _importTotalCount;
    private int _importCurrentCount;
    private bool _isImporting;
    private CancellationTokenSource? _importCts;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    public PlaylistWindow(Plugin plugin) : base($"{Plugin.Name} {Language.PlaylistTitle}###PlaylistWindow")
    {
        Plugin = plugin;
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
        // Show import progress if importing
        if (_isImporting)
        {
            DrawImportProgress();
        }

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

        ImGuiHelpers.ScaledDummy(0, 5);

        // Draw playlist list using indexes
        foreach (var idx in _playlistSearchIndexes)
        {
            var playlist = _playlists[idx];
            if (ImGui.Selectable($"{playlist.Name}##Song_{playlist.Id}", _selectedPlaylist?.Id == playlist.Id))
            {
                _selectedPlaylist = playlist;
                _ = LoadPlaylistSongsAsync(playlist.Id);
            }
        }

        DrawNewPlaylistPopup();
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

        // Import buttons
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

    private void DrawSongList()
    {
        var tableColumnCount = 13;
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV; // ImGuiTableFlags.Resizable;

        if (ImGui.BeginTable("##SongTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Last Played", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

            ImGui.TableSetupScrollFreeze(0, 1);

            // Draw header
            ImGui.TableHeadersRow();

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
        ImGui.PushID($"##song_{song.Id}");

        // Get PlaylistSong data from lookup (fast O(1) access instead of O(n) search)
        // Don't assign to _selectedPlaylistSong here - it's done in LoadEditFields when user clicks
        var playlistSong = _playlistSongLookup.GetValueOrDefault(song.Id);
        var isPlayed = playlistSong?.IsPlayed ?? false;

        // Determine text color based on HasValidFilePath
        var textColor = song.HasValidFilePath ? Vector4.One : Style.Colors.Yellow;

        // Table row
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text($"{displayIndex + 1:00}");
        ImGui.PopStyleColor();

        // Name column
        ImGui.TableNextColumn();
        var isSelected = _selectedSongIndex == songIndex;
        if (ImGui.Selectable($"({song.Id}) {song.Name}##Song_{song.Id}", isSelected))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
        }

        // Artist column
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text(song.Artist ?? "-");
        ImGui.PopStyleColor();

        // Year column
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
        ImGui.PopStyleColor();

        // Duration column
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text(song.Duration.ToString(@"mm\:ss"));
        ImGui.PopStyleColor();

        // Play Count column
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text(song.PlayCount.ToString());
        ImGui.PopStyleColor();

        // Last Played column
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.Text(song.LastPlayedAt?.ToString("dd/MM/yy HH:mm") ?? "-");
        ImGui.PopStyleColor();

        // Played column
        ImGui.TableNextColumn();
        ImGui.Text(isPlayed ? "✓" : "-");

        // Rating column
        ImGui.TableNextColumn();
        ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");

        // Tags column
        ImGui.TableNextColumn();
        var tagsText = song.Tags.Count > 0 ? string.Join(", ", song.Tags.Select(t => t.Name)) : "-";
        ImGui.Text(tagsText);

        // FilePath column
        ImGui.TableNextColumn();
        ImGui.TextWrapped(song.FilePath);

        // File Modified column
        ImGui.TableNextColumn();
        ImGui.Text(song.FileLastModifiedAt?.ToString("g") ?? "-");


        // Actions column
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
            Plugin.Ui.EditPlaylistSongWindow.EditPlaylistSong(_selectedPlaylist!.Id, song.Id);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, $"##ChangeSongFilePathTableBtn_{song.Id}", "Change File Path"))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            _ = ChangeFilePathAsync(song.Id);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, $"##LoadSongToPlaybackBtn_{song.Id}", "Load to Playback"))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            _ = PlaySongAsync();
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
        ImGui.EndGroup();
    }

    public async void RunImportFileTask()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        // Cancel any existing import and create new cancellation token
        _importCts?.Cancel();
        _importCts = new CancellationTokenSource();
        var token = _importCts.Token;

        try
        {
            CheckLastOpenedFolderPath();
            if (Plugin.Config.useLegacyFileDialog)
                await RunImportFileTaskWin32Async(token);
            else
                await RunImportFileTaskImGuiAsync(token);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when importing files: {e}");
        }
        finally
        {
            _isImporting = false;
        }
    }

    public async void RunImportFolderTask()
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        // Cancel any existing import and create new cancellation token
        _importCts?.Cancel();
        _importCts = new CancellationTokenSource();
        var token = _importCts.Token;

        try
        {
            CheckLastOpenedFolderPath();
            if (Plugin.Config.useLegacyFileDialog)
                await RunImportFolderTaskWin32Async(token);
            else
                await RunImportFolderTaskImGuiAsync(token);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error during folder import: {e}");
        }
        finally
        {
            _isImporting = false;
        }
    }

    private void CancelImport()
    {
        _importCts?.Cancel();
    }

    private Task RunImportFileTaskWin32Async(CancellationToken cancellationToken)
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
                        await ImportFilesAsync(filePaths, cancellationToken);
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

    private async Task RunImportFileTaskImGuiAsync(CancellationToken cancellationToken)
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
                        await ImportFilesAsync(filePaths, cancellationToken);
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

    private async Task RunImportFolderTaskWin32Async(CancellationToken cancellationToken)
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
                        await ImportFilesAsync(files, cancellationToken);
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

    private async Task RunImportFolderTaskImGuiAsync(CancellationToken cancellationToken)
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
                        var allowedExtensions = new[] { ".mid", ".midi" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));

                        await ImportFilesAsync(files, cancellationToken);
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

    private async Task ImportFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken)
    {
        if (_selectedPlaylist == null) return;

        // Initialize progress tracking
        var filePathList = filePaths.ToList();
        _importTotalCount = filePathList.Count;
        _importCurrentCount = 0;
        _isImporting = true;

        var playlistId = _selectedPlaylist.Id;
        var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
        var playlistRepo = ServiceContainer.GetServiceOrNull<IPlaylistRepository>();

        if (songRepo == null || playlistRepo == null) return;

        // Get existing song IDs in this playlist to avoid duplicates
        var existingSongIds = _playlistSongs.Select(s => s.Id).ToHashSet();

        // Pre-load all existing songs from database for faster lookup (batch query)
        var allSongs = await songRepo.GetAllSongsAsync();
        var songByPath = allSongs.ToDictionary(s => s.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePathList)
        {
            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                _messageDisplay.ShowWarning("Import cancelled!");
                break;
            }

            try
            {
                // Check if song already exists in our pre-loaded dictionary
                if (songByPath.TryGetValue(filePath, out var existingSong))
                {
                    // Song exists in database - just add to playlist if not already there
                    if (!existingSongIds.Contains(existingSong.Id))
                    {
                        var order = _playlistSongs.Count + _importCurrentCount;
                        await playlistRepo.AddSongToPlaylistAsync(playlistId, existingSong.Id, order);
                        existingSongIds.Add(existingSong.Id);
                    }
                }
                else
                {
                    // Song doesn't exist - need to create it (load midi file for duration)
                    var duration = TimeSpan.Zero;
                    var midiFileService = ServiceContainer.GetService<IMidiFileService>();
                    var midiFile = midiFileService?.LoadMidiFile(filePath);
                    if (midiFile != null)
                    {
                        duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                    }

                    var song = await songRepo.CreateOrGetSongAsync(
                        filePath,
                        Path.GetFileNameWithoutExtension(filePath),
                        "", 0, duration, true);

                    // Set file last modified date directly on the object
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            song.FileLastModifiedAt = File.GetLastWriteTimeUtc(filePath);
                        }
                        catch { /* ignore */ }
                    }

                    var order = _playlistSongs.Count + _importCurrentCount;
                    await playlistRepo.AddSongToPlaylistAsync(playlistId, song.Id, order);
                    existingSongIds.Add(song.Id);

                    // Add to dictionary for subsequent lookups
                    songByPath[filePath] = song;
                }

                // Update progress
                _importCurrentCount++;
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
        if (ImGui.BeginPopup("##NewPlaylistPopup"))
        {
            ImGui.Text("New Playlist");
            ImGui.InputTextWithHint("##NewPlaylistNameInput", "Playlist", ref _newPlaylistName, 100);

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

    private void DrawClearPlaylistPopup()
    {
        if (ImGui.BeginPopup("ClearPlaylistPopup"))
        {
            ImGui.Text($"Are you sure you want to remove all songs from '{_selectedPlaylist?.Name}'?");
            ImGui.Text($"This will remove {_playlistSongs.Count} songs from the playlist.");

            if (ImGui.Button("Yes, Clear"))
            {
                if (_selectedPlaylist != null)
                {
                    _ = ClearPlaylistAsync(_selectedPlaylist.Id);
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

    private void DrawSpinner(string id)
    {
        var spinnerLabel = $"##Spinner_{id}";
        // var spinnerRadius = ImGui.GetTextLineHeight() / 4;
        var spinnerRadius = ImGui.GetTextLineHeight();
        var spinnerThickness = 5 * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + spinnerRadius);
        ImGuiUtil.Spinner(spinnerLabel, spinnerRadius, spinnerThickness, Style.Colors.Blue);
    }

    private void DrawImportProgress()
    {
        // Progress bar
        var progress = _importTotalCount > 0 ? (float)_importCurrentCount / _importTotalCount : 0f;
        ImGui.ProgressBar(progress, ImGuiHelpers.ScaledVector2(-1, 20), $"Importing: {_importCurrentCount}/{_importTotalCount} - {progress * 100:F1}%");

        // Cancel button
        if (ImGui.Button("Cancel Import"))
        {
            CancelImport();
        }
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
            await Plugin.PlaylistManager.UpdateSongAsync(song);

            // Sync file data (validate path and recalculate duration)
            await Plugin.PlaylistManager.SyncSongFileDataAsync(song);

            // Reload playlist to refresh the UI
            if (_selectedPlaylist != null)
            {
                await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
            }
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

}

