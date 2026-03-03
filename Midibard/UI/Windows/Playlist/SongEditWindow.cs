using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;

namespace MidiBard;

/// <summary>
/// Window for editing a Song in the global song library (independent of any playlist context).
/// </summary>
public class SongEditWindow : Window
{
    private Plugin Plugin { get; }
    private readonly SongImportHelper _importHelper;

    // ID of the song being edited
    private int _songId = -1;
    private bool _isLoading = true;

    // Edit state - nested class for organization
    private class EditState
    {
        // Song fields (global)
        public string EditName = string.Empty;
        public string EditArtist = string.Empty;
        public int EditReleaseYear = 0;
        public int EditRating = 0;
        public int EditPlayCount = 0;
        public string EditFilePath = string.Empty;
        public string EditDuration = string.Empty;

        // Tag management
        public List<Tag> AvailableTags = new();  // Tags not yet assigned to the song
        public List<Tag> SongTags = new();        // Tags currently assigned to the song
        public int SelectedTagIndex = 0;
    }

    private EditState _editState = new();

    public SongEditWindow(Plugin plugin)
        : base($"{Plugin.Name} Edit Song###SongEditWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);

        Size = ImGuiHelpers.ScaledVector2(600, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>
    /// Initialize editing for a specific global Song.
    /// Call this to open the window with data loaded.
    /// </summary>
    public void EditSong(int songId)
    {
        ResetState();

        _songId = songId;
        _isLoading = true;

        // Load data asynchronously
        _ = LoadDataAsync();

        this.Toggle();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            var songService = ServiceContainer.GetServiceOrNull<ISongService>();
            var tagRepo = ServiceContainer.GetServiceOrNull<ITagRepository>();

            if (songService == null)
                return;

            // Load song via service
            var song = await songService.GetByIdAsync(_songId);

            if (song == null)
            {
                DalamudApi.PluginLog.Warning("[SongEditWindow] Song not found: {SongId}", _songId);
                return;
            }

            // Populate edit state
            _editState.EditName = song.Name ?? "";
            _editState.EditArtist = song.Artist ?? "";
            _editState.EditReleaseYear = song.ReleaseYear;
            _editState.EditRating = song.Rating;
            _editState.EditPlayCount = song.PlayCount;
            _editState.EditFilePath = song.FilePath ?? "";
            _editState.EditDuration = song.Duration.ToString(@"mm\:ss");

            // Split tags into assigned and available
            if (tagRepo != null)
            {
                var allTags = await tagRepo.GetAllAsync();
                var assignedTagIds = song.Tags.Select(t => t.Id).ToHashSet();
                _editState.SongTags = song.Tags.ToList();
                _editState.AvailableTags = allTags.Where(t => !assignedTagIds.Contains(t.Id)).ToList();
            }

            _isLoading = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongEditWindow] Error loading song data");
            _isLoading = false;
        }
    }

