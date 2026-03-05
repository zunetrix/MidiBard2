using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;

namespace MidiBard;

public class ExportWindow : Window
{
    private Plugin Plugin { get; }

    // Export mode
    private bool _isPlaylistMode;
    private string _playlistName = string.Empty;
    private List<Song> _songs = new();
    private Dictionary<int, PlaylistSong> _songLookup = new();

    // Options (persisted across opens so user keeps their selection)
    private readonly ExportOptions _options = new();

    // Feedback
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    public ExportWindow(Plugin plugin) : base($"{Plugin.Name} Export###ExportWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(400, 370);
        SizeCondition = ImGuiCond.Always;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;
    }

    /// <summary>Open the window for exporting a flat list of songs.</summary>
    public void OpenForSongs(List<Song> songs)
    {
        _isPlaylistMode = false;
        _playlistName = string.Empty;
        _songs = songs ?? new List<Song>();
        _songLookup = new Dictionary<int, PlaylistSong>();
        IsOpen = true;
    }

    /// <summary>Open the window for exporting songs from a playlist context.</summary>
    public void OpenForPlaylist(string playlistName, List<PlaylistSong> songs)
    {
        _isPlaylistMode = true;
        _playlistName = playlistName ?? string.Empty;
        _songs = songs?.Where(ps => ps.Song != null).Select(ps => ps.Song!).ToList() ?? new();
        _songLookup = songs?.Where(ps => ps.Song?.Id > 0).ToDictionary(ps => ps.Song!.Id, ps => ps) ?? new();
        IsOpen = true;
    }

    public override void PreDraw()
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(320, 300),
        };
        base.PreDraw();
    }

    public override void Draw()
    {
        _messageDisplay.Draw();

        // Context info
        if (_isPlaylistMode)
            ImGui.Text($"Playlist: {_playlistName}  ({_songs.Count} songs)");
        else
            ImGui.Text($"{_songs.Count} songs");

        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 2);

        ImGui.Text("Fields to include:");
        ImGuiHelpers.ScaledDummy(0, 2);

        // Two-column checkbox layout
        float colWidth = ImGuiHelpers.GlobalScale * 175;
        if (ImGui.BeginTable("##ExportFieldsTable", 2, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("##col1", ImGuiTableColumnFlags.WidthFixed, colWidth);
            ImGui.TableSetupColumn("##col2", ImGuiTableColumnFlags.WidthStretch);

            if (_isPlaylistMode)
            {
                DrawCheckboxRow("Playlist Name", ref _options.IncludePlaylistName, "Is Played", ref _options.IncludeIsPlayed);
            }

            DrawCheckboxRow("Song Name", ref _options.IncludeName, "Artist", ref _options.IncludeArtist);
            DrawCheckboxRow("Duration", ref _options.IncludeDuration, "File Path", ref _options.IncludeFilePath);
            DrawCheckboxRow("Tags", ref _options.IncludeTags, "Comments", ref _options.IncludeComments);
            DrawCheckboxRow("Release Year", ref _options.IncludeReleaseYear, "Rating", ref _options.IncludeRating);
            DrawCheckboxRow("Last Played", ref _options.IncludeLastPlayedAt, "File Modified", ref _options.IncludeFileLastModifiedAt);

            ImGui.EndTable();
        }

        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);

        // Export buttons
        float btnWidth = ImGuiHelpers.GlobalScale * 140;
        if (ImGui.Button("Export CSV##ExportCsvBtn", ImGuiHelpers.ScaledVector2(btnWidth, 0)))
            OpenSaveDialog(".csv");

        ImGui.SameLine();
        if (ImGui.Button("Export JSON##ExportJsonBtn", ImGuiHelpers.ScaledVector2(btnWidth, 0)))
            OpenSaveDialog(".json");
    }

    private static void DrawCheckboxRow(string label1, ref bool value1, string label2, ref bool value2)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Checkbox(label1, ref value1);
        ImGui.TableNextColumn();
        ImGui.Checkbox(label2, ref value2);
    }

    private void OpenSaveDialog(string extension)
    {
        var isJson = extension == ".json";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var baseName = _isPlaylistMode
            ? SanitizeFileName(_playlistName) + $"_export_{timestamp}"
            : $"songs_export_{timestamp}";

        var defaultFolder = string.IsNullOrEmpty(Plugin.Config.defaultPlaylistFolder)
            ? DalamudApi.PluginInterface.ConfigDirectory.FullName
            : Plugin.Config.defaultPlaylistFolder;

        Plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(
            isJson ? "Export to JSON" : "Export to CSV",
            extension,
            baseName + extension,
            extension,
            async (result, path) =>
            {
                if (!result || string.IsNullOrWhiteSpace(path)) return;

                bool success;
                if (_isPlaylistMode)
                {
                    success = isJson
                        ? await ServiceContainer.PlaylistExportService.ExportPlaylistSongsToJsonAsync(_playlistName, _songs, _songLookup, path, _options)
                        : await ServiceContainer.PlaylistExportService.ExportPlaylistSongsToCsvAsync(_playlistName, _songs, _songLookup, path, _options);
                }
                else
                {
                    success = isJson
                        ? await ServiceContainer.PlaylistExportService.ExportSongsToJsonAsync(_songs, path, _options)
                        : await ServiceContainer.PlaylistExportService.ExportSongsToCsvAsync(_songs, path, _options);
                }

                if (success)
                    _messageDisplay.Show($"Exported to {Path.GetFileName(path)}");
                else
                    _messageDisplay.Show("Export failed. Check log for details.");
            },
            defaultFolder);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "playlist";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
