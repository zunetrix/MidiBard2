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
    private async Task RunImportDialogAsync(Func<Plugin, Task<IEnumerable<string>?>> getFilesAsync)
    {
        if (_importHelper.IsRunning) return;
        try
        {
            var files = await getFilesAsync(Plugin);
            if (files != null)
                StartImportToCurrentPlaylist(files);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when importing: {e}");
        }
    }

    public async void RunImportFileTask() =>
        await (Plugin.Config.TempPlaylistMode
            ? RunQuickLoadDialogAsync(_importHelper.GetMidiFilesFromFileDialogAsync)
            : RunImportDialogAsync(_importHelper.GetMidiFilesFromFileDialogAsync));

    public async void RunImportFolderTask() =>
        await (Plugin.Config.TempPlaylistMode
            ? RunQuickLoadDialogAsync(_importHelper.GetMidiFilesFromFolderDialogAsync)
            : RunImportDialogAsync(_importHelper.GetMidiFilesFromFolderDialogAsync));

    public async void RunQuickLoadFileTask() =>
        await RunQuickLoadDialogAsync(_importHelper.GetMidiFilesFromFileDialogAsync);

    public async void RunQuickLoadFolderTask() =>
        await RunQuickLoadDialogAsync(_importHelper.GetMidiFilesFromFolderDialogAsync);

    private async Task RunQuickLoadDialogAsync(Func<Plugin, Task<IEnumerable<string>?>> getFilesAsync)
    {
        if (_importHelper.IsRunning) return;
        try
        {
            var files = await getFilesAsync(Plugin);
            if (files != null)
            {
                var fileList = files.ToArray();
                if (fileList.Length > 0)
                {
                    _importHelper.SetProgress(0, fileList.Length);
                    var progress = new Progress<(int current, int total)>(p => {
                        if (_importHelper.IsRunning) _importHelper.SetProgress(p.current, p.total);
                    });
                    try
                    {
                        await Plugin.PlaylistManager.LoadTempPlaylistAsync(fileList, progress);
                        Plugin.IpcProvider.LoadTempPlaylist(fileList);
                    }
                    finally
                    {
                        _importHelper.StopProgress();
                    }
                }
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when quick loading: {e}");
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
            // Broadcast to other clients so they also pick up the new songs
            Plugin.IpcProvider.LoadPlaylist(playlistId);
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
        var value = _importHelper.TotalCount > 0 ? _importHelper.GetProgressValue() : -1f;
        var text  = _importHelper.TotalCount > 0 ? _importHelper.GetProgressText()  : "Loading...";

        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, Style.Colors.GrassGreen))
        {
            ImGui.ProgressBar(value, ImGuiHelpers.ScaledVector2(-1, 20), text);
        }

        if (ImGuiUtil.DangerButton("Cancel##MainWindowImport"))
            _importHelper.Cancel();
    }
}
