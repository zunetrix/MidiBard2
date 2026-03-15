using System;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Playlist;
using MidiBard.Resources;

namespace MidiBard;

public sealed class DatabaseDebugWidget : Widget
{
    public override string Title => "Database";

    private int _playlistCount = 3;
    private int _songsPerPlaylist = 5;
    private string _statusMessage = string.Empty;

    public DatabaseDebugWidget(WidgetContext ctx) : base(ctx)
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
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.Spacing();
        ImGui.Spacing();

        // == Reset Table ==
        ImGui.Text("Reset Table");
        ImGui.Separator();
        ImGui.TextDisabled("Deletes all rows from the chosen table.");
        ImGui.Spacing();

        if (ImGui.Button("Songs##ResetTableSongs"))
        {
            if (ImGui.GetIO().KeyCtrl)
                _ = ResetTableAsync("songs");
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();

        if (ImGui.Button("Playlists##ResetTablePlaylists"))
        {
            if (ImGui.GetIO().KeyCtrl)
                _ = ResetTableAsync("playlists");
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();

        if (ImGui.Button("Tags##ResetTableTags"))
        {
            if (ImGui.GetIO().KeyCtrl)
                _ = ResetTableAsync("tags");
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.Spacing();
        ImGui.Spacing();

        // == Reset Auto-Increment ==
        ImGui.Text("Reset Auto-Increment");
        ImGui.Separator();
        ImGui.TextDisabled("Resets the next ID to 1 for the chosen table.");
        ImGui.Spacing();

        if (ImGui.Button("Songs##ResetSeqSongs"))
        {
            if (ImGui.GetIO().KeyCtrl)
                ResetSequence("songs");
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();

        if (ImGui.Button("Playlists##ResetSeqPlaylists"))
        {
            if (ImGui.GetIO().KeyCtrl)
                ResetSequence("playlists");
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();

        if (ImGui.Button("Tags##ResetSeqTags"))
        {
            if (ImGui.GetIO().KeyCtrl)
                ResetSequence("tags");
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

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

    private async Task ResetTableAsync(string collectionName)
    {
        _statusMessage = $"Clearing '{collectionName}'...";
        await Task.Run(() => ServiceContainer.DbContext?.ResetCollection(collectionName));
        _statusMessage = $"Table '{collectionName}' cleared.";
        RefreshWindows();
    }

    private void ResetSequence(string collectionName)
    {
        ServiceContainer.DbContext?.ResetSequence(collectionName);
        _statusMessage = $"Auto-increment for '{collectionName}' reset.";
    }

    private void RefreshWindows()
    {
        Context.Plugin.Ui.RefreshOpenWindows();
    }
}
