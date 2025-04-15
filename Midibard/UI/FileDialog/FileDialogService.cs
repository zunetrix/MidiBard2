using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;

namespace MidiBard.Util;

public class FileDialogService
{
    public FileDialogManager DialogManager { get; }

    public FileDialogService(List<string>? foldersPaths)
    {
        DialogManager = new FileDialogManager();

        if (foldersPaths != null)
        {
            SetPinnedFolders(foldersPaths);
        }
    }

    public void AddWindowsQuickAccessFolders()
    {
        if (GetQuickAccessFolders(out var folders))
        {
            foreach (var ((name, path), idx) in folders.WithIndex())
                DialogManager.CustomSideBarItems.Add(($"{name}##{idx}", path, FontAwesomeIcon.Folder, -1));
        }
    }

    public void AddPinnedFolders(List<string> foldersPaths)
    {
        foldersPaths.RemoveAll(path => !Directory.Exists(path));

        foreach (var (path, idx) in foldersPaths.WithIndex())
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            DialogManager.CustomSideBarItems.Add(($"{name}##pinned{idx}", path, FontAwesomeIcon.Folder, -1));
        }
    }

    public void SetPinnedFolders(List<string> foldersPaths)
    {
        foldersPaths.RemoveAll(path => !Directory.Exists(path));
        ClearFavoriteFolders();

        foreach (var (path, idx) in foldersPaths.WithIndex())
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            DialogManager.CustomSideBarItems.Add(($"{name}##pinned{idx}", path, FontAwesomeIcon.Folder, -1));
        }
    }

    public void ClearFavoriteFolders()
    {
        DialogManager.CustomSideBarItems.Clear();
        DialogManager.CustomSideBarItems.Add(("Documents", string.Empty, 0, -1));
        DialogManager.CustomSideBarItems.Add(("Favorites", string.Empty, 0, -1));
        DialogManager.CustomSideBarItems.Add(("Videos", string.Empty, 0, -1));
        DialogManager.CustomSideBarItems.Add(("Music", string.Empty, 0, -1));
        DialogManager.CustomSideBarItems.Add(("Pictures", string.Empty, 0, -1));
    }

    private bool GetQuickAccessFolders(out List<(string Name, string Path)> folders)
    {
        folders = new List<(string Name, string Path)>();
        try
        {
            var shellAppType = Type.GetTypeFromProgID("Shell.Application");
            if (shellAppType == null)
                return false;

            var shell = Activator.CreateInstance(shellAppType);
            var obj = shellAppType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[]
            {
                "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}",
            });

            if (obj == null)
                return false;

            foreach (var fi in ((dynamic)obj).Items())
            {
                if (!fi.IsLink && !fi.IsFolder)
                    continue;

                folders.Add((fi.Name, fi.Path));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // public Task<string?> OpenFilePickerAsync(string title, string filter, string initialFolder)
    // {
    //     var tcs = new TaskCompletionSource<string?>();

    //     try
    //     {
    //         DialogManager.OpenFileDialog(title, filter, (result, files) =>
    //         {
    //             try
    //             {
    //                 if (result == true && files is { Count: > 0 })
    //                     tcs.TrySetResult(files[0]);
    //                 else
    //                     tcs.TrySetResult(null);
    //             }
    //             catch (Exception ex)
    //             {
    //                 PluginLog.Error($"[FilePicker Callback] Exception: {ex}");
    //                 tcs.TrySetException(ex);
    //             }
    //         }, 0, initialFolder);
    //     }
    //     catch (Exception ex)
    //     {
    //         PluginLog.Error($"[OpenFilePicker] Exception: {ex}");
    //         tcs.TrySetException(ex);
    //     }

    //     return tcs.Task;
    // }

    // public Task<string?> OpenFolderPickerAsync(string title, string? initialFolder = null)
    // {
    //     var tcs = new TaskCompletionSource<string?>();

    //     try
    //     {
    //         DialogManager.OpenFolderDialog(title, (result, folderPath) =>
    //         {
    //             try
    //             {
    //                 tcs.TrySetResult(result ? folderPath : null);
    //             }
    //             catch (Exception ex)
    //             {
    //                 PluginLog.Error($"[FolderPicker Callback] Exception: {ex}");
    //                 tcs.TrySetException(ex);
    //             }
    //         }, initialFolder ?? string.Empty);
    //     }
    //     catch (Exception ex)
    //     {
    //         PluginLog.Error($"[OpenFolderPicker] Exception: {ex}");
    //         tcs.TrySetException(ex);
    //     }

    //     return tcs.Task;
    // }
}
