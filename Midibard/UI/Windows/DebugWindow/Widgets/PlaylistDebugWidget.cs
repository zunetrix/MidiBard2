using System;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;

namespace MidiBard;

public sealed class PlaylistDebugWidget : Widget
{
    public override string Title => "Playlist";

    private int _playlistCount = 3;
    private int _songsPerPlaylist = 5;
    private string _statusMessage = string.Empty;

    public PlaylistDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        ImGui.Text("Seed Database");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150);
        ImGui.InputInt("Playlists##SeedPlaylistCount", ref _playlistCount);
        if (_playlistCount < 1) _playlistCount = 1;
        if (_playlistCount > 100) _playlistCount = 100;

        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 150);
        ImGui.InputInt("Songs per Playlist##SeedSongsPerPlaylist", ref _songsPerPlaylist);
        if (_songsPerPlaylist < 0) _songsPerPlaylist = 0;
        if (_songsPerPlaylist > 500) _songsPerPlaylist = 500;

        ImGui.Spacing();

        if (ImGui.Button("Create Playlists##SeedCreate"))
            _ = CreateFakeDataAsync(_playlistCount, _songsPerPlaylist);

        ImGui.SameLine();

        if (ImGui.Button("Reset Database##SeedReset"))
        {
            if (ImGui.GetIO().KeyCtrl)
                _ = ResetDatabaseAsync();
        }
        ImGuiUtil.ToolTip("Hold Ctrl and click to reset.");

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.Spacing();
            ImGui.TextColored(Style.Colors.GrassGreen, _statusMessage);
        }
    }

    private async Task CreateFakeDataAsync(int playlistCount, int songsPerPlaylist)
    {
        _statusMessage = "Creating...";

        var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
        var playlistRepo = ServiceContainer.GetServiceOrNull<IPlaylistRepository>();
        var playlistService = ServiceContainer.GetServiceOrNull<IPlaylistService>();

        if (songRepo == null || playlistRepo == null || playlistService == null)
        {
            _statusMessage = "Services not available.";
            return;
        }

        for (int i = 1; i <= playlistCount; i++)
        {
            var playlist = await playlistService.CreateAsync($"Playlist {i}");
            if (playlist == null) continue;

            for (int j = 1; j <= songsPerPlaylist; j++)
            {
                var filePath = $@"C:\fake\playlist_{i}\song_{j}.mid";
                var song = await songRepo.CreateOrGetSongAsync(
                    filePath, $"Song {j}", $"Artist {i}", 2000 + i, TimeSpan.FromSeconds(60 + j * 10));
                await playlistRepo.AddSongToPlaylistAsync(playlist.Id, song.Id, -1);
            }
        }

        _statusMessage = $"Created {playlistCount} playlist(s) with {songsPerPlaylist} song(s) each.";
        RefreshWindows();
    }

    private async Task ResetDatabaseAsync()
    {
        _statusMessage = "Resetting...";

        var songRepo = ServiceContainer.GetServiceOrNull<ISongRepository>();
        var playlistService = ServiceContainer.GetServiceOrNull<IPlaylistService>();

        if (songRepo == null || playlistService == null)
        {
            _statusMessage = "Services not available.";
            return;
        }

        var playlists = await playlistService.GetAllAsync();
        foreach (var p in playlists)
            await playlistService.DeleteAsync(p.Id);

        await songRepo.DeleteAllAsync();

        _statusMessage = "Database reset.";
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        var plugin = Context.Plugin;
        if (plugin.Ui.PlaylistWindow.IsOpen)
            _ = plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();
        if (plugin.Ui.SongsWindow.IsOpen)
            _ = plugin.Ui.SongsWindow.LoadSongsAsync();
    }
}

