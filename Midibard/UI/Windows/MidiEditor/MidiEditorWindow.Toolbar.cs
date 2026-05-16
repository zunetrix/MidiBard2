using System;
using System.IO;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.File;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawToolbar()
    {
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4));

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##midiEdOpen", MidiEditorOperationHelp.OpenMidiFile,
            size: Style.Dimensions.ButtonLarge))
            OpenMidiFileDialog();

        ImGui.SameLine();

        using (ImRaii.Disabled(_file is not { IsDirty: true } || string.IsNullOrWhiteSpace(_file.FilePath)))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Save, "##midiEdSave", MidiEditorOperationHelp.SaveMidiFile,
                size: Style.Dimensions.ButtonLarge))
                SaveMidiFile();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(_file == null))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##midiEdSaveAs", MidiEditorOperationHelp.SaveMidiFileAs,
                size: Style.Dimensions.ButtonLarge))
                SaveAsDialog();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(_file == null || !_history.CanUndo))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##midiEdUndo", MidiEditorOperationHelp.Undo,
                size: Style.Dimensions.ButtonLarge))
                UndoMidiEdit();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(_file == null || !_history.CanRedo))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Redo, "##midiEdRedo", MidiEditorOperationHelp.Redo,
                size: Style.Dimensions.ButtonLarge))
                RedoMidiEdit();
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
                "Open MIDI File", MidiOpenDialogExtensions,
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

        var defaultName = Path.GetFileName(_file.FilePath ?? _file.DisplayName);
        var initDir = Path.GetDirectoryName(_file.FilePath) ?? _plugin.Config.lastOpenedFolderPath;

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.SaveFileDialog(
                (result, path) =>
                {
                    if (result && !string.IsNullOrEmpty(path))
                        SaveMidiFileAs(path);
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
                        SaveMidiFileAs(path);
                },
                initDir);
        }
    }

    private void ExportLrcFromMidiMetadataDialog()
    {
        if (_file == null) return;

        var defaultName = Path.ChangeExtension(Path.GetFileName(_file.FilePath ?? "untitled"), ".lrc");
        var initDir = Path.GetDirectoryName(_file.FilePath) ?? _plugin.Config.lastOpenedFolderPath;

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.SaveFileDialog(
                (result, path) =>
                {
                    if (result && !string.IsNullOrEmpty(path))
                        ExportLrcFromMidiMetadata(path);
                },
                initDir, defaultName, "LRC files|*.lrc", ".lrc");
        }
        else
        {
            _plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(
                "Export LRC File", "LRC files{.lrc}",
                defaultName, ".lrc",
                (result, path) =>
                {
                    if (result && !string.IsNullOrEmpty(path))
                        ExportLrcFromMidiMetadata(path);
                },
                initDir);
        }
    }

    private void ExportLrcFromMidiMetadata(string path)
    {
        if (_file == null) return;

        try
        {
            var title = Path.GetFileNameWithoutExtension(_file.FilePath ?? _file.DisplayName);
            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileNameWithoutExtension(path);

            var result = _editorCommandExecutor.Execute(
                new ExportLrcMetadataCommand(),
                CreateEditorCommandContext(),
                new ExportLrcMetadataOptions(path, title));
            if (!result.Succeeded)
            {
                DalamudApi.PrintError(result.Message);
                return;
            }

            var export = result.Result!.Value;
            if (export.HasLyrics)
                DalamudApi.PrintEcho($"Exported {export.LineCount} LRC line(s): {path}");
            else
                DalamudApi.PrintEcho("No MIDI lyric/text metadata found; exported a blank LRC template.");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditor] Failed to export MIDI metadata as LRC");
            DalamudApi.PrintError("Failed to export MIDI metadata as LRC. See plugin log for details.");
        }
    }

    private void OpenMergeSongDialog()
    {
        if (_file == null) return;
        var initDir = Path.GetDirectoryName(_file.FilePath) ?? _plugin.Config.lastOpenedFolderPath;

        if (_plugin.Config.useLegacyFileDialog)
        {
            Win32.FileDialogs.OpenMidiFileDialog((result, paths) =>
            {
                if (result == true && paths is { Length: > 0 })
                    MergeSongFromFile(paths[0]);
            }, initDir);
        }
        else
        {
            _plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog(
                "Merge MIDI File", MidiOpenDialogExtensions,
                (result, paths) =>
                {
                    if (result && paths.Count > 0)
                        MergeSongFromFile(paths[0]);
                },
                1, initDir);
        }
    }

    private void MergeSongFromFile(string path)
    {
        if (_file == null || !File.Exists(path)) return;
        try
        {
            var imported = ServiceContainer.MidiFileService.LoadMidiFile(path);
            if (imported == null) return;

            var mergeSongState = GetMergeSongPopupState();
            var result = _editorCommandExecutor.Execute(
                new MergeSongCommand(),
                CreateEditorCommandContext(),
                new MergeSongOptions(
                    imported,
                    mergeSongState.Sequential,
                    mergeSongState.DelayMilliseconds,
                    mergeSongState.IgnoreDifferentTempoMaps));

            if (result.Succeeded)
            {
                ApplyDocumentCommandResult(resetTransientState: true);
                DalamudApi.PluginLog.Info($"[MidiEditor] Merged '{Path.GetFileName(path)}' ({(mergeSongState.Sequential ? "sequential" : "simultaneous")})");
            }
            else
            {
                DalamudApi.PluginLog.Warning($"[MidiEditor] Merge MIDI file command rejected: {result.Message}");
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditor] Failed to merge MIDI file");
        }
    }

    private void SaveMidiFile()
    {
        if (_file == null) return;

        var result = _editorCommandExecutor.Execute(
            new SaveFileCommand(),
            CreateEditorCommandContext(),
            new EditorOperationEmptyOptions());
        if (result.Succeeded)
            ApplyDocumentCommandResult(resetTransientState: false);
    }

    private void SaveMidiFileAs(string path)
    {
        if (_file == null) return;

        var result = _editorCommandExecutor.Execute(
            new SaveFileAsCommand(),
            CreateEditorCommandContext(),
            new SaveFileAsOptions(path));
        if (result.Succeeded)
            ApplyDocumentCommandResult(resetTransientState: false);
    }
}
