using System;
using System.IO;
using System.Threading;

using Microsoft.Win32;

using MidiBard.Resources;

namespace MidiBard.Win32;

static class FileDialogs
{
    //public delegate void MultiFileSelectedCallback(bool? fileDialogResult, string[] filePaths);
    //public delegate void FileSelectedCallback(bool? fileDialogResult, string filePath);
    //public delegate void FolderSelectedCallback(bool? fileDialogResult, string folderPath);
    //public delegate void SaveFileDialogCallback(bool? fileDialogResult, string filePath);

    public static void OpenMidiFileDialog(Action<bool?, string[]> callback, string initialDirectory)
    {
        var t = new Thread(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Midi Files (*.mid, *.midi, *.mmsong)|*.mid;*.midi;*.mmsong",
                RestoreDirectory = true,
                CheckFileExists = true,
                Multiselect = true,
                InitialDirectory = initialDirectory
            };

            callback(dialog.ShowDialog(), dialog.FileNames);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void OpenPlaylistDialog(Action<bool?, string> callback, string initialDirectory)
    {
        var t = new Thread(() =>
        {
            var dialog = new OpenFileDialog()
            {
                Filter = "Midibard playlist (*.mpl)|*.mpl",
                RestoreDirectory = true,
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = initialDirectory
            };
            callback(dialog.ShowDialog(), dialog.FileName);
        });

        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void FolderPicker(Action<bool?, string> callback, string initialDirectory)
    {
        var t = new Thread(() =>
        {
            var dlg = new FolderPicker();
            if (Directory.Exists(initialDirectory))
            {
                dlg.InputPath = initialDirectory;
            }
            callback(dlg.ShowDialog(DalamudApi.PluginInterface.UiBuilder.WindowHandlePtr), dlg.ResultPath);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void SavePlaylistDialog(Action<bool?, string> callback, string filename, string initialDirectory)
    {
        var t = new Thread(() =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = $"{Language.text_midibard_playlist} (*.mpl)|*.mpl",
                RestoreDirectory = true,
                AddExtension = true,
                DefaultExt = ".mpl",
                OverwritePrompt = true,
                FileName = filename,
                InitialDirectory = initialDirectory
            };
            callback(dialog.ShowDialog(), dialog.FileName);
        });
        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void SaveFileDialog(Action<bool, string> callback, string initDirectory, string filename = null, string filter = null, string defaultExt = null)
    {
        var t = new Thread(() =>
        {
            var dialog = new SaveFileDialog
            {
                RestoreDirectory = true,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = initDirectory
            };

            if (filename is not null) dialog.FileName = filename;
            if (filter is not null) dialog.Filter = filter;
            if (defaultExt is not null) dialog.DefaultExt = defaultExt;

            callback(dialog.ShowDialog() == true, dialog.FileName);
        });

        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void OpenFileDialog(Action<bool, string, string[]> callback, string initDirectory, bool multiselect = true, string filter = null)
    {
        var t = new Thread(() =>
        {
            var dialog = new OpenFileDialog()
            {
                Filter = filter,
                RestoreDirectory = true,
                CheckFileExists = true,
                Multiselect = multiselect,
                InitialDirectory = initDirectory
            };
            callback(dialog.ShowDialog() == true, dialog.FileName, dialog.FileNames);
        });

        t.IsBackground = true;
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

}
