using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Resources;

namespace MidiBard;

/// <summary>
/// Window for editing a Song in the global song library (independent of any playlist context).
/// </summary>
public class EditSongWindow : Window
{
    private Plugin Plugin { get; }
    private PluginUi Ui { get; }

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
        public List<Tag> AvailableTags = new();
        public int SelectedTagIndex = -1;
    }

    private EditState _editState = new();

    public EditSongWindow(Plugin plugin, PluginUi ui)
        : base($"{Plugin.Name} Edit Song###EditSongWindow")
    {
        Plugin = plugin;
        Ui = ui;

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
                DalamudApi.PluginLog.Warning("[EditSongWindow] Song not found: {SongId}", _songId);
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

            // Load available tags
            if (tagRepo != null)
            {
                _editState.AvailableTags = await tagRepo.GetAllAsync();
            }

            _isLoading = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[EditSongWindow] Error loading song data");
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
        ImGui.InputText("##EditSongName", ref _editState.EditName, 256);

        ImGui.Text("Artist:");
        ImGui.InputText("##EditSongArtist", ref _editState.EditArtist, 256);

        ImGui.Text("Release Year:");
        ImGui.InputInt("##EditSongYear", ref _editState.EditReleaseYear);

        ImGui.Text("Rating:");
        ImGui.SliderInt("##EditSongRating", ref _editState.EditRating, 0, 5);

        ImGui.Text("Play Count:");
        ImGui.InputInt("##EditSongPlayCount", ref _editState.EditPlayCount);

        ImGui.Text($"Duration: {_editState.EditDuration}");

        ImGui.Text("File Path:");
        ImGui.TextWrapped(_editState.EditFilePath);

        ImGui.Separator();

        ImGui.Text("Tags:");
        if (_editState.AvailableTags.Count > 0)
        {
            var tagNames = _editState.AvailableTags.Select(t => t.Name).ToArray();
            ImGui.Combo("##EditSongTagSelect", ref _editState.SelectedTagIndex, tagNames, tagNames.Length);
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

    private async Task SaveAsync()
    {
        try
        {
            var songService = ServiceContainer.GetServiceOrNull<ISongService>();

            if (songService == null)
            {
                DalamudApi.PluginLog.Error("[EditSongWindow] Song service not available");
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
                song.UpdatedAt = DateTime.UtcNow;

                await songService.UpdateAsync(song);

                DalamudApi.PluginLog.Information("[EditSongWindow] Saved changes for song {SongId}", _songId);
            }

            this.IsOpen = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[EditSongWindow] Error saving changes");
        }
    }
}
