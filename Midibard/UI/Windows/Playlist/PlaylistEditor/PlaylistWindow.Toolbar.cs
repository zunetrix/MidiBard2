using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Resources;

namespace MidiBard;

public partial class PlaylistWindow
{
    private void DrawMenuButtons()
    {
        using (ImRaii.Group())
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##PlaylistImportFileBtn", Language.icon_button_tooltip_import_file, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFileTask();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##PlaylistImportFolderBtn", Language.icon_button_tooltip_import_folder, size: Style.Dimensions.ButtonLarge))
            {
                RunImportFolderTask();
            }


            ImGui.SameLine();
            using (ImRaii.Disabled(PlaylistSongs.Count == 0 || AgentManager.AgentMetronome.EnsembleModeRunning || Plugin.CurrentBardPlayback.IsRunning))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Upload, "##PlaylistLoadBtn", "Load Playlist To Playback", size: Style.Dimensions.ButtonLarge))
                {
                    if (_selectedPlaylist != null)
                    {
                        _ = LoadPlaylistToCurrentAsync(_selectedPlaylist.Id);
                    }
                }

                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##PlaylistCLear", "Clear (remove all songs)", size: Style.Dimensions.ButtonLarge))
                {
                    if (_selectedPlaylist != null)
                    {
                        ImGui.OpenPopup("ClearPlaylistPopup");
                    }
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Eraser, "##ResetPlaylistPlayedStatusBtn", Language.tooltip_reset_played_status, size: Style.Dimensions.ButtonLarge))
            {
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _ = ResetPlaylistSongsPlayedStatusAsync();
                }
            }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.FileImport, "##SongsImportSettingsBtn", "Import Rules\nDefine rules to extract info from file name", size: Style.Dimensions.ButtonLarge))
            // {
            //     Plugin.Ui.ExtractionRulesWindow.Toggle();
            // }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##PlaylistExportBtn", "Export", size: Style.Dimensions.ButtonLarge))
            // {
            //     if (_selectedPlaylist != null)
            //         Plugin.Ui.ExportWindow.OpenForPlaylist(_selectedPlaylist.Name, PlaylistSongs);
            // }

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButton(FontAwesomeIcon.Tags, "#TagsWindowBtn", "Tags", size: Style.Dimensions.ButtonLarge))
            // {
            //     Plugin.Ui.TagsWindow.Toggle();
            // }

            // ImGui.SameLine();
            // DrawViewColumnsButton(); // moved to menu bar

            ImGui.SameLine();
            using (ImRaii.Disabled(!HasActiveFiltersOrSort))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.FilterCircleXmark, "##PlaylistClearFiltersBtn", "Clear all filters and sorting", size: Style.Dimensions.ButtonLarge))
                    ClearFiltersAndSort();
            }

            if (DalamudApi.PartyList.IsPartyLeader())
            {
                ImGui.SameLine();
                DrawEnsembleButton();
            }

            DrawSongCounter();
        }

        ImGui.SameLine();
    }

    private void DrawViewColumnsButton()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Columns, "##PlaylistViewColumnsBtn", "Show/Hide Columns", size: Style.Dimensions.ButtonLarge))
            ImGui.OpenPopup("PlaylistColumnsPopup");
    }

    private void DrawEnsembleButton()
    {
        if (!AgentManager.AgentMetronome.EnsembleModeRunning)
        {
            using var _ = ImRaii.Disabled(!Plugin.CurrentBardPlayback.IsLoaded || Plugin.CurrentBardPlayback.IsRunning);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.UserCheck, "##PlaylistEnsembleStart", Language.ensemble_begin_ensemble_ready_check, size: Style.Dimensions.ButtonLarge))
            {
                if (Plugin.Config.UpdateInstrumentBeforeReadyCheck)
                {
                    Plugin.EnsembleManager.BroadcastEquipInstruments();
                    Plugin.EnsembleManager.BeginEnsembleReadyCheck(Plugin.Config.PreReadyCheckDelayMs);
                }
                else
                {
                    Plugin.EnsembleManager.BeginEnsembleReadyCheck();
                }
            }
        }
        else
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##PlaylistEnsembleStop", Language.ensemble_stop_ensemble, size: Style.Dimensions.ButtonLarge))
            {
                Plugin.EnsembleManager.BroadcastUnequipInstruments();
            }
        }
    }

    public async void RunImportFileTask()
    {
        if (_selectedPlaylist == null) return;

        var files = await _importHelper.GetMidiFilesFromFileDialogAsync(Plugin);
        if (files != null)
            StartPlaylistImport(files);
    }

    public async void RunImportFolderTask()
    {
        if (_selectedPlaylist == null) return;

        var files = await _importHelper.GetMidiFilesFromFolderDialogAsync(Plugin);
        if (files != null)
            StartPlaylistImport(files);
    }

    public async void RunImportOldPlaylistTask()
    {
        if (_selectedPlaylist == null) return;

        var mplPath = await _importHelper.GetMplFilePathAsync(Plugin);
        if (string.IsNullOrEmpty(mplPath) || !File.Exists(mplPath)) return;

        var filePaths = ParseMplFilePaths(mplPath);
        if (filePaths.Count == 0)
        {
            _messageDisplay.Show("No valid songs found in the playlist file.");
            return;
        }

        Plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(mplPath) ?? Plugin.Config.lastOpenedFolderPath;
        StartPlaylistImport(filePaths);
    }

    private static List<string> ParseMplFilePaths(string mplPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(mplPath));
            if (!doc.RootElement.TryGetProperty("Songs", out var songs))
                return new();

            var paths = new List<string>();
            foreach (var song in songs.EnumerateArray())
            {
                if (song.TryGetProperty("FilePath", out var fp))
                {
                    var path = fp.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }
            }
            return paths;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[PlaylistWindow] Failed to parse mpl file");
            return new();
        }
    }

    private void CancelImport() => _importHelper.Cancel();

    private void StartPlaylistImport(IEnumerable<string> files)
    {
        if (_selectedPlaylist == null) return;

        var playlistId = _selectedPlaylist.Id;
        var existingSongIds = PlaylistSongs.Select(ps => ps.Song?.Id ?? 0).Where(id => id > 0).ToHashSet();
        var baseOrder = PlaylistSongs.Count;

        _importHelper.OnImportCompleted = async () =>
        {
            _selectedPlaylist = await Plugin.PlaylistManager.GetPlaylistByIdAsync(playlistId);
            await LoadPlaylistSongsAsync(playlistId);
        };

        _importHelper.StartImport(files, async (filePath, _) =>
        {
            var songRepo = ServiceContainer.SongRepository;
            var playlistRepo = ServiceContainer.PlaylistRepository;

            var song = await songRepo.GetByFilePathAsync(filePath);
            if (song == null) return;

            if (!existingSongIds.Contains(song.Id))
            {
                var order = baseOrder + _importHelper.CurrentCount;
                await playlistRepo.AddSongToPlaylistAsync(playlistId, song.Id, order);
                existingSongIds.Add(song.Id);
            }
        });
    }
}
