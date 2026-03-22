using System;
using System.IO;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

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

        var defaultName = Path.GetFileName(_file.FilePath ?? "untitled");
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
                "Merge MIDI File", ".mid,.midi",
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

            var merged = _mergeSongSequential
                ? Merger.MergeSequentially(new[] { _file.Source, imported },
                    new SequentialMergingSettings
                    {
                        DelayBetweenFiles = _mergeSongDelayMs > 0
                            ? new MetricTimeSpan(_mergeSongDelayMs * 1_000L)
                            : null,
                    })
                : Merger.MergeSimultaneously(new[] { _file.Source, imported },
                    new SimultaneousMergingSettings
                    {
                        IgnoreDifferentTempoMaps = _mergeSongIgnoreDifferentTempo,
                    });

            var prevPath = _file.FilePath;
            foreach (var t in _file.Tracks) t.Dispose();
            _file = new EditableMidiFile(merged, prevPath);

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
            _file.IsDirty = true;
            SelectTrack(-1);
            _selectedTrackIndices.Clear();
            _selectedEventIndices.Clear();
            _globalTracksChecked = false;
            _globalEventsChecked = false;
            DalamudApi.PluginLog.Info($"[MidiEditor] Merged '{Path.GetFileName(path)}' ({(_mergeSongSequential ? "sequential" : "simultaneous")})");
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditor] Failed to merge MIDI file");
        }
    }
}
