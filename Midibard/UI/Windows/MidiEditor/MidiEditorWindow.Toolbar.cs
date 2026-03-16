using System.IO;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawToolbar()
    {
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4));

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##midiEdOpen", "Open MIDI File",
            size: Style.Dimensions.ButtonLarge))
            OpenMidiFileDialog();

        ImGui.SameLine();

        using (ImRaii.Disabled(_file is not { IsDirty: true }))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Save, "##midiEdSave", "Save",
                size: Style.Dimensions.ButtonLarge))
                _file?.Save();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(_file == null))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##midiEdSaveAs", "Save As",
                size: Style.Dimensions.ButtonLarge))
                SaveAsDialog();
        }
    }

    private void OpenMidiFileDialog()
    {
        var initDir = _plugin.Config.lastOpenedFolderPath;

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.OpenMidiFileDialog((result, paths) =>
            {
                if (result == true && paths is { Length: > 0 })
                    OpenFile(paths[0]);
            }, initDir);
        }
        else
        {
            _plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Open MIDI File", ".mid,.midi",
                (result, paths) =>
                {
                    if (result && paths.Count > 0)
                        OpenFile(paths[0]);
                },
                1, initDir);
        }
    }

    private void SaveAsDialog()
    {
        if (_file == null) return;

        var defaultName = Path.GetFileNameWithoutExtension(_file.FilePath ?? "untitled");
        var initDir = Path.GetDirectoryName(_file.FilePath) ?? _plugin.Config.lastOpenedFolderPath;

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.SaveFileDialog(
                (result, path) =>
                {
                    if (result && !string.IsNullOrEmpty(path))
                        _file.SaveAs(path);
                },
                initDir, defaultName, "MIDI files|*.mid;*.midi", ".mid");
        }
        else
        {
            _plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(
                "Save MIDI File As", "Midi files{.mid,.midi}",
                defaultName, ".mid",
                (result, path) =>
                {
                    if (result && !string.IsNullOrEmpty(path))
                        _file.SaveAs(path);
                },
                initDir);
        }
    }
}
