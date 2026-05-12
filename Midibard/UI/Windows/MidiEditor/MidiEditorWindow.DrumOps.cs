using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private static readonly MidiForgeDrumTransposePreset[] DrumTransposePresets =
        Enum.GetValues<MidiForgeDrumTransposePreset>();

    private static readonly string[] DrumTransposePresetLabels = DrumTransposePresets
        .Select(preset => preset switch
        {
            MidiForgeDrumTransposePreset.BardForge2 => "BardForge 2",
            MidiForgeDrumTransposePreset.MogAmp => "MogAmp",
            _ => "BardForge Default",
        })
        .ToArray();

    private static MidiForgeDrumTransposePreset GetDrumTransposePreset(int index)
        => DrumTransposePresets[Math.Clamp(index, 0, DrumTransposePresets.Length - 1)];

    private static string[] GetDrumTransposeTargetLabels(MidiForgeDrumTransposePreset preset)
        => MidiForgeDrumMaps.GetTransposeTargets(preset)
            .Select(target => $"{target.Category} - {target.DrumkitInstrument} ({target.InputNote} -> {target.OutputNote})")
            .ToArray();

    private int[] GetSelectedDrumkitTrackIndices()
    {
        if (_file == null)
            return [];

        return _selectedTrackIndices
            .Where(index => index >= 0
                && index < _file.Tracks.Count
                && !_file.Tracks[index].IsConductorTrack
                && _file.Tracks[index].CloneCurrentChunk().GetNotes().Any(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel))
            .OrderBy(index => index)
            .ToArray();
    }

    private int[] GetSelectedSingleNoteTrackIndices()
    {
        if (_file == null)
            return [];

        return _selectedTrackIndices
            .Where(index => index >= 0
                && index < _file.Tracks.Count
                && !_file.Tracks[index].IsConductorTrack
                && _file.Tracks[index].CloneCurrentChunk().GetNotes()
                    .Select(note => (byte)note.NoteNumber)
                    .Distinct()
                    .Take(2)
                    .Count() == 1)
            .OrderBy(index => index)
            .ToArray();
    }

    private void DrawSplitDrumkitPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitDrumkitPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = GetSelectedDrumkitTrackIndices();

        ImGui.Text("Split Drumkit Tracks");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(220f);
        ImGui.Combo(
            "Transpose preset##splitDrumsTransposePreset",
            ref _splitDrumkitTransposePresetIndex,
            DrumTransposePresetLabels,
            DrumTransposePresetLabels.Length);
        ImGui.Checkbox("Auto-fix simultaneous hits (keep highest)##splitDrumsAutoEdit", ref _splitDrumkitAutoEditAfterSplit);
        ImGui.Checkbox("Create Drumkit Rest track for unmapped notes##splitDrumsRest", ref _splitDrumkitCreateRestTrack);
        ImGui.Checkbox("Move source drumkit tracks to end##splitDrumsMoveSource", ref _splitDrumkitMoveSourceTracksToEnd);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected drumkit track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitDrumkit"))
            {
                CaptureHistorySnapshot();
                MidiForgeOperations.SplitDrumkitTracks(
                    _file,
                    validIndices,
                    new MidiForgeSplitDrumkitOptions(
                        AutoEditAfterSplit: _splitDrumkitAutoEditAfterSplit,
                        CreateRestTrack: _splitDrumkitCreateRestTrack,
                        MoveSourceTracksToEnd: _splitDrumkitMoveSourceTracksToEnd,
                        TransposePreset: GetDrumTransposePreset(_splitDrumkitTransposePresetIndex)));

                _selectedTrackIndex = -1;
                _selectedEventIndices.Clear();
                _globalEventsChecked = false;
                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSplitDrumkit"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawDisassembleDrumkitPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##DisassembleDrumkitPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = GetSelectedDrumkitTrackIndices();

        ImGui.Text("Disassemble Drumkit Tracks");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Checkbox("Delete original drumkit tracks##disassembleDrumsDeleteOriginal", ref _disassembleDrumkitDeleteOriginalTracks);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected drumkit track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doDisassembleDrumkit"))
            {
                CaptureHistorySnapshot();
                MidiForgeOperations.DisassembleDrumkitTracks(
                    _file,
                    validIndices,
                    new MidiForgeDisassembleDrumkitOptions(
                        DeleteOriginalTracks: _disassembleDrumkitDeleteOriginalTracks));

                _selectedTrackIndex = -1;
                _selectedEventIndices.Clear();
                _globalEventsChecked = false;
                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelDisassembleDrumkit"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawTransposeSingleNoteTracksToDrumNotePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##TransposeSingleNoteTracksToDrumNotePopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = GetSelectedSingleNoteTrackIndices();
        var selectedPreset = GetDrumTransposePreset(_transposeToDrumPresetIndex);
        var transposeTargets = MidiForgeDrumMaps.GetTransposeTargets(selectedPreset);
        var transposeTargetLabels = GetDrumTransposeTargetLabels(selectedPreset);
        _transposeToDrumTargetIndex = Math.Clamp(
            _transposeToDrumTargetIndex,
            0,
            transposeTargets.Count - 1);

        ImGui.Text("Transpose Single-Note Tracks to Drum Note");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo(
            "Preset##transposeToDrumPreset",
            ref _transposeToDrumPresetIndex,
            DrumTransposePresetLabels,
            DrumTransposePresetLabels.Length))
        {
            _transposeToDrumTargetIndex = 0;
            var presetTargets = MidiForgeDrumMaps.GetTransposeTargets(GetDrumTransposePreset(_transposeToDrumPresetIndex));
            _transposeToDrumTrackName = presetTargets[0].Category;
        }

        selectedPreset = GetDrumTransposePreset(_transposeToDrumPresetIndex);
        transposeTargets = MidiForgeDrumMaps.GetTransposeTargets(selectedPreset);
        transposeTargetLabels = GetDrumTransposeTargetLabels(selectedPreset);
        _transposeToDrumTargetIndex = Math.Clamp(_transposeToDrumTargetIndex, 0, transposeTargets.Count - 1);

        ImGui.SetNextItemWidth(320f);
        if (ImGui.Combo(
            "Target##transposeToDrumTarget",
            ref _transposeToDrumTargetIndex,
            transposeTargetLabels,
            transposeTargetLabels.Length))
        {
            _transposeToDrumTrackName = transposeTargets[_transposeToDrumTargetIndex].Category;
        }

        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Track name##transposeToDrumTrackName", ref _transposeToDrumTrackName, 128);
        ImGui.Checkbox("Delete original tracks##transposeToDrumDeleteOriginal", ref _transposeToDrumDeleteOriginalTracks);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected single-note track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doTransposeSingleNoteTracksToDrumNote"))
            {
                var target = transposeTargets[_transposeToDrumTargetIndex];
                CaptureHistorySnapshot();
                MidiForgeOperations.TransposeSingleNoteTracksToDrumNote(
                    _file,
                    validIndices,
                    new MidiForgeTransposeToDrumNoteOptions(
                        TargetNote: target.OutputNote,
                        TrackName: _transposeToDrumTrackName,
                        DeleteOriginalTracks: _transposeToDrumDeleteOriginalTracks));

                _selectedTrackIndex = -1;
                _selectedEventIndices.Clear();
                _globalEventsChecked = false;
                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelTransposeSingleNoteTracksToDrumNote"))
            ImGui.CloseCurrentPopup();
    }
}
