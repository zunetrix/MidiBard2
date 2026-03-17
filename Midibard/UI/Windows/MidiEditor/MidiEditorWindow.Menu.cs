using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawMenuBar()
    {
        using var color = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var menuBar = ImRaii.MenuBar();
        if (!menuBar) return;
        DrawMenuFile();
        // DrawMenuEdit();
        DrawMenuTrack();
        // DrawMenuView();

        if (_file?.IsDirty == true)
        {
            var unsavedText = "(unsaved changes)";
            var textSize = ImGui.CalcTextSize(unsavedText);
            var padding = ImGui.GetStyle().FramePadding.X + 5;
            var regionMaxX = ImGui.GetWindowContentRegionMax().X;
            ImGui.SameLine(regionMaxX - textSize.X - (padding * 2));
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Orange))
                ImGui.Text(unsavedText);
        }
    }

    private void DrawMenuFile()
    {
        if (!ImGui.BeginMenu("File")) return;

        if (ImGui.MenuItem("Open..."))
            OpenMidiFileDialog();

        using (ImRaii.Disabled(_file is not { IsDirty: true }))
            if (ImGui.MenuItem("Save"))
                _file?.Save();

        using (ImRaii.Disabled(_file == null))
            if (ImGui.MenuItem("Save As..."))
                SaveAsDialog();

        ImGui.Separator();

        using (ImRaii.Disabled(_file == null))
            if (ImGui.MenuItem("Merge Song..."))
                OpenMergeSongDialog();

        ImGui.EndMenu();
    }

    private void DrawMenuEdit()
    {
        if (!ImGui.BeginMenu("Edit")) return;
        ImGui.Text("Option");
        ImGui.EndMenu();
    }

    private void DrawMenuTrack()
    {
        using var disabled = ImRaii.Disabled(_file == null);
        if (!ImGui.BeginMenu("Track")) return;

        var hasSel = _selectedTrackIndices.Count > 0;
        var selSuffix = hasSel ? $" ({_selectedTrackIndices.Count})" : string.Empty;

        var selNC = _selectedTrackIndices
            .Where(i => i < _file!.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .ToList();
        var hasSelNC = selNC.Count > 0;

        if (ImGui.MenuItem($"Clone Selected Tracks{selSuffix}", default, false, hasSel))
        {
            foreach (var idx in _selectedTrackIndices.OrderByDescending(i => i))
                _file!.CloneTrack(idx);
            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
        }

        var canDelete = hasSel && _selectedTrackIndices.Any(
            i => i < _file!.Tracks.Count && !_file.Tracks[i].IsConductorTrack);

        if (ImGui.MenuItem($"Delete Selected Tracks{selSuffix}", default, false, canDelete))
            DeleteSelectedTracks();

        ImGui.Separator();

        if (ImGui.MenuItem($"Merge Selected Tracks{selSuffix}", default, false, selNC.Count >= 2))
            OpenMergePopup();

        if (ImGui.MenuItem($"Transpose Selected Tracks{selSuffix}...", default, false, hasSelNC))
            OpenTransposePopup();

        if (ImGui.MenuItem($"Quantize Selected Tracks{selSuffix}...", default, false, hasSelNC))
            OpenQuantizePopup();

        // ImGui.Separator();

        // if (ImGui.MenuItem("Consolidate Tempo to Conductor Track"))
        //     _file?.ConsolidateTempoToConductorTrack();

        ImGui.Separator();

        var canSplit = _selectedTrackIndex >= 0
            && _selectedTrackIndex < _file!.Tracks.Count
            && _file.Tracks[_selectedTrackIndex].HasMultipleChannels;

        if (ImGui.MenuItem("Split Selected Track by Channel", default, false, canSplit))
        {
            _file!.SplitTrackByChannel(_selectedTrackIndex);
            if (_selectedTrackIndex >= _file.Tracks.Count)
            {
                _selectedTrackIndex = -1;
                _selectedEventIndices.Clear();
            }
            _selectedTrackIndices.Clear();
        }

        ImGui.EndMenu();
    }

    private void DrawMenuView()
    {
        if (!ImGui.BeginMenu("View")) return;
        ImGui.Text("Option");
        ImGui.EndMenu();
    }

    //  Popup open helpers

    private void OpenTransposePopup()
    {
        _transposeSemitones = 0;
        _pendingPopup = "##TransposeTracksPopup";
    }

    private void OpenMergePopup()
    {
        _mergeTargetRelIdx = 0;
        _pendingPopup = "##MergeTracksPopup";
    }

    private void OpenQuantizePopup()
    {
        _pendingPopup = "##QuantizeTracksPopup";
    }
}
