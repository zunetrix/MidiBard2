using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Playlist;
using MidiBard.Playlist.Services;
using MidiBard.Resources;

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
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(320, 300),
        };
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

        ImGui.Text(Language.playlist_export_fields_include);
        ImGuiHelpers.ScaledDummy(0, 2);

        // Two-column checkbox layout
        float colWidth = ImGuiHelpers.GlobalScale * 175;
        if (ImGui.BeginTable("##ExportFieldsTable", 2, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("##col1", ImGuiTableColumnFlags.WidthFixed, colWidth);
            ImGui.TableSetupColumn("##col2", ImGuiTableColumnFlags.WidthStretch);

            if (_isPlaylistMode)
            {
                DrawCheckboxRow(Language.playlist_export_field_playlist_name, ref _options.IncludePlaylistName, Language.playlist_export_field_is_played, ref _options.IncludeIsPlayed);
            }

            DrawCheckboxRow(Language.common_label_name, ref _options.IncludeName, Language.common_label_artist, ref _options.IncludeArtist);
            DrawCheckboxRow(Language.common_label_duration, ref _options.IncludeDuration, Language.common_label_file_path, ref _options.IncludeFilePath);
            DrawCheckboxRow(Language.common_label_tags, ref _options.IncludeTags, Language.common_label_comments, ref _options.IncludeComments);
            DrawCheckboxRow(Language.playlist_export_field_release_year, ref _options.IncludeReleaseYear, Language.common_label_rating, ref _options.IncludeRating);
            DrawCheckboxRow(Language.playlist_export_field_last_played, ref _options.IncludeLastPlayedAt, Language.playlist_export_field_file_modified, ref _options.IncludeFileLastModifiedAt);

            ImGui.EndTable();
        }

        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);

        // Export buttons
        float btnWidth = ImGuiHelpers.GlobalScale * 140;
        if (ImGui.Button($"{Language.playlist_export_csv}##ExportCsvBtn", ImGuiHelpers.ScaledVector2(btnWidth, 0)))
            OpenSaveDialog(".csv");

        ImGui.SameLine();
        if (ImGui.Button($"{Language.playlist_export_json}##ExportJsonBtn", ImGuiHelpers.ScaledVector2(btnWidth, 0)))
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
            isJson ? Language.playlist_export_title_json : Language.playlist_export_title_csv,
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
                    _messageDisplay.Show(string.Format(Language.playlist_export_success, Path.GetFileName(path)));
                else
                    _messageDisplay.Show(Language.playlist_export_failed);
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