    public override void PreDraw()
    {
        base.PreDraw();
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    private void ResetState()
    {
        _songId = -1;
        _editState = new EditState();
        _isLoading = true;
    }

    public override void Draw()
    {
        if (_isLoading)
        {
            ImGuiUtil.DrawColoredBanner("Loading...", Style.Colors.Violet);
            return;
        }

        DrawEditForm();
    }

    private void DrawEditForm()
    {
        ImGui.Text("Song Name:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##EditSongName", ref _editState.EditName, 256);

        ImGui.Text("Artist:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##EditSongArtist", ref _editState.EditArtist, 256);

        ImGui.Text("Release Year:");
        ImGui.InputInt("##EditSongYear", ref _editState.EditReleaseYear);
        if (_editState.EditReleaseYear < 0) _editState.EditReleaseYear = 0;

        ImGui.Text("Rating:");
        ImGui.SliderInt("##EditSongRating", ref _editState.EditRating, 0, 5);

        ImGui.Text("Play Count:");
        if (ImGui.InputInt("##EditSongPlayCount", ref _editState.EditPlayCount, 1, 1, flags: ImGuiInputTextFlags.AutoSelectAll))
        {
            if (_editState.EditPlayCount < 0)
                _editState.EditPlayCount = 0;
        }

        ImGui.Text($"Duration: {_editState.EditDuration}");

        ImGui.Text("File Path:");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##ChangeSongFilePath", "Change File Path"))
        {
            _ = ChangeFilePathAsync();
        }
        ImGui.TextWrapped(_editState.EditFilePath);

        ImGui.Separator();

        ImGui.Text("Tags:");

        // Combobox to add available tags
        if (_editState.AvailableTags.Count > 0)
        {
            var tagNames = _editState.AvailableTags.Select(t => t.Name).ToArray();
            if (ImGui.Combo("##EditSongAddTagCombo", ref _editState.SelectedTagIndex, tagNames, tagNames.Length))
            {
                if (_editState.SelectedTagIndex >= 0 && _editState.SelectedTagIndex < _editState.AvailableTags.Count)
                {
                    var tag = _editState.AvailableTags[_editState.SelectedTagIndex];
                    _editState.AvailableTags.RemoveAt(_editState.SelectedTagIndex);
                    _editState.SongTags.Add(tag);
                    _editState.SelectedTagIndex = 0;
                }
            }
        }
        else if (_editState.SongTags.Count > 0)
        {
            ImGui.TextDisabled("All tags already added to this song");
        }

        // Show assigned tags in a scrollable area
        using (ImRaii.Child("##TagsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 150), false))
        {
            if (_editState.SongTags.Count > 0)
            {
                foreach (var tag in _editState.SongTags.ToList())
                {
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Times, $"##RemoverPlaylistSongTag_{tag.Id}", "Remove"))
                    {
                        _editState.SongTags.Remove(tag);
                        _editState.AvailableTags.Add(tag);
                        _editState.AvailableTags.Sort((a, b) =>
                            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                        _editState.SelectedTagIndex = 0;
                    }
                    ImGui.SameLine();
                    ImGui.Text($"[{tag.Name}]");
                }
            }
            else
            {
                ImGui.Text("No tags");
            }
        }

        ImGui.Separator();

        if (ImGui.Button("Save##EditSongSave", ImGuiHelpers.ScaledVector2(100, 0)))
        {
            _ = SaveAsync();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel##EditSongCancel", ImGuiHelpers.ScaledVector2(100, 0)))
        {
            this.IsOpen = false;
        }
    }

    private async Task ChangeFilePathAsync()
    {
        var newFilePath = await _importHelper.GetMidiFilePathAsync(Plugin, Path.GetDirectoryName(_editState.EditFilePath));

        if (string.IsNullOrWhiteSpace(newFilePath) || newFilePath == _editState.EditFilePath)
            return;

        _editState.EditFilePath = newFilePath;
        _editState.EditName = Path.GetFileNameWithoutExtension(newFilePath);

        var midiFileService = ServiceContainer.GetServiceOrNull<IMidiFileService>();
        if (midiFileService != null)
        {
            var duration = await midiFileService.CalculateDurationFromFileAsync(newFilePath);
            _editState.EditDuration = duration.ToString(@"mm\:ss");
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var songService = ServiceContainer.GetServiceOrNull<ISongService>();

            if (songService == null)
            {
                DalamudApi.PluginLog.Error("[SongEditWindow] Song service not available");
                return;
            }

            // Load, update, and save song via service
            var song = await songService.GetByIdAsync(_songId);
            if (song != null)
            {
                song.Name = _editState.EditName;
                song.Artist = _editState.EditArtist;
                song.ReleaseYear = _editState.EditReleaseYear;
                song.Rating = _editState.EditRating;
                song.PlayCount = _editState.EditPlayCount;
                song.FilePath = _editState.EditFilePath;
                song.Tags = _editState.SongTags;
                song.UpdatedAt = DateTime.UtcNow;

                await songService.UpdateAsync(song);

                // Sync file metadata (HasValidFilePath, FileLastModifiedAt, Duration) from disk
                if (Plugin.PlaylistManager != null)
                    await Plugin.PlaylistManager.SyncSongFileDataAsync(song);

                DalamudApi.PluginLog.Information("[SongEditWindow] Saved changes for song {SongId}", _songId);
            }

            this.IsOpen = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongEditWindow] Error saving changes");
        }
    }
}
