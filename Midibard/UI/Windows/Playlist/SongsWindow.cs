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
using MidiBard.Playlist;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public class SongsWindow : Window
{
    private Plugin Plugin { get; }

    // UI State
    private List<Song> _songs = new();
    private Song? _selectedSong;
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
    private int _selectedTagIndex = -1;
    private List<Tag> _availableTags = new();

    // Search
    private readonly List<int> _searchIndexes = new();
    private string _search = string.Empty;

    private bool _isLoading;

    // Components
    private readonly ImGuiMessageDisplay _messageDisplay = new();
    private readonly ImGuiModalEditor<Song> _songEditorModal = new("##SongEditModal", ImGuiHelpers.ScaledVector2(600, 400));

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
            var songRepo = ServiceContainer.TryGet<Playlist.ISongRepository>();
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

        // Modal for editing
        _songEditorModal.Draw();
    }

    private void DrawHeader()
    {
        // Display message if there's one
        _messageDisplay.Draw();

        // Fixed search input at top
        if (ImGui.InputTextWithHint("##SongsSearchInput", Language.SearchInputLabel, ref _search, 200))
        {
            Search();
        }
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawSongTable()
    {
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var tableColumnCount = 8;

        if (ImGui.BeginTable("##SongsTable", tableColumnCount, ImGuiTableFlags.Resizable))
        {
            // Setup columns with headers
            ImGui.TableSetupColumn("##col_num", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
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

        var isSelected = _selectedSongIndex == songIndex;

        // Table row
        ImGui.TableNextRow();

        // # column
        ImGui.TableNextColumn();
        ImGui.Text($"{displayIndex + 1:0000}");

        // Name column
        ImGui.TableNextColumn();
        ImGui.Text($"({song.Id}) ");
        ImGui.SameLine();

        if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", isSelected))
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

        // Play Count column
        ImGui.TableNextColumn();
        ImGui.Text(song.PlayCount.ToString());

        // Rating column
        ImGui.TableNextColumn();
        ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");

        // Actions column
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{songIndex}", "Edit"))
        {
            _selectedSongIndex = songIndex;
            _selectedSong = song;
            LoadEditFields(song);
            _songEditorModal.Show(
                $"Edit Song: {song.Name}",
                song,
                (modal, songData) => DrawSongEditContent(songData),
                (songData) => _ = SaveSongAsync()
            );
        }
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteSongBtn_{songIndex}", "Delete"))
        {
            _ = DeleteSongAsync(song.Id);
        }

        ImGui.PopID();
    }

    private void LoadEditFields(Song song)
    {
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
    }

    private void DrawSongEditContent(Song song)
    {
        // Load available tags
        LoadAvailableTags();

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

        // Tags section
        ImGui.Separator();
        ImGui.Text("Tags:");

        // Add tag section - Select from existing tags (excluding already added tags)
        if (_availableTags.Count > 0)
        {
            // Filter out tags that are already added to this song
            var availableTagsForAdd = _availableTags
                .Where(t => !_selectedSong.Tags.Any(st => st.Id == t.Id))
                .ToList();

            if (availableTagsForAdd.Count > 0)
            {
                var tagNames = availableTagsForAdd.Select(t => t.Name).ToList();

                if (ImGui.Combo("Add existing tag", ref _selectedTagIndex, tagNames.ToArray(), tagNames.Count))
                {
                    // Selection changed
                }

                ImGui.SameLine();
                if (ImGui.Button("Add Tag##AddExistingTagBtn"))
                {
                    if (_selectedTagIndex >= 0 && _selectedTagIndex < availableTagsForAdd.Count)
                    {
                        _ = AddExistingTagAsync(availableTagsForAdd[_selectedTagIndex]);
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("All tags already added to this song");
            }
        }

        using (ImRaii.Child("##TagsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 200), false))
        {
            // Display current tags with remove button
            if (_selectedSong.Tags.Count > 0)
            {
                foreach (var tag in _selectedSong.Tags.ToList())
                {
                    ImGui.PushID($"##tag_{tag.Id}");
                    ImGui.Text($"[{tag.Name}]");
                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Times, "##removeTag", "Remove"))
                    {
                        _ = RemoveTagAsync(tag.Name);
                    }
                    ImGui.PopID();
                }
            }
            else
            {
                ImGui.Text("No tags");
            }
        }
    }

    private void LoadAvailableTags()
    {
        var tagRepo = ServiceContainer.TryGet<ITagRepository>();
        if (tagRepo != null)
        {
            _availableTags = tagRepo.GetAllAsync().Result;
        }
    }

    private async Task SaveSongAsync()
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null) return;

        // Update song properties
        _selectedSong.Name = _editName;
        _selectedSong.Artist = _editArtist;
        _selectedSong.ReleaseYear = _editReleaseYear;
        _selectedSong.Rating = _editRating;

        // Save to database
        await Plugin.PlaylistManager.UpdateSongAsync(_selectedSong);

        // Reload songs list
        await LoadSongsAsync();
    }

    private async Task DeleteSongAsync(int songId)
    {
        if (Plugin.PlaylistManager == null) return;

        var songRepo = ServiceContainer.TryGet<ISongRepository>();
        if (songRepo != null)
        {
            await songRepo.DeleteAsync(songId);
        }

        _selectedSong = null;
        _selectedSongIndex = -1;
        await LoadSongsAsync();
    }

    private async Task AddExistingTagAsync(Tag tag)
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null || tag == null) return;

        // Check if tag already exists on song
        if (_selectedSong.Tags.Any(t => t.Id == tag.Id)) return;

        await Plugin.PlaylistManager.AddTagToSongAsync(_selectedSong.Id, tag.Name);
        var updatedSong = await Plugin.PlaylistManager.GetSongByIdAsync(_selectedSong.Id);
        if (updatedSong != null)
        {
            _selectedSong = updatedSong;
        }
    }

    private async Task RemoveTagAsync(string tagName)
    {
        if (Plugin.PlaylistManager == null || _selectedSong == null || string.IsNullOrWhiteSpace(tagName)) return;

        await Plugin.PlaylistManager.RemoveTagFromSongAsync(_selectedSong.Id, tagName);
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
