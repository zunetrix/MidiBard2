using System;
using System.IO;
using System.Text;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;

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

        using (ImRaii.Disabled(_file is not { IsDirty: true } || string.IsNullOrWhiteSpace(_file.FilePath)))
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

        ImGui.SameLine();

        using (ImRaii.Disabled(_file == null || !_history.CanUndo))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##midiEdUndo", "Undo",
                size: Style.Dimensions.ButtonLarge))
                UndoMidiEdit();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(_file == null || !_history.CanRedo))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Redo, "##midiEdRedo", "Redo",
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
            foreach (var track in _file.Tracks)
                track.FlushChanges();

            var title = Path.GetFileNameWithoutExtension(_file.FilePath ?? _file.DisplayName);
            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileNameWithoutExtension(path);

            var result = MidiForgeLyricsExporter.Export(_file.Source, title);
            File.WriteAllText(path, result.Content, Encoding.UTF8);

            if (result.HasLyrics)
                DalamudApi.PrintEcho($"Exported {result.Lines.Count} LRC line(s): {path}");
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

            // Flush in-memory edits so Source reflects the current state before merging
            foreach (var t in _file.Tracks) t.FlushChanges();

            var mergeSongState = GetMergeSongPopupState();

            var merged = mergeSongState.Sequential
                ? Merger.MergeSequentially(new[] { _file.Source, imported },
                    new SequentialMergingSettings
                    {
                        DelayBetweenFiles = mergeSongState.DelayMilliseconds > 0
                            ? new MetricTimeSpan(mergeSongState.DelayMilliseconds * 1_000L)
                            : null,
                    })
                : Merger.MergeSimultaneously(new[] { _file.Source, imported },
                    new SimultaneousMergingSettings
                    {
                        IgnoreDifferentTempoMaps = mergeSongState.IgnoreDifferentTempoMaps,
                    });

            var prevPath = _file.FilePath;
            foreach (var t in _file.Tracks) t.Dispose();
            _file = new EditableMidiFile(merged, prevPath);
            _history.Clear();

            // Consolidate if the merge produced more than one conductor track
            _file.MergeMultipleConductorTracks();
            _file.ConsolidateTempoToConductorTrack();
            // Remove any duplicate tempo/time-signature events introduced by the merge
            _file.SanitizeFile(new Melanchall.DryWetMidi.Tools.SanitizingSettings
            {
                RemoveDuplicatedSetTempoEvents = true,
                RemoveDuplicatedTimeSignatureEvents = true,
                RemoveDuplicatedNotes = false,
                RemoveEmptyTrackChunks = false,
                RemoveOrphanedNoteOffEvents = false,
                Trim = false,
            });
            _file.MarkChanged();
            SelectTrack(-1);
            _selectedTrackIndices.Clear();
            _selectedEventIndices.Clear();
            _globalTracksChecked = false;
            _globalEventsChecked = false;
            DalamudApi.PluginLog.Info($"[MidiEditor] Merged '{Path.GetFileName(path)}' ({(mergeSongState.Sequential ? "sequential" : "simultaneous")})");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditor] Failed to merge MIDI file");
        }
    }
}
