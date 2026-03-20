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
        DrawMenuView();

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

        ImGui.Separator();

        var hasSelNotes = _selectedEventIndices.Count > 0;
        if (ImGui.MenuItem("Transpose Selected Notes...", default, false, hasSelNotes))
            OpenTransposeNotesPopup();

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

        ImGui.Checkbox("Show Track Panel##ShowTrackPanel", ref _showTrackPanel);
        ImGui.Checkbox("Show Event Panel##ShowEventPanel", ref _showEventPanel);

        ImGui.Separator();
        ImGui.TextDisabled("Preview Piano Roll");

        bool showLeftPanel = _previewState.ShowLeftPanel;
        if (ImGui.Checkbox("Voice Limit Panel##PreviewLeftPanel", ref showLeftPanel))
            _previewState.ShowLeftPanel = showLeftPanel;

        bool showNoteLabel = _previewState.ShowNoteLabel;
        if (ImGui.Checkbox("Note Label##PreviewNoteLabel", ref showNoteLabel))
            _previewState.ShowNoteLabel = showNoteLabel;

        bool showNoteBorder = _previewState.ShowNoteBorder;
        if (ImGui.Checkbox("Note Border##PreviewNoteBorder", ref showNoteBorder))
            _previewState.ShowNoteBorder = showNoteBorder;

        bool showSeconds = _previewState.ShowSeconds;
        if (ImGui.Checkbox("Time Markers##PreviewTimeMarkers", ref showSeconds))
            _previewState.ShowSeconds = showSeconds;

        bool showC3C6 = _previewState.ShowC3C6Range;
        if (ImGui.Checkbox("C3-C6 Markers##PreviewC3C6", ref showC3C6))
            _previewState.ShowC3C6Range = showC3C6;

        using (ImRaii.Disabled(_previewTracks == null || _previewTracks.Length == 0))
        {
            bool showAdapted = _previewTracks != null && _previewTracks.Length > 0 && _previewTracks[0].ShowAdaptedNotes;
            for (int i = 1; showAdapted && _previewTracks != null && i < _previewTracks.Length; i++)
                showAdapted = _previewTracks[i].ShowAdaptedNotes;
            if (ImGui.Checkbox("Show Adapted Notes##PreviewAdapted", ref showAdapted))
            {
                if (_previewTracks != null)
                    foreach (var t in _previewTracks) t.ShowAdaptedNotes = showAdapted;
            }
        }

        ImGui.EndMenu();
    }

    //  Popup open helpers
    private void OpenTransposePopup()
    {
        _transposeSemitones = 0;
        _pendingPopup = "##TransposeTracksPopup";
    }

    private void OpenTransposeNotesPopup()
    {
        _transposeNotesSemitones = 0;
        _pendingPopup = "##TransposeNotesPopup";
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
