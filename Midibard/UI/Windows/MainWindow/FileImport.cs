using System;
using System.Threading.Tasks;

namespace MidiBard;

// TODO: refactor
public partial class MainWindow
{
    public bool IsImportRunning { get; private set; }

    public async void RunImportFileTask()
    {
        if (IsImportRunning) return;
        IsImportRunning = true;
        DalamudApi.PluginLog.Debug("Import file task started");

        try
        {
            var files = await _importHelper.GetMidiFilesFromFileDialogAsync(Plugin);
            if (files != null)
                await Task.Run(() => Plugin.PlaylistManager.AddAsync(files));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error when importing files: {e}");
        }
        finally
        {
            IsImportRunning = false;
            DalamudApi.PluginLog.Debug("Import file task finished");
        }
    }

    public async void RunImportFolderTask()
    {
        if (IsImportRunning) return;
        IsImportRunning = true;
        DalamudApi.PluginLog.Debug("Import folder task started");

        try
        {
            var files = await _importHelper.GetMidiFilesFromFolderDialogAsync(Plugin);
            if (files != null)
                await Task.Run(() => Plugin.PlaylistManager.AddAsync(files));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Error during folder import: {e}");
        }
        finally
        {
            IsImportRunning = false;
            DalamudApi.PluginLog.Debug("Import folder task finished");
        }
    }
}


