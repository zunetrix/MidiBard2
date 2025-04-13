using System;
using System.Collections.Generic;
using System.Reflection;

using Dalamud.Interface;

using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Utility;

using MidiBard.Util;

public static class FileDialogService
{
    public static FileDialogManager CreateFileDialogManager()
    {
        var fileManager = new FileDialogManager();

        if (GetQuickAccessFolders(out var folders))
        {
            foreach (var ((name, path), idx) in folders.WithIndex())
                fileManager.CustomSideBarItems.Add(($"{name}##{idx}", path, FontAwesomeIcon.Folder, -1));
        }

        fileManager.CustomSideBarItems.Add(("Videos", string.Empty, 0, -1));
        fileManager.CustomSideBarItems.Add(("Music", string.Empty, 0, -1));
        fileManager.CustomSideBarItems.Add(("Pictures", string.Empty, 0, -1));

        return fileManager;
    }

    public static bool GetQuickAccessFolders(out List<(string Name, string Path)> folders)
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
}
