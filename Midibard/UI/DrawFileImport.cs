// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using static Dalamud.api;

using MidiBard.UI.Win32;

namespace MidiBard;

public partial class PluginUI
{
    public bool IsImportRunning { get; private set; }

    #region import
    private void RunImportFileTask()
    {
        if (IsImportRunning) return;
        IsImportRunning = true;

        try
        {
            CheckLastOpenedFolderPath();

            if (MidiBard.config.useLegacyFileDialog)
            {
                RunImportFileTaskWin32();
            }
            else
            {
                RunImportFileTaskImGui();
            }
        }
        catch (Exception e)
        {
            IsImportRunning = false;
            PluginLog.Error($"Error when importing files: {e.Message}");
        }
    }

    private void RunImportFolderTask()
    {
        if (IsImportRunning) return;
        IsImportRunning = true;

        try
        {
            CheckLastOpenedFolderPath();

            if (MidiBard.config.useLegacyFileDialog)
            {
                RunImportFolderTaskWin32();
            }
            else
            {
                RunImportFolderTaskImGui();
            }
        }
        catch (Exception e)
        {
            IsImportRunning = false;
            PluginLog.Error($"Error during folder import: {e.Message}");
        }
    }

    private void RunImportFileTaskWin32()
    {
        FileDialogs.OpenMidiFileDialog((result, filePaths) =>
        {
            if (result == true)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await PlaylistManager.AddAsync(filePaths);
                        MidiBard.config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during file import: {ex.Message}");
                    }
                    finally
                    {
                        IsImportRunning = false;
                    }
                });
            }
            else
            {
                IsImportRunning = false;
            }
        });
    }

    private void RunImportFileTaskImGui()
    {
        void OnFileDialogResult(bool result, List<string> filePaths)
        {
            if (result)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await PlaylistManager.AddAsync(filePaths);
                        MidiBard.config.lastOpenedFolderPath = Path.GetDirectoryName(filePaths[0]);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during file import: {ex.Message}");
                    }
                    finally
                    {
                        IsImportRunning = false;
                    }
                });
            }
            else
            {
                IsImportRunning = false;
            }
        }

        fileDialogManager.OpenFileDialog("Open", ".mid,.midi,.mmsong", OnFileDialogResult, 0, MidiBard.config.lastOpenedFolderPath);
    }

    private void RunImportFolderTaskImGui()
    {
        fileDialogManager.OpenFolderDialog("Open folder", (b, folderPath) =>
        {
            //PluginLog.Debug($"dialog result: {b}\n{string.Join("\n", filePath)}");
            if (b)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                            .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                        await PlaylistManager.AddAsync(files);
                    }
                    finally
                    {
                        IsImportRunning = false;
                        MidiBard.config.lastOpenedFolderPath = Directory.GetParent(folderPath).FullName;
                    }
                });
            }
            else
            {
                IsImportRunning = false;
            }
        }, MidiBard.config.lastOpenedFolderPath);
    }

    private void RunImportFolderTaskWin32()
    {
        FileDialogs.FolderPicker((result, folderPath) =>
        {
            if (result == true)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        if (Directory.Exists(folderPath))
                        {
                            var allowedExtensions = new[] { ".mid", ".midi", ".mmsong" };
                            var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                .Where(i => allowedExtensions.Any(ext => i.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase)));
                            await PlaylistManager.AddAsync(files);
                            MidiBard.config.lastOpenedFolderPath = Directory.GetParent(folderPath).FullName;
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error during folder import: {ex.Message}");
                    }
                    finally
                    {
                        IsImportRunning = false;
                    }
                });
            }
            else
            {
                IsImportRunning = false;
            }
        });
    }

    private void CheckLastOpenedFolderPath()
    {
        if (!Directory.Exists(MidiBard.config.lastOpenedFolderPath))
        {
            MidiBard.config.lastOpenedFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    #endregion
}
