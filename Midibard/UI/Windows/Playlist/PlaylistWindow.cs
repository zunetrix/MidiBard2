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
using MidiBard.Extensions.DryWetMidi;

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
        if (Plugin.PlaylistManager?.PlaylistRepository == null) return;

        _isLoading = true;
        try
        {
            _playlists = await Plugin.PlaylistManager.PlaylistRepository.GetAllAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadPlaylistSongsAsync(int playlistId)
    {
        if (Plugin.PlaylistManager?.PlaylistRepository == null || Plugin.PlaylistManager?.SongRepository == null) return;

        _isLoading = true;
        try
        {
            var playlist = await Plugin.PlaylistManager.PlaylistRepository.GetByIdAsync(playlistId);
            if (playlist != null)
            {
                _playlistSongs = new List<SongModel>();
                foreach (var ps in playlist.Songs.OrderBy(s => s.Order))
                {
                    var song = await Plugin.PlaylistManager.SongRepository.GetSongByIdAsync(ps.SongId);
                    if (song != null)
                    {
                        _playlistSongs.Add(song);
                    }
                }
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    public override void Draw()
    {
        ImGui.BeginChild("PlaylistTabs", new Vector2(200, -1), true);

        if (ImGui.Button("+ Nova Playlist"))
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
            if (ImGui.Button("Adicionar músicas de pasta..."))
            {
                ImGui.OpenPopup("AddSongsFromFolder");
            }

            ImGui.SameLine();

            if (ImGui.Button("Atualizar"))
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
            ImGui.Text("Editar música selecionada:");

            // TODO: Add song editing fields (Name, Artist, ReleaseYear, Tags, Rate)
        }
        else
        {
            ImGui.Text("Selecione uma playlist para ver os detalhes");
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
            ImGui.Text("Nova Playlist");
            ImGui.InputText("Nome", ref _newPlaylistName, 100);

            if (ImGui.Button("Criar"))
            {
                if (!string.IsNullOrWhiteSpace(_newPlaylistName))
                {
                    _ = CreatePlaylistAsync(_newPlaylistName);
                    _newPlaylistName = "";
                }
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancelar"))
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
            ImGui.Text("Adicionar músicas de pasta");
            ImGui.InputText("Caminho", ref _searchFolder, 500);

            if (ImGui.Button("Procurar..."))
            {
                // TODO: Open folder browser dialog
            }

            ImGui.SameLine();

            if (ImGui.Button("Carregar"))
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
        if (Plugin.PlaylistManager?.PlaylistRepository == null) return;

        var playlist = new PlaylistModel { Name = name };
        await Plugin.PlaylistManager.PlaylistRepository.CreateAsync(playlist);
        await LoadPlaylistsAsync();
    }

    private async Task LoadSongsFromFolderAsync(string folderPath)
    {
        if (Plugin.PlaylistManager?.SongRepository == null || _selectedPlaylist == null) return;

        var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories))
            .ToList();

        foreach (var filePath in midiFiles)
        {
            try
            {
                // Load file to get duration
                var duration = TimeSpan.Zero;
                var midiFile = Plugin.PlaylistManager.LoadSongFile(filePath);
                if (midiFile != null)
                {
                    duration = midiFile.GetDurationTimeSpan() ?? TimeSpan.Zero;
                }

                // Create or get song in database
                var song = await Plugin.PlaylistManager.SongRepository.CreateOrGetSongAsync(
                    filePath,
                    Path.GetFileNameWithoutExtension(filePath),
                    "", // Artist
                    0,  // ReleaseYear
                    duration
                );

                // Add to playlist
                var order = _selectedPlaylist.Songs.Count;
                await Plugin.PlaylistManager.PlaylistRepository.AddSongToPlaylistAsync(_selectedPlaylist.Id, song.Id, order);
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Warning(ex, $"Error adding song: {filePath}");
            }
        }

        await LoadPlaylistSongsAsync(_selectedPlaylist.Id);
    }
}
