using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;

namespace MidiBard;

/// <summary>
/// Window for editing a PlaylistSong and its related Song within a specific playlist context.
/// Edits composite: Song metadata (global) + PlaylistSong state (playlist-scoped).
/// </summary>
public class EditPlaylistSongWindow : Window
{
    private Plugin Plugin { get; }

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

        // PlaylistSong fields (playlist-scoped)
        public bool EditIsPlayed = false;
        public string EditAddedAt = string.Empty;

        // Tag management
        public List<Tag> AvailableTags = new();
        public int SelectedTagIndex = -1;
    }

    private EditState _editState = new();

    public EditPlaylistSongWindow(Plugin plugin)
        : base($"{Plugin.Name} Edit Song###EditPlaylistSongWindow")
    {
        Plugin = plugin;

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
            var songService = ServiceContainer.GetServiceOrNull<ISongService>();
            var playlistService = ServiceContainer.GetServiceOrNull<IPlaylistService>();
            var tagRepo = ServiceContainer.GetServiceOrNull<ITagRepository>();

            if (songService == null || playlistService == null)
                return;

            // Load song and playlist via services
            var song = await songService.GetByIdAsync(_songId);
            var playlist = await playlistService.GetByIdAsync(_playlistId);

            if (song == null || playlist == null)
            {
                DalamudApi.PluginLog.Warning("[EditPlaylistSongWindow] Failed to load song or playlist");
                return;
            }

            // Find the PlaylistSong within the playlist
            var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == _songId);
            if (playlistSong == null)
            {
                DalamudApi.PluginLog.Warning("[EditPlaylistSongWindow] Song not found in playlist");
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

            // Populate edit state - PlaylistSong fields
            _editState.EditIsPlayed = playlistSong.IsPlayed;
            _editState.EditAddedAt = playlistSong.AddedAt.ToString("g");

            // Load available tags
            if (tagRepo != null)
            {
                _editState.AvailableTags = await tagRepo.GetAllAsync();
            }

            _isLoading = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[EditPlaylistSongWindow] Error loading data");
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
        ImGui.InputText("##EditPlaylistSongName", ref _editState.EditName, 256);

        ImGui.Text("Artist:");
        ImGui.InputText("##EditPlaylistSongArtist", ref _editState.EditArtist, 256);

        ImGui.Text("Release Year:");
        ImGui.InputInt("##EditPlaylistSongYear", ref _editState.EditReleaseYear);

        ImGui.Text("Rating:");
        ImGui.SliderInt("##EditPlaylistSongRating", ref _editState.EditRating, 0, 5);

        ImGui.Text("Play Count:");
        ImGui.InputInt("##EditPlaylistSongPlayCount", ref _editState.EditPlayCount);

        ImGui.Text($"Duration: {_editState.EditDuration}");

        ImGui.Text("File Path:");
        ImGui.TextWrapped(_editState.EditFilePath);

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
            ImGui.Combo("##EditPlaylistSongTagSelect", ref _editState.SelectedTagIndex, tagNames, tagNames.Length);
        }

        ImGui.Separator();

        if (ImGui.Button("Save##EditPlaylistSongSave", ImGuiHelpers.ScaledVector2(100, 0)))
        {
            _ = SaveAsync();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel##EditPlaylistSongCancel", ImGuiHelpers.ScaledVector2(100, 0)))
        {
            this.IsOpen = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var songService = ServiceContainer.GetServiceOrNull<ISongService>();
            var playlistService = ServiceContainer.GetServiceOrNull<IPlaylistService>();

            if (songService == null || playlistService == null)
            {
                DalamudApi.PluginLog.Error("[EditPlaylistSongWindow] Services not available");
                return;
            }

            // Update Song metadata via service
            var song = await songService.GetByIdAsync(_songId);
            if (song != null)
            {
                song.Name = _editState.EditName;
                song.Artist = _editState.EditArtist;
                song.ReleaseYear = _editState.EditReleaseYear;
                song.Rating = _editState.EditRating;
                song.PlayCount = _editState.EditPlayCount;

                await songService.UpdateAsync(song);
            }

            // Update PlaylistSong state via playlist service
            var playlist = await playlistService.GetByIdAsync(_playlistId);
            if (playlist != null)
            {
                var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song?.Id == _songId);
                if (playlistSong != null)
                {
                    playlistSong.IsPlayed = _editState.EditIsPlayed;
                    // Note: AddedAt is typically immutable, but leaving editable if needed
                }

                playlist.UpdatedAt = DateTime.UtcNow;
                await playlistService.UpdateAsync(playlist);
            }

            DalamudApi.PluginLog.Information("[EditPlaylistSongWindow] Saved changes for song {SongId}", _songId);

            this.IsOpen = false;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[EditPlaylistSongWindow] Error saving changes");
        }
    }
}
