using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.Drum;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const string SplitDrumkitPopupStateKey = "drum.split-drumkit.popup";
    private const string DisassembleDrumkitPopupStateKey = "drum.disassemble-drumkit.popup";
    private const string TransposeSingleNoteTracksToDrumNotePopupStateKey = "drum.transpose-single-note-tracks-to-drum-note.popup";

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

    private string[] GetDrumTransposeTargetLabels(MidiForgeDrumTransposePreset preset)
        => CreateEditorMidiMapProvider()
            .GetDrumTransposeTargets(preset)
            .Select(target => $"{target.Category} - {target.DrumkitInstrument} ({target.InputNote} -> {target.OutputNote})")
            .ToArray();

    private static string GetDefaultTransposeToDrumTrackName()
        => MidiForgeDrumMaps.GetTransposeTargets(MidiForgeDrumTransposePreset.Default)[0].Category;

    private SplitDrumkitPopupState GetSplitDrumkitPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SplitDrumkitPopupStateKey,
            static () => new SplitDrumkitPopupState());

    private DisassembleDrumkitPopupState GetDisassembleDrumkitPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            DisassembleDrumkitPopupStateKey,
            static () => new DisassembleDrumkitPopupState());

    private TransposeSingleNoteTracksToDrumNotePopupState GetTransposeSingleNoteTracksToDrumNotePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            TransposeSingleNoteTracksToDrumNotePopupStateKey,
            static () => new TransposeSingleNoteTracksToDrumNotePopupState());

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

        var state = GetSplitDrumkitPopupState();
        var validIndices = GetSelectedDrumkitTrackIndices();

        ImGui.Text("Split Drumkit Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitDrumkit);

        ImGui.SetNextItemWidth(220f);
        ImGui.Combo(
            "Transpose preset##splitDrumsTransposePreset",
            ref state.TransposePresetIndex,
            DrumTransposePresetLabels,
            DrumTransposePresetLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.DrumTransposePreset);
        ImGui.Checkbox("Auto-fix simultaneous hits (keep highest)##splitDrumsAutoEdit", ref state.AutoEditAfterSplit);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.DrumAutoEdit);
        ImGui.Checkbox("Create Drumkit Rest track for unmapped notes##splitDrumsRest", ref state.CreateRestTrack);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.DrumRestTrack);
        ImGui.Checkbox("Move source drumkit tracks to end##splitDrumsMoveSource", ref state.MoveSourceTracksToEnd);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.DrumMoveSource);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected drumkit track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitDrumkit"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SplitDrumkitTracksCommand(),
                    CreateEditorCommandContext(),
                    new SplitDrumkitTracksCommandOptions(
                        validIndices,
                        new MidiForgeSplitDrumkitOptions(
                            AutoEditAfterSplit: state.AutoEditAfterSplit,
                            CreateRestTrack: state.CreateRestTrack,
                            MoveSourceTracksToEnd: state.MoveSourceTracksToEnd,
                            TransposePreset: GetDrumTransposePreset(state.TransposePresetIndex))));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
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

        var state = GetDisassembleDrumkitPopupState();
        var validIndices = GetSelectedDrumkitTrackIndices();

        ImGui.Text("Disassemble Drumkit Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.DisassembleDrumkit);

        ImGui.Checkbox("Delete original drumkit tracks##disassembleDrumsDeleteOriginal", ref state.DeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.DisassembleDrumkitDeleteOriginal);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected drumkit track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doDisassembleDrumkit"))
            {
                var result = _editorCommandExecutor.Execute(
                    new DisassembleDrumkitTracksCommand(),
                    CreateEditorCommandContext(),
                    new DisassembleDrumkitTracksCommandOptions(
                        validIndices,
                        new MidiForgeDisassembleDrumkitOptions(
                            DeleteOriginalTracks: state.DeleteOriginalTracks)));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
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

        var state = GetTransposeSingleNoteTracksToDrumNotePopupState();
        var validIndices = GetSelectedSingleNoteTrackIndices();
        var selectedPreset = GetDrumTransposePreset(state.PresetIndex);
        var transposeTargets = CreateEditorMidiMapProvider().GetDrumTransposeTargets(selectedPreset);
        var transposeTargetLabels = GetDrumTransposeTargetLabels(selectedPreset);
        state.TargetIndex = Math.Clamp(
            state.TargetIndex,
            0,
            transposeTargets.Count - 1);

        ImGui.Text("Retarget Single-Note Drum Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.TransposeSingleNoteToDrum);

        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo(
            "Preset##transposeToDrumPreset",
            ref state.PresetIndex,
            DrumTransposePresetLabels,
            DrumTransposePresetLabels.Length))
        {
            state.TargetIndex = 0;
            var presetTargets = CreateEditorMidiMapProvider().GetDrumTransposeTargets(GetDrumTransposePreset(state.PresetIndex));
            state.TrackName = presetTargets[0].Category;
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.DrumTransposePreset);

        selectedPreset = GetDrumTransposePreset(state.PresetIndex);
        transposeTargets = CreateEditorMidiMapProvider().GetDrumTransposeTargets(selectedPreset);
        transposeTargetLabels = GetDrumTransposeTargetLabels(selectedPreset);
        state.TargetIndex = Math.Clamp(state.TargetIndex, 0, transposeTargets.Count - 1);

        ImGui.SetNextItemWidth(320f);
        if (ImGui.Combo(
            "Target##transposeToDrumTarget",
            ref state.TargetIndex,
            transposeTargetLabels,
            transposeTargetLabels.Length))
        {
            state.TrackName = transposeTargets[state.TargetIndex].Category;
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TransposeToDrumTarget);

        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Track name##transposeToDrumTrackName", ref state.TrackName, 128);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TransposeToDrumTrackName);
        ImGui.Checkbox("Delete original tracks##transposeToDrumDeleteOriginal", ref state.DeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TransposeToDrumDeleteOriginal);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected single-note track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doTransposeSingleNoteTracksToDrumNote"))
            {
                var target = transposeTargets[state.TargetIndex];
                var result = _editorCommandExecutor.Execute(
                    new TransposeSingleNoteTracksToDrumNoteCommand(),
                    CreateEditorCommandContext(),
                    new TransposeSingleNoteTracksToDrumNoteCommandOptions(
                        validIndices,
                        new MidiForgeTransposeToDrumNoteOptions(
                            TargetNote: target.OutputNote,
                            TrackName: state.TrackName,
                            DeleteOriginalTracks: state.DeleteOriginalTracks)));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelTransposeSingleNoteTracksToDrumNote"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class SplitDrumkitPopupState
    {
        public int TransposePresetIndex = 0;
        public bool AutoEditAfterSplit = true;
        public bool CreateRestTrack = true;
        public bool MoveSourceTracksToEnd = true;

        public void Reset()
        {
            TransposePresetIndex = 0;
            AutoEditAfterSplit = true;
            CreateRestTrack = true;
            MoveSourceTracksToEnd = true;
        }
    }

    private sealed class DisassembleDrumkitPopupState
    {
        public bool DeleteOriginalTracks = false;

        public void Reset()
            => DeleteOriginalTracks = false;
    }

    private sealed class TransposeSingleNoteTracksToDrumNotePopupState
    {
        public int PresetIndex = 0;
        public int TargetIndex = 0;
        public string TrackName = GetDefaultTransposeToDrumTrackName();
        public bool DeleteOriginalTracks = true;

        public void Reset()
        {
            PresetIndex = 0;
            TargetIndex = 0;
            TrackName = GetDefaultTransposeToDrumTrackName();
            DeleteOriginalTracks = true;
        }
    }
}
