using System;
using System.Linq;
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

        var songRepo = ServiceContainer.SongRepository;
        var playlistRepo = ServiceContainer.PlaylistRepository;
        var playlistService = ServiceContainer.PlaylistService;

        await Task.Run(async () =>
        {
            for (int i = 1; i <= playlistCount; i++)
            {
                var playlist = await playlistService.CreateAsync($"Playlist {i}");
                if (playlist == null) continue;

                if (songsPerPlaylist > 0)
                {
                    var songs = Enumerable.Range(1, songsPerPlaylist).Select(j => new Song
                    {
                        FilePath = $@"C:\fake\playlist_{i}\song_{j}.mid",
                        Name = $"Song {j}",
                        Artist = $"Artist {i}",
                        ReleaseYear = 2000 + i,
                        Duration = TimeSpan.FromSeconds(60 + j * 10),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    });

                    var inserted = await songRepo.BulkInsertSongsAsync(songs);
                    await playlistRepo.BulkAddSongsToPlaylistAsync(playlist.Id, inserted.Select(s => s.Id));
                }
            }
        });

        _statusMessage = $"Created {playlistCount} playlist(s) with {songsPerPlaylist} song(s) each.";
        RefreshWindows();
    }

    private async Task ResetDatabaseAsync()
    {
        _statusMessage = "Resetting...";

        var songRepo = ServiceContainer.SongRepository;
        var playlistService = ServiceContainer.PlaylistService;

        await Task.Run(async () =>
        {
            var playlists = await playlistService.GetAllAsync();
            foreach (var p in playlists)
                await playlistService.DeleteAsync(p.Id);

            await songRepo.DeleteAllAsync();
        });

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

