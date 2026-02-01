using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using MidiBard.Win32;

namespace MidiBard;

// TODO: refactor inside FileDialogService
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
            CheckLastOpenedFolderPath();

            if (Plugin.Config.useLegacyFileDialog)
                await RunImportFileTaskWin32Async();
            else
                await RunImportFileTaskImGuiAsync();
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
            CheckLastOpenedFolderPath();

            if (Plugin.Config.useLegacyFileDialog)
                await RunImportFolderTaskWin32Async();
            else
                await RunImportFolderTaskImGuiAsync();
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

    private Task RunImportFileTaskWin32Async()
    {
        var tcs = new TaskCompletionSource();

        FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true && filePaths is { Length: > 0 })
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Plugin.PlaylistManager.AddAsync(filePaths);
                        Plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during file import: {ex.Message}");
                    }
                    finally
                    {
                        tcs.TrySetResult();
                    }
                });
            }
            else
            {
                tcs.TrySetResult();
            }
        }, initialDirectory: Plugin.Config.lastOpenedFolderPath);

        return tcs.Task;
    }

    private Task RunImportFileTaskImGuiAsync()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFileDialogResult(bool result, List<string> filePaths)
        {
            if (result && filePaths.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Plugin.PlaylistManager.AddAsync(filePaths);
                        Plugin.Config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during file import: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }

        try
        {
            Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                title: "Open",
                filters: ".mid,.midi,.mmsong",
                callback: OnFileDialogResult,
                selectionCountMax: 0,
                startPath: Plugin.Config.lastOpenedFolderPath
            );
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error($"Failed to open file dialog: {e}");
            tcs.TrySetException(e);
        }

        return tcs.Task;
    }

    private Task RunImportFolderTaskWin32Async()
    {
        var tcs = new TaskCompletionSource();

        FileDialogs.FolderPicker((result, folderPath) =>
        {
            if (result == true && !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await Plugin.PlaylistManager.AddAsync(files);
                        Plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during folder import: {ex.Message}");
                    }
                    finally
                    {
                        tcs.TrySetResult();
                    }
                });
            }
            else
            {
                tcs.TrySetResult();
            }
        }, initialDirectory: Plugin.Config.lastOpenedFolderPath);

        return tcs.Task;
    }

    private Task RunImportFolderTaskImGuiAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Open folder", (result, folderPath) =>
        {
            if (result && Directory.Exists(folderPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await Plugin.PlaylistManager.AddAsync(files);
                        Plugin.Config.lastOpenedFolderPath = Directory.GetParent(folderPath)?.FullName ?? folderPath;
                    }
                    catch (Exception ex)
                    {
                        DalamudApi.PluginLog.Error($"Error during folder import: {ex.Message}");
                    }
                    finally
                    {
                        tcs.TrySetResult(true);
                    }
                });
            }
            else
            {
                tcs.TrySetResult(false);
            }
        }, Plugin.Config.lastOpenedFolderPath);

        return tcs.Task;
    }

    private void CheckLastOpenedFolderPath()
    {
        if (!Directory.Exists(Plugin.Config.lastOpenedFolderPath))
        {
            Plugin.Config.lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
