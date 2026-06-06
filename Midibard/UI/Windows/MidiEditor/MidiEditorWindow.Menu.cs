using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.Track;

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
        DrawMenuEdit();
        DrawMenuTrack();
        DrawMenuDrums();
        DrawMenuForge();
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

        if (ImGui.MenuItem("Open With Options..."))
            OpenImportOptionsPopup();

        if (ImGui.MenuItem("Import From URL..."))
            OpenImportFromUrlPopup();

        if (ImGui.MenuItem("Import Guitar Tab..."))
            OpenGuitarTabDialog();

        ImGui.Separator();

        using (ImRaii.Disabled(_file is not { IsDirty: true } || string.IsNullOrWhiteSpace(_file.FilePath)))
            if (ImGui.MenuItem("Save"))
                SaveMidiFile();

        using (ImRaii.Disabled(_file == null))
            if (ImGui.MenuItem("Save As..."))
                SaveAsDialog();

        using (ImRaii.Disabled(_file == null))
            if (ImGui.MenuItem("Export LRC From MIDI Metadata..."))
                ExportLrcFromMidiMetadataDialog();

        ImGui.Separator();

        using (ImRaii.Disabled(_file == null))
            if (ImGui.MenuItem("Merge Song..."))
                OpenMergeSongPopup();

        ImGui.Separator();

        using (ImRaii.Disabled(_file == null))
            if (ImGui.MenuItem("Close"))
                CloseFile();

        ImGui.EndMenu();
    }

    private void DrawMenuEdit()
    {
        if (!ImGui.BeginMenu("Edit")) return;

        using (ImRaii.Disabled(_file == null || !_history.CanUndo))
        {
            if (ImGui.MenuItem("Undo"))
                UndoMidiEdit();
        }

        using (ImRaii.Disabled(_file == null || !_history.CanRedo))
        {
            if (ImGui.MenuItem("Redo"))
                RedoMidiEdit();
        }

        ImGui.Separator();

        var selectedNoteCount = GetSelectedNoteKeys().Count;
        var hasSelectedEvents = _selectedEventIndices.Count > 0;
        var hasSelNotes = selectedNoteCount > 0;
        var canPasteNotes = _file != null
            && _editorCommandSession.NoteClipboard.HasNotes
            && _selectedTrackIndex >= 0
            && _selectedTrackIndex < _file.Tracks.Count
            && !_file.Tracks[_selectedTrackIndex].IsConductorTrack;
        var hasLoadedTrack = CurrentEvents != null;

        if (ImGui.MenuItem("Select All Notes in Track", default, false, hasLoadedTrack))
            SelectAllNotesInTrack();

        ImGui.Separator();

        if (ImGui.MenuItem($"Copy Selected Notes ({selectedNoteCount})", default, false, hasSelNotes))
            CopySelectedNotes();

        if (ImGui.MenuItem("Paste Notes at Preview Position", default, false, canPasteNotes))
            PasteCopiedNotes();

        ImGui.Separator();

        if (ImGui.MenuItem("Transpose Selected Notes...", default, false, hasSelNotes))
            OpenTransposeNotesPopup();

        if (ImGui.MenuItem("Quantize Selected Notes...", default, false, hasSelNotes))
            OpenQuantizeNotesPopup();

        if (ImGui.MenuItem("Move Selected Notes Left", default, false, hasSelNotes))
            NudgeSelectedNotesByGrid(-1);

        if (ImGui.MenuItem("Move Selected Notes Right", default, false, hasSelNotes))
            NudgeSelectedNotesByGrid(1);

        if (ImGui.MenuItem("Delete Selected Notes", default, false, hasSelNotes))
            DeleteSelectedNotes();

        if (ImGui.MenuItem("Clear Note Selection", default, false, hasSelectedEvents))
            ClearEventSelection();

        if (ImGui.MenuItem("Deselect All", default, false, hasSelectedEvents || _selectedTrackIndex >= 0))
        {
            _selectedEventIndices.Clear();
            _selectedTrackIndex = -1;
            _selectedTrackIndices.Clear();
            _globalEventsChecked = false;
            _globalTracksChecked = false;
            _noteHitList.Clear();
        }

        ImGui.Separator();

        if (ImGui.MenuItem(_pencilModeActive ? "Turn Pencil Mode Off" : "Turn Pencil Mode On"))
            _pencilModeActive = !_pencilModeActive;

        if (ImGui.MenuItem(_previewState.SnapToGrid ? "Turn Snap to Grid Off" : "Turn Snap to Grid On"))
            _previewState.SnapToGrid = !_previewState.SnapToGrid;

        ImGui.Separator();

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

        if (ImGui.MenuItem("Add Blank Track"))
            AddBlankTrackAfterSelection();

        ImGui.Separator();

        if (ImGui.MenuItem($"Clone Selected Tracks{selSuffix}", default, false, hasSelNC))
        {
            var result = _editorCommandExecutor.Execute(
                new CloneTracksCommand(),
                CreateEditorCommandContext(),
                new CloneTracksOptions(selNC));
            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
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

        if (ImGui.MenuItem($"Change Selected Track Note Length{selSuffix}...", default, false, hasSelNC))
            OpenChangeNoteLengthPopup();

        if (ImGui.MenuItem($"Set Selected Track MIDI Program{selSuffix}...", default, false, hasSelNC))
            OpenSetTrackProgramPopup();

        ImGui.Separator();

        if (ImGui.MenuItem($"Map Selected Instruments{selSuffix}...", default, false, hasSelNC))
            OpenMapInstrumentsPopup();

        if (ImGui.MenuItem($"Clear Selected Track Names{selSuffix}", default, false, hasSelNC))
            ClearSelectedTrackNames();

        ImGui.Separator();

        var canSplit = _selectedTrackIndex >= 0
            && _selectedTrackIndex < _file!.Tracks.Count
            && _file.Tracks[_selectedTrackIndex].HasMultipleChannels;

        if (ImGui.MenuItem("Split Selected Track by Channel", default, false, canSplit))
        {
            var result = _editorCommandExecutor.Execute(
                new SplitTrackByChannelCommand(),
                CreateEditorCommandContext(),
                new SplitTrackByChannelOptions(_selectedTrackIndex));
            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Sanitize File..."))
            OpenSanitizePopup();

        ImGui.EndMenu();
    }

    private int[] GetSelectedPerformanceTrackIndices()
        => _file == null
            ? []
            : _selectedTrackIndices
                .Where(i => i >= 0 && i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
                .OrderBy(i => i)
                .ToArray();

    private void AddBlankTrackAfterSelection()
    {
        if (_file == null) return;

        var insertAfter = _selectedTrackIndex >= 0 ? _selectedTrackIndex : (int?)null;
        AddBlankTrackAfter(insertAfter);
    }

    private void AddBlankTrackAfter(int? insertAfter)
    {
        if (_file == null) return;

        var result = _editorCommandExecutor.Execute(
            new CreateBlankTrackCommand(),
            CreateEditorCommandContext(),
            new CreateBlankTrackOptions(insertAfter));
        if (result.Succeeded)
        {
            var createdIndex = result.Result?.Value.CreatedTrackIndices.FirstOrDefault() ?? -1;
            ApplyEditorCommandRefreshHints();
            if (createdIndex >= 0 && createdIndex < _file.Tracks.Count)
                SelectTrack(createdIndex);
        }
    }

    private void ClearSelectedTrackNames()
    {
        if (_file == null) return;

        var selectedIndices = GetSelectedPerformanceTrackIndices();
        var result = _editorCommandExecutor.Execute(
            new ClearTrackNamesCommand(),
            CreateEditorCommandContext(),
            new ClearTrackNamesOptions(selectedIndices));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void DrawMenuForge()
    {
        using var disabled = ImRaii.Disabled(_file == null);
        if (!ImGui.BeginMenu("Forge")) return;

        var selectedPerformanceTracks = _selectedTrackIndices
            .Count(i => i < _file!.Tracks.Count && !_file.Tracks[i].IsConductorTrack);
        var selectedPitchBendTracks = _selectedTrackIndices
            .Count(i => i < _file!.Tracks.Count
                        && !_file.Tracks[i].IsConductorTrack
                        && MidiForgeAnalysis.AnalyzeTrack(_file.Tracks[i]).PitchBendCount > 0);
        var selectedTrackNameTransposeTracks = _selectedTrackIndices
            .Count(i => i >= 0
                        && i < _file!.Tracks.Count
                        && !_file.Tracks[i].IsConductorTrack
                        && TrackInfo.GetTransposeByName(_file.Tracks[i].Name) != 0);
        var suffix = selectedPerformanceTracks > 0 ? $" ({selectedPerformanceTracks})" : string.Empty;
        var pitchBendSuffix = selectedPitchBendTracks > 0 ? $" ({selectedPitchBendTracks})" : string.Empty;
        var trackNameTransposeSuffix = selectedTrackNameTransposeTracks > 0 ? $" ({selectedTrackNameTransposeTracks})" : string.Empty;
        var hasSelNotes = _selectedEventIndices.Count > 0;

        // --- Auto Arrange ---
        if (ImGui.BeginMenu("Auto Arrange"))
        {
            if (ImGui.MenuItem("All Tracks..."))
                OpenPrepareForPlaybackPopup();

            if (ImGui.MenuItem($"Selected Tracks{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenAutoArrangeSelectedPopup();

            if (ImGui.MenuItem($"Fit Only (Selected Tracks){suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenAutoEditPopup();

            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Range / Transpose ---
        if (ImGui.BeginMenu("Range / Transpose"))
        {
            if (ImGui.MenuItem($"Adapt to C3-C6{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenAdaptToRangePopup();

            if (ImGui.MenuItem($"Apply Track-Name Transposes{trackNameTransposeSuffix}...", default, false, selectedTrackNameTransposeTracks > 0))
                OpenApplyTrackNameTransposesPopup();

            ImGui.EndMenu();
        }

        // --- Guitar ---
        if (ImGui.BeginMenu("Guitar"))
        {
            if (ImGui.MenuItem($"Merge Guitar Tone Tracks{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenMergeGuitarToneTracksPopup();

            ImGui.EndMenu();
        }

        // --- Chords ---
        if (ImGui.BeginMenu("Chords"))
        {
            if (ImGui.MenuItem($"Split Chords{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenSplitChordsPopup();

            if (ImGui.MenuItem($"Limit Simultaneous Notes{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenLimitSimultaneousNotesPopup();

            var selectedNotes = _selectedEventIndices.Count;
            var strumSuffix = selectedNotes > 0 ? $" ({selectedNotes} notes)" : suffix;
            if (ImGui.MenuItem($"Strum Notes{strumSuffix}...", default, false, selectedNotes > 0 || selectedPerformanceTracks > 0))
                OpenStrumNotesPopup();

            ImGui.EndMenu();
        }

        // --- Split Notes ---
        if (ImGui.BeginMenu("Split Notes"))
        {
            if (ImGui.MenuItem($"By Tone Range{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenSplitNotesByToneRangePopup();

            if (ImGui.MenuItem($"By Length Range{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenSplitNotesByLengthRangePopup();

            if (ImGui.MenuItem($"Overlapped Notes{suffix}", default, false, selectedPerformanceTracks > 0))
                SplitSelectedOverlappedNotes();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.SplitOverlappedNotes);

            if (ImGui.MenuItem($"Equal Notes{suffix}...", default, false, selectedPerformanceTracks >= 2))
                OpenSplitEqualNotesPopup();

            if (ImGui.MenuItem($"Difference Tracks{suffix}...", default, false, selectedPerformanceTracks >= 2))
                OpenDifferenceTracksPopup();

            if (ImGui.MenuItem($"Into Tracks{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenSplitNotesIntoTracksPopup();

            ImGui.EndMenu();
        }

        // --- Note Duration ---
        if (ImGui.BeginMenu("Note Duration"))
        {
            if (ImGui.MenuItem($"Trim Overlapped Sustained{suffix}", default, false, selectedPerformanceTracks > 0))
                TrimSelectedOverlappedSustainedNotes();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.TrimOverlappedSustainedNotes);

            if (ImGui.MenuItem($"Extend Duration{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenExtendNotesDurationPopup();

            ImGui.EndMenu();
        }

        // --- Pitch Bend ---
        if (ImGui.BeginMenu("Pitch Bend"))
        {
            if (ImGui.MenuItem($"Generate Pitch-Bend Notes{pitchBendSuffix}...", default, false, selectedPitchBendTracks > 0))
                OpenGeneratePitchBendNotesPopup();

            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Selected Notes ---
        if (ImGui.BeginMenu("Selected Notes"))
        {
            if (ImGui.MenuItem("Glue Notes...", default, false, hasSelNotes))
                OpenGlueNotesPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.GlueNotes);

            if (ImGui.MenuItem("Split in Half", default, false, hasSelNotes))
                SplitSelectedNotesInHalf();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.SplitSelectedNotesInHalf);

            if (ImGui.MenuItem("Repeat...", default, false, hasSelNotes))
                OpenRepeatLoopPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.RepeatLoop);

            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Measures ---
        if (ImGui.BeginMenu("Measures"))
        {
            if (ImGui.MenuItem($"Insert Measures{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenInsertMeasuresPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.InsertMeasures);

            if (ImGui.MenuItem($"Delete Measures{suffix}...", default, false, selectedPerformanceTracks > 0))
                OpenDeleteMeasuresPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.DeleteMeasures);

            ImGui.EndMenu();
        }

        ImGui.EndMenu();
    }

    private void DrawMenuDrums()
    {
        using var disabled = ImRaii.Disabled(_file == null);
        if (!ImGui.BeginMenu("Drums")) return;

        var selectedDrumkitTracks = GetSelectedDrumkitTrackIndices().Length;
        var selectedSingleNoteTracks = GetSelectedSingleNoteTrackIndices().Length;
        var drumkitSuffix = selectedDrumkitTracks > 0 ? $" ({selectedDrumkitTracks})" : string.Empty;
        var singleNoteSuffix = selectedSingleNoteTracks > 0 ? $" ({selectedSingleNoteTracks})" : string.Empty;

        if (ImGui.MenuItem($"Split Drumkit Tracks{drumkitSuffix}...", default, false, selectedDrumkitTracks > 0))
            OpenSplitDrumkitPopup();

        if (ImGui.MenuItem($"Disassemble Drumkit Tracks{drumkitSuffix}...", default, false, selectedDrumkitTracks > 0))
            OpenDisassembleDrumkitPopup();

        if (ImGui.BeginMenu("Repair Tools"))
        {
            if (ImGui.MenuItem($"Retarget Single-Note Drum Tracks{singleNoteSuffix}...", default, false, selectedSingleNoteTracks > 0))
                OpenTransposeSingleNoteTracksToDrumNotePopup();

            ImGui.EndMenu();
        }

        ImGui.EndMenu();
    }

    private void DrawMenuView()
    {
        using var menu = ImRaii.Menu("View");
        if (!menu) return;

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

        bool pcMarkers = _previewState.ShowProgramChangeMarkers;
        if (ImGui.Checkbox("Program Change Markers##previewPCMarkers", ref pcMarkers))
            _previewState.ShowProgramChangeMarkers = pcMarkers;

        bool showNotePreview = _previewState.ShowNotePreview;
        if (ImGui.Checkbox("Note Preview##PreviewNotePreview", ref showNotePreview))
            _previewState.ShowNotePreview = showNotePreview;
    }

    //  Popup open helpers
    private void OpenImportOptionsPopup()
    {
        GetImportPopupState().ResetNormalizationDefaults();
        _pendingPopup = "##OpenWithOptionsPopup";
    }

    private void OpenImportFromUrlPopup()
    {
        GetImportPopupState().ResetSourceImportForOpen();
        _pendingPopup = "##ImportFromUrlPopup";
    }

    private void OpenTransposePopup()
    {
        GetTransposePopupState().Reset();
        _pendingPopup = "##TransposeTracksPopup";
    }

    private void OpenTransposeNotesPopup()
    {
        GetTransposeNotesPopupState().Reset();
        _pendingPopup = "##TransposeNotesPopup";
    }

    private void OpenMergePopup()
    {
        GetMergePopupState().ResetTarget();
        _pendingPopup = "##MergeTracksPopup";
    }

    private void OpenQuantizePopup()
    {
        GetQuantizePopupState().NotesOnly = false;
        _pendingPopup = "##QuantizeTracksPopup";
    }

    private void OpenQuantizeNotesPopup()
    {
        GetQuantizePopupState().NotesOnly = true;
        _pendingPopup = "##QuantizeTracksPopup";
    }

    private void OpenChangeNoteLengthPopup()
    {
        GetChangeNoteLengthPopupState().Reset();
        _pendingPopup = "##ChangeNoteLengthPopup";
    }

    private void OpenSetTrackProgramPopup()
    {
        GetSetTrackProgramPopupState().Reset();
        _pendingPopup = "##SetTrackProgramPopup";
    }

    private void OpenMapInstrumentsPopup()
    {
        GetMapInstrumentsPopupState().Reset();
        _pendingPopup = "##MapInstrumentsPopup";
    }

    private void OpenMergeSongPopup()
    {
        GetMergeSongPopupState().ResetForOpen();
        _pendingPopup = "##MergeSongPopup";
    }

    private void OpenSanitizePopup()
    {
        _pendingPopup = "##SanitizePopup";
    }

    private void OpenAdaptToRangePopup()
    {
        GetAdaptToRangePopupState().Reset();
        _pendingPopup = "##AdaptToRangePopup";
    }

    private void OpenPrepareForPlaybackPopup()
    {
        GetPrepareForPlaybackPopupState();
        _pendingPopup = "##PrepareForPlaybackPopup";
    }

    private void OpenAutoArrangeSelectedPopup()
    {
        GetPrepareForPlaybackPopupState();
        _pendingPopup = "##AutoArrangeSelectedPopup";
    }

    private void OpenApplyTrackNameTransposesPopup()
    {
        GetApplyTrackNameTransposesPopupState().Reset();
        _pendingPopup = "##ApplyTrackNameTransposesPopup";
    }

    private void OpenMergeGuitarToneTracksPopup()
    {
        GetMergeGuitarToneTracksPopupState().Reset();
        _pendingPopup = "##MergeGuitarToneTracksPopup";
    }

    private void OpenAutoEditPopup()
    {
        GetAutoEditPopupState();
        _pendingPopup = "##AutoEditPopup";
    }

    private void OpenSplitChordsPopup()
    {
        GetSplitChordsPopupState().Reset();
        _pendingPopup = "##SplitChordsPopup";
    }

    private void OpenLimitSimultaneousNotesPopup()
    {
        GetLimitSimultaneousNotesPopupState().Reset();
        _pendingPopup = "##LimitSimultaneousNotesPopup";
    }

    private void OpenStrumNotesPopup()
    {
        GetStrumNotesPopupState().Reset();
        _pendingPopup = "##StrumNotesPopup";
    }

    private void OpenSplitNotesByToneRangePopup()
    {
        GetSplitToneRangePopupState().Reset();
        _pendingPopup = "##SplitNotesByToneRangePopup";
    }

    private void OpenSplitNotesByLengthRangePopup()
    {
        GetSplitLengthRangePopupState().Reset();
        _pendingPopup = "##SplitNotesByLengthRangePopup";
    }

    private void OpenExtendNotesDurationPopup()
    {
        GetExtendNotesDurationPopupState().Reset();
        _pendingPopup = "##ExtendNotesDurationPopup";
    }

    private void OpenSplitEqualNotesPopup()
    {
        GetSplitEqualNotesPopupState().Reset();
        _pendingPopup = "##SplitEqualNotesPopup";
    }

    private void OpenDifferenceTracksPopup()
    {
        GetDifferenceTracksPopupState().Reset();
        _pendingPopup = "##DifferenceTracksPopup";
    }

    private void OpenSplitNotesIntoTracksPopup()
    {
        GetSplitNotesIntoTracksPopupState().Reset();
        _pendingPopup = "##SplitNotesIntoTracksPopup";
    }

    private void OpenGeneratePitchBendNotesPopup()
    {
        GetGeneratePitchBendNotesPopupState().Reset();
        _pendingPopup = "##GeneratePitchBendNotesPopup";
    }

    private void OpenSplitDrumkitPopup()
    {
        GetSplitDrumkitPopupState().Reset();
        _pendingPopup = "##SplitDrumkitPopup";
    }

    private void OpenDisassembleDrumkitPopup()
    {
        GetDisassembleDrumkitPopupState().Reset();
        _pendingPopup = "##DisassembleDrumkitPopup";
    }

    private void OpenTransposeSingleNoteTracksToDrumNotePopup()
    {
        GetTransposeSingleNoteTracksToDrumNotePopupState().Reset();
        _pendingPopup = "##TransposeSingleNoteTracksToDrumNotePopup";
    }

    private void OpenGlueNotesPopup()
    {
        GetGlueNotesPopupState();
        _pendingPopup = "##GlueNotesPopup";
    }

    private void OpenRepeatLoopPopup()
    {
        GetRepeatLoopPopupState();
        _pendingPopup = "##RepeatLoopPopup";
    }

    private void OpenInsertMeasuresPopup()
    {
        GetInsertMeasuresPopupState();
        _pendingPopup = "##InsertMeasuresPopup";
    }

    private void OpenDeleteMeasuresPopup()
    {
        GetDeleteMeasuresPopupState();
        _pendingPopup = "##DeleteMeasuresPopup";
    }
}
