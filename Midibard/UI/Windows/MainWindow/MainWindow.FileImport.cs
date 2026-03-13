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

    private async Task RunImportDialogAsync(Func<Plugin, Task<IEnumerable<string>?>> getFilesAsync)
    {
        if (IsImportRunning) return;
        _importDialogPending = true;
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
        finally
        {
            _importDialogPending = false;
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
        if (IsImportRunning) return;
        _importDialogPending = true;
        try
        {
            var files = await getFilesAsync(Plugin);
            if (files != null)
            {
                var fileList = files.ToArray();
                if (fileList.Length > 0)
                {
                    await Plugin.PlaylistManager.LoadTempPlaylistAsync(fileList);
                    Plugin.IpcProvider.LoadTempPlaylist(fileList);
                }
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when quick loading: {e}");
        }
        finally
        {
            _importDialogPending = false;
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

        // 4.2: OnImportCompleted is now Func<Task> so this lambda is properly typed (not async void)
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
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, Style.Colors.GrassGreen))
        {
            ImGui.ProgressBar(_importHelper.GetProgressValue(), ImGuiHelpers.ScaledVector2(-1, 20), _importHelper.GetProgressText());
        }

        if (ImGuiUtil.DangerButton("Cancel##MainWindowImport"))
            _importHelper.Cancel();
    }
}
