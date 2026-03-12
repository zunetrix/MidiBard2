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
using MidiBard.Playlist.Helpers;

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
        public string EditComments = string.Empty;

        // File path change
        public bool UpdateSongName = false;
        public string FilePathError = string.Empty;

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
            var songService = ServiceContainer.SongService;

            // Load song via service
            var song = await songService.GetByIdAsync(_songId);

            if (song == null)
            {
                DalamudApi.PluginLog.Warning($"[SongEditWindow] Song not found: {_songId}");
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
            _editState.EditComments = song.Comments ?? "";

            // Split tags into assigned and available
            var allTags = await ServiceContainer.TagService.GetAllAsync();
            var assignedTagIds = song.Tags.Select(t => t.Id).ToHashSet();
            _editState.SongTags = song.Tags.ToList();
            _editState.AvailableTags = allTags.Where(t => !assignedTagIds.Contains(t.Id)).ToList();

            _isLoading = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongEditWindow] Error loading song data");
            _isLoading = false;
        }
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    public async Task RefreshTagsAsync()
    {
        if (_songId < 0) return;
        var allTags = await ServiceContainer.TagService.GetAllAsync();
        var allTagIds = allTags.Select(t => t.Id).ToHashSet();
        var tagById = allTags.ToDictionary(t => t.Id);

        _editState.SongTags = _editState.SongTags.Where(t => allTagIds.Contains(t.Id)).ToList();
        foreach (var st in _editState.SongTags)
            if (tagById.TryGetValue(st.Id, out var updated)) st.Name = updated.Name;

        var assignedIds = _editState.SongTags.Select(t => t.Id).ToHashSet();
        _editState.AvailableTags = allTags.Where(t => !assignedIds.Contains(t.Id)).ToList();
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
        ImGui.Checkbox("Update Song Name##UpdateSongName", ref _editState.UpdateSongName);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##ChangeSongFilePath", "Change File Path"))
        {
            _ = ChangeFilePathAsync();
        }
        ImGui.SameLine();
        ImGui.TextWrapped(_editState.EditFilePath);

        if (!string.IsNullOrEmpty(_editState.FilePathError))
            ImGui.TextColored(Style.Colors.Red, _editState.FilePathError);

        ImGui.Text("Comments:");
        ImGui.InputTextMultiline("##EditSongComments", ref _editState.EditComments, 1024, ImGuiHelpers.ScaledVector2(-1, 80));

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

        ImGui.Separator();

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
                    ImGui.Text(tag.Name);
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

        var existing = await ServiceContainer.SongRepository.GetByFilePathAsync(newFilePath);
        if (existing != null && existing.Id != _songId)
        {
            _editState.FilePathError = $"File already registered as \"{existing.Name}\".";
            return;
        }

        _editState.FilePathError = string.Empty;
        _editState.EditFilePath = newFilePath;

        if (_editState.UpdateSongName)
        {
            var midiName = ServiceContainer.MidiFileService.ExtractSongNameFromMidi(newFilePath);
            var metadata = SongMetadataExtractor.Extract(midiName, Plugin.Config.ExtractionRules);
            _editState.EditName = metadata.SongName ?? midiName ?? Path.GetFileNameWithoutExtension(newFilePath);
        }

        var duration = await ServiceContainer.MidiFileService.CalculateDurationFromFileAsync(newFilePath);
        _editState.EditDuration = duration.ToString(@"mm\:ss");
    }

    private async Task SaveAsync()
    {
        try
        {
            var songService = ServiceContainer.SongService;

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
                song.Comments = _editState.EditComments;
                song.Tags = _editState.SongTags;
                song.UpdatedAt = DateTime.UtcNow;

                await songService.UpdateAsync(song);

                // Sync file metadata (HasValidFilePath, FileLastModifiedAt, Duration) from disk
                await Plugin.PlaylistManager.SyncSongFileDataAsync(song);

                DalamudApi.PluginLog.Information($"[SongEditWindow] Saved changes for song {_songId}");
            }

            Plugin.Ui.RefreshOpenWindows();

            this.IsOpen = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongEditWindow] Error saving changes");
        }
    }
}
