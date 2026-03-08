using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MainWindow
{
    private bool _importDialogPending;
    public bool IsImportRunning => _importDialogPending || _importHelper.IsImporting;

    public async void RunImportFileTask()
    {
        if (IsImportRunning) return;
        _importDialogPending = true;
        DalamudApi.PluginLog.Debug("Import file task started");

        try
        {
            var files = await _importHelper.GetMidiFilesFromFileDialogAsync(Plugin);
            if (files != null)
                StartImportToCurrentPlaylist(files);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when importing files: {e}");
        }
        finally
        {
            _importDialogPending = false;
            DalamudApi.PluginLog.Debug("Import file task: dialog closed");
        }
    }

    public async void RunImportFolderTask()
    {
        if (IsImportRunning) return;
        _importDialogPending = true;
        DalamudApi.PluginLog.Debug("Import folder task started");

        try
        {
            var files = await _importHelper.GetMidiFilesFromFolderDialogAsync(Plugin);
            if (files != null)
                StartImportToCurrentPlaylist(files);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error during folder import: {e}");
        }
        finally
        {
            _importDialogPending = false;
            DalamudApi.PluginLog.Debug("Import folder task: dialog closed");
        }
    }

    private void StartImportToCurrentPlaylist(IEnumerable<string> files)
    {
        var currentPlaylist = Plugin.PlaylistManager.CurrentPlaylist;
        if (currentPlaylist == null || !currentPlaylist.IsValid)
        {
            DalamudApi.PluginLog.Warning("[MainWindow] Cannot import - no valid current playlist");
            return;
        }

        var playlistId = currentPlaylist.Id;
        var existingSongIds = currentPlaylist.Songs
            .Select(ps => ps.Song?.Id ?? 0)
            .Where(id => id > 0)
            .ToHashSet();
        var baseOrder = currentPlaylist.Songs.Count;

        _importHelper.OnImportCompleted = async () =>
        {
            await Plugin.PlaylistManager.ReloadAsync();
        };

        _importHelper.StartImport(files, async (filePath, _) =>
        {
            var song = await ServiceContainer.SongRepository.GetByFilePathAsync(filePath);
            if (song == null) return;

            if (!existingSongIds.Contains(song.Id))
            {
                var order = baseOrder + _importHelper.CurrentCount;
                await ServiceContainer.PlaylistRepository.AddSongToPlaylistAsync(playlistId, song.Id, order);
                existingSongIds.Add(song.Id);
            }
        });
    }

    private void DrawImportProgress()
    {
        ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            if (ImGui.Button("Cancel##MainWindowImport"))
                _importHelper.Cancel();
        }
    }
}
