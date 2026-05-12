using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private static readonly string[] DrumTransposeTargetLabels = MidiForgeDrumMaps.DefaultTransposeTargets
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
                        MoveSourceTracksToEnd: _splitDrumkitMoveSourceTracksToEnd));

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
        _transposeToDrumTargetIndex = Math.Clamp(
            _transposeToDrumTargetIndex,
            0,
            MidiForgeDrumMaps.DefaultTransposeTargets.Count - 1);

        ImGui.Text("Transpose Single-Note Tracks to Drum Note");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(320f);
        if (ImGui.Combo(
            "Target##transposeToDrumTarget",
            ref _transposeToDrumTargetIndex,
            DrumTransposeTargetLabels,
            DrumTransposeTargetLabels.Length))
        {
            _transposeToDrumTrackName = MidiForgeDrumMaps.DefaultTransposeTargets[_transposeToDrumTargetIndex].Category;
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
                var target = MidiForgeDrumMaps.DefaultTransposeTargets[_transposeToDrumTargetIndex];
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
