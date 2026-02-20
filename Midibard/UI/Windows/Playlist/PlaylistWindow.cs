using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

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
    private string _newPlaylistName = "";
    private string _searchFolder = "";
    private List<string> _pendingFiles = new();
    private bool _isLoading;

    public PlaylistWindow(Plugin plugin) : base($"{Plugin.Name} {Language.PlaylistTitle}###PlaylistWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(800, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        base.PreDraw();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _ = LoadPlaylistsAsync();
    }

    public override void OnClose()
    {
        base.OnClose();
    }

    private async Task LoadPlaylistsAsync()
    {
        if (Plugin.PlaylistManager == null) return;

        _isLoading = true;
        try
        {
            _playlists = await Plugin.PlaylistManager.GetAllPlaylistsAsync();
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
        }
        finally
        {
            _isLoading = false;
        }
    }

    public override void Draw()
    {
        ImGui.BeginChild("PlaylistTabs", new Vector2(200, -1), true);

        if (ImGui.Button("+ New Playlist"))
        {
            ImGui.OpenPopup("NewPlaylistPopup");
        }

        ImGui.Separator();

        // Playlist list
        foreach (var playlist in _playlists)
        {
            if (ImGui.Selectable(playlist.Name, _selectedPlaylist?.Id == playlist.Id))
            {
                _selectedPlaylist = playlist;
                _ = LoadPlaylistSongsAsync(playlist.Id);
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("PlaylistDetails", new Vector2(-1, -1), true);

        if (_selectedPlaylist != null)
        {
            ImGui.Text($"Playlist: {_selectedPlaylist.Name}");
            ImGui.Separator();

            // Add songs from folder
            if (ImGui.Button("Add song from folder..."))
            {
                ImGui.OpenPopup("AddSongsFromFolder");
            }

            ImGui.SameLine();

            if (ImGui.Button("Update"))
            {
                _ = LoadPlaylistSongsAsync(_selectedPlaylist.Id);
            }

            ImGui.Separator();

            // Song list
            ImGui.BeginChild("SongList", new Vector2(-1, -200), true);

            for (int i = 0; i < _playlistSongs.Count; i++)
            {
                var song = _playlistSongs[i];
                ImGui.Text($"{i + 1}. {song.Name} - {song.Artist}");
                ImGui.SameLine();
                ImGui.Text($"({song.Duration})");
            }

            ImGui.EndChild();

            // Edit selected song
            ImGui.Separator();
            ImGui.Text("Edit selected song:");

            // TODO: Add song editing fields (Name, Artist, ReleaseYear, Tags, Rate)
        }
        else
        {
            ImGui.Text("Select playlist for details");
        }

        ImGui.EndChild();

        // Popups
        DrawNewPlaylistPopup();
        DrawAddSongsPopup();
    }

    private void DrawNewPlaylistPopup()
    {
        if (ImGui.BeginPopup("NewPlaylistPopup"))
        {
            ImGui.Text("New  Playlist");
            ImGui.InputText("Nome", ref _newPlaylistName, 100);

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

    private void DrawAddSongsPopup()
    {
        if (ImGui.BeginPopup("AddSongsFromFolder"))
        {
            ImGui.Text("Add song from folder");
            ImGui.InputText("Path", ref _searchFolder, 500);

            if (ImGui.Button("Select..."))
            {
                // TODO: Open folder browser dialog
            }

            ImGui.SameLine();

            if (ImGui.Button("Load"))
            {
                if (!string.IsNullOrWhiteSpace(_searchFolder) && Directory.Exists(_searchFolder))
                {
                    _ = LoadSongsFromFolderAsync(_searchFolder);
                }
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

    private async Task LoadSongsFromFolderAsync(string folderPath)
    {
        if (Plugin.PlaylistManager == null || _selectedPlaylist == null) return;

        await Plugin.PlaylistManager.AddSongsFromFolderAsync(_selectedPlaylist.Id, folderPath);
        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }
}
