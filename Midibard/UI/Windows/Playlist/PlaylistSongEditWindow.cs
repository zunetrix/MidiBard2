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
/// Window for editing a PlaylistSong and its related Song within a specific playlist context.
/// Edits composite: Song metadata (global) + PlaylistSong state (playlist-scoped).
/// </summary>
public class PlaylistSongEditWindow : Window
{
    private Plugin Plugin { get; }
    private readonly SongImportHelper _importHelper;

    // IDs for the entities being edited
    private int _playlistId = -1;
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

        // PlaylistSong fields (playlist-scoped)
        public bool EditIsPlayed = false;
        public string EditAddedAt = string.Empty;

        // Tag management
        public List<Tag> AvailableTags = new();
        public List<Tag> SongTags = new();
        public int SelectedTagIndex = 0;
    }

    private EditState _editState = new();

    public PlaylistSongEditWindow(Plugin plugin)
        : base($"{Plugin.Name} Edit Playlist Song###PlaylistSonEditgWindow")
    {
        Plugin = plugin;
        _importHelper = new SongImportHelper(plugin);

        Size = ImGuiHelpers.ScaledVector2(600, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>
    /// Initialize editing for a specific PlaylistSong within a playlist.
    /// Call this to open the window with data loaded.
    /// </summary>
    public void EditPlaylistSong(int playlistId, int songId)
    {
        ResetState();

        _playlistId = playlistId;
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
            var playlistService = ServiceContainer.PlaylistService;

            var song = await songService.GetByIdAsync(_songId);
            var playlist = await playlistService.GetByIdAsync(_playlistId);

            if (song == null || playlist == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistSonEditgWindow] Failed to load song or playlist");
                return;
            }

            // Find the PlaylistSong within the playlist
            var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == _songId);
            if (playlistSong == null)
            {
                DalamudApi.PluginLog.Warning("[PlaylistSonEditgWindow] Song not found in playlist");
                return;
            }

            // Populate edit state - Song fields
            _editState.EditName = song.Name ?? "";
            _editState.EditArtist = song.Artist ?? "";
            _editState.EditReleaseYear = song.ReleaseYear;
            _editState.EditRating = song.Rating;
            _editState.EditPlayCount = song.PlayCount;
            _editState.EditFilePath = song.FilePath ?? "";
            _editState.EditDuration = song.Duration.ToString(@"mm\:ss");
            _editState.EditComments = song.Comments ?? "";

            // Populate edit state - PlaylistSong fields
            _editState.EditIsPlayed = playlistSong.IsPlayed;
            _editState.EditAddedAt = playlistSong.AddedAt.ToString("g");

            // Split tags into assigned and available
            var allTags = await ServiceContainer.TagService.GetAllAsync();
            var assignedTagIds = song.Tags.Select(t => t.Id).ToHashSet();
            _editState.SongTags = song.Tags.ToList();
            _editState.AvailableTags = allTags.Where(t => !assignedTagIds.Contains(t.Id)).ToList();

            _isLoading = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistSonEditgWindow] Error loading data");
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
        _playlistId = -1;
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
        ImGui.InputText("##EditPlaylistSongName", ref _editState.EditName, 256);

        ImGui.Text("Artist:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##EditPlaylistSongArtist", ref _editState.EditArtist, 256);

        ImGui.Text("Release Year:");
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##EditPlaylistSongYear", ref _editState.EditReleaseYear, 1, 1, flags: ImGuiInputTextFlags.AutoSelectAll))
        {
            if (_editState.EditReleaseYear < 0)
                _editState.EditReleaseYear = 0;
        }

        ImGui.Text("Play Count:");
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("##EditPlaylistSongPlayCount", ref _editState.EditPlayCount, 1, 1, flags: ImGuiInputTextFlags.AutoSelectAll))
        {
            if (_editState.EditPlayCount < 0)
                _editState.EditPlayCount = 0;
        }

        ImGui.Text("Rating:");
        ImGui.SliderInt("##EditPlaylistSongRating", ref _editState.EditRating, 0, 5);

        ImGuiUtil.TextIcon(FontAwesomeIcon.Clock);
        ImGui.SameLine();
        ImGui.Text($"Duration: {_editState.EditDuration}");

        ImGui.Text("File Path:");
        ImGui.SameLine();
        ImGuiUtil.HelpMarker("Selecting a new file recalculates the duration and updates the path.\nIf 'Update Song Name' is checked, the song name will be overwritten from the new file.");

        if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.FolderOpen, "##ChangePlaylistSongFilePath", "Change File Path"))
            _ = ChangeFilePathAsync();
        ImGui.SameLine();
        ImGui.Checkbox("Update Song Name##PLUpdateSongName", ref _editState.UpdateSongName);

        ImGui.Spacing();
        ImGui.TextWrapped(_editState.EditFilePath);
        ImGui.Spacing();

        if (!string.IsNullOrEmpty(_editState.FilePathError))
            ImGui.TextColored(Style.Colors.Red, _editState.FilePathError);

        ImGui.Spacing();

        ImGui.Text("Comments:");
        ImGui.InputTextMultiline("##EditPlaylistSongComments", ref _editState.EditComments, 1024, ImGuiHelpers.ScaledVector2(-1, 80));

        ImGui.Separator();

        ImGui.Text("Playlist-Scoped Fields:");

        ImGui.Checkbox("##EditPlaylistSongIsPlayed", ref _editState.EditIsPlayed);
        ImGui.SameLine();
        ImGui.Text("Is Played");

        ImGui.Text("Added to Playlist:");
        ImGui.TextWrapped(_editState.EditAddedAt);

        ImGui.Separator();

        ImGui.Text("Tags:");

        if (_editState.AvailableTags.Count > 0)
        {
            var tagNames = _editState.AvailableTags.Select(t => t.Name).ToArray();
            if (ImGui.Combo("##EditPlaylistSongAddTagCombo", ref _editState.SelectedTagIndex, tagNames, 10))
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

        using (ImRaii.Child("##PLTagsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 150), false))
        {
            if (_editState.SongTags.Count > 0)
            {
                foreach (var tag in _editState.SongTags.ToList())
                {
                    if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, $"##RemoverSongTag_{tag.Id}", "Remove"))
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

        if (ImGuiUtil.SuccessButton("Save##EditPlaylistSongSave", ImGuiHelpers.ScaledVector2(100, 0)))
            _ = SaveAsync();

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##EditPlaylistSongCancel", ImGuiHelpers.ScaledVector2(100, 0)))
            this.IsOpen = false;
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
            var playlistService = ServiceContainer.PlaylistService;

            // Update Song metadata via service
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

                await songService.UpdateAsync(song);

                // Sync file metadata (FilePath, FileLastModifiedAt, Duration) from disk
                await Plugin.PlaylistManager.SyncSongFileDataAsync(song);
            }

            // Update PlaylistSong state via playlist service
            var playlist = await playlistService.GetByIdAsync(_playlistId);
            if (playlist != null)
            {
                var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == _songId);
                if (playlistSong != null)
                {
                    playlistSong.IsPlayed = _editState.EditIsPlayed;
                }

                playlist.UpdatedAt = DateTime.UtcNow;
                await playlistService.UpdateAsync(playlist);
            }

            DalamudApi.PluginLog.Information($"[PlaylistSonEditgWindow] Saved changes for song {_songId}");

            Plugin.Ui.RefreshOpenWindows();

            this.IsOpen = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistSonEditgWindow] Error saving changes");
        }
    }
}
