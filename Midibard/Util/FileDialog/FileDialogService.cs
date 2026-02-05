using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;

using MidiBard.Extensions.Enumerable;

namespace MidiBard.Util;

public class FileDialogService
{
    public FileDialogManager FileDialogManager { get; }

    public FileDialogService(List<string>? foldersPaths)
    {
        this.FileDialogManager = new FileDialogManager();
        // remove dialog header, there is a bug where if its closed while minimized the callback result is never invoked
        this.FileDialogManager.AddedWindowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;

        if (foldersPaths != null)
        {
            OverwriteCustomPinnedFolders(foldersPaths);
        }
    }

    public void OverwriteCustomPinnedFolders(List<string> foldersPaths)
    {
        foldersPaths.RemoveAll(path => !Directory.Exists(path));
        ClearCustomFavoriteFolders();

        foreach (var (path, idx) in foldersPaths.WithIndex())
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            this.FileDialogManager.CustomSideBarItems.Add(($"{name}##pinned{idx}", path, FontAwesomeIcon.Folder, -1));
        }
    }

    public void ClearCustomFavoriteFolders()
    {
        this.FileDialogManager.CustomSideBarItems.Clear();
    }

    public void RemoveWindowsQuickAccessFolders()
    {
        this.FileDialogManager.CustomSideBarItems.Add(("Favorites", string.Empty, 0, -1));
        this.FileDialogManager.CustomSideBarItems.Add(("Downloads", string.Empty, 0, -1));
        this.FileDialogManager.CustomSideBarItems.Add(("Documents", string.Empty, 0, -1));
        this.FileDialogManager.CustomSideBarItems.Add(("Videos", string.Empty, 0, -1));
        this.FileDialogManager.CustomSideBarItems.Add(("Music", string.Empty, 0, -1));
        this.FileDialogManager.CustomSideBarItems.Add(("Pictures", string.Empty, 0, -1));
    }

    private bool GetWindowsUserPinnedFolders(out List<(string Name, string Path)> folders)
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

    public void AddWindowsUserPinnedFolders()
    {
        if (GetWindowsUserPinnedFolders(out var folders))
        {
            foreach (var ((name, path), idx) in folders.WithIndex())
                this.FileDialogManager.CustomSideBarItems.Add(($"{name}", path, FontAwesomeIcon.Folder, -1));
        }
    }

    public void RemoveWindowsUserPinnedFolders()
    {
        if (GetWindowsUserPinnedFolders(out var folders))
        {
            foreach (var ((name, path), idx) in folders.WithIndex())
                this.FileDialogManager.CustomSideBarItems.Add(($"{name}", string.Empty, 0, -1));
        }
    }
}
