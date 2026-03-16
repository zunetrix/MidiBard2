using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Core;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private static readonly string[] QuantizeStepLabels =
        { "1/4 Note", "1/8 Note", "1/16 Note", "1/32 Note", "1/64 Note" };

    //  Transpose Popup

    private void DrawTransposePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##TransposeTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        ImGui.Text("Transpose Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Semitones##transpSemi", ref _transposeSemitones);
        if (_transposeSemitones < -48) _transposeSemitones = -48;
        if (_transposeSemitones > 48) _transposeSemitones = 48;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doTranspose"))
        {
            bool needsReload = _selectedTrackIndex >= 0
                && _selectedTrackIndex < _file.Tracks.Count
                && _selectedTrackIndices.Contains(_selectedTrackIndex);

            _file.TransposeTracks(_selectedTrackIndices, _transposeSemitones);

            if (needsReload)
            {
                _file.Tracks[_selectedTrackIndex].LoadEvents(_file.TempoMap);
                _selectedEventIndices.Clear();
                _globalEventsChecked = false;
            }

            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelTranspose"))
            ImGui.CloseCurrentPopup();
    }

    //  Merge Popup

    private void DrawMergePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##MergeTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        ImGui.Text("Merge Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Checkbox("Include Program Change events", ref _mergeIncludePC);
        ImGui.Checkbox("Include Pitch Bend events", ref _mergeIncludePB);

        ImGui.Spacing();
        ImGui.Text("Target track (merge INTO this track's clone):");

        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToList();

        if (_mergeTargetRelIdx >= validIndices.Count)
            _mergeTargetRelIdx = 0;

        for (int r = 0; r < validIndices.Count; r++)
        {
            var track = _file.Tracks[validIndices[r]];
            bool sel = _mergeTargetRelIdx == r;
            if (ImGui.RadioButton($"{track.DisplayName}##mergeTarget_{r}", sel))
                _mergeTargetRelIdx = r;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool canMerge = validIndices.Count >= 2;
        using (ImRaii.Disabled(!canMerge))
        {
            if (ImGuiUtil.SuccessButton("Merge##doMerge"))
            {
                var targetIdx = validIndices[_mergeTargetRelIdx];
                _file.MergeTracks(targetIdx, validIndices, _mergeIncludePC, _mergeIncludePB);
                SelectTrack(-1);
                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelMerge"))
            ImGui.CloseCurrentPopup();
    }

    //  Quantize Popup

    private void DrawQuantizePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##QuantizeTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        ImGui.Text("Quantize Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Quantize to##quantStep", ref _quantizeStepIndex,
            QuantizeStepLabels, QuantizeStepLabels.Length);

        ImGui.Checkbox("Create new quantized track (keep original)", ref _quantizeToNewTrack);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doQuantize"))
        {
            var ppq = _file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision td
                ? td.TicksPerQuarterNote : 480;
            long quantTicks = System.Math.Max(1, ppq >> _quantizeStepIndex);

            bool needsReload = !_quantizeToNewTrack
                && _selectedTrackIndex >= 0
                && _selectedTrackIndex < _file.Tracks.Count
                && _selectedTrackIndices.Contains(_selectedTrackIndex);

            _file.QuantizeTracks(_selectedTrackIndices, quantTicks, _quantizeToNewTrack);

            if (needsReload)
            {
                _file.Tracks[_selectedTrackIndex].LoadEvents(_file.TempoMap);
                _selectedEventIndices.Clear();
                _globalEventsChecked = false;
            }

            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelQuantize"))
            ImGui.CloseCurrentPopup();
    }
}
