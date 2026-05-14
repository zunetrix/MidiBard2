using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
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

        using (ImRaii.Disabled(_file is not { IsDirty: true } || string.IsNullOrWhiteSpace(_file.FilePath)))
            if (ImGui.MenuItem("Save"))
                _file?.Save();

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

        var hasSelNotes = _selectedEventIndices.Count > 0;
        if (ImGui.MenuItem("Transpose Selected Notes...", default, false, hasSelNotes))
            OpenTransposeNotesPopup();

        if (ImGui.MenuItem("Quantize Selected Notes...", default, false, hasSelNotes))
            OpenQuantizeNotesPopup();

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

        if (ImGui.MenuItem($"Clone Selected Tracks{selSuffix}", default, false, hasSelNC))
        {
            CaptureHistorySnapshot();
            foreach (var idx in selNC.OrderByDescending(i => i))
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

        if (ImGui.MenuItem($"Change Selected Track Note Length{selSuffix}...", default, false, hasSelNC))
            OpenChangeNoteLengthPopup();

        if (ImGui.MenuItem($"Set Selected Track MIDI Program{selSuffix}...", default, false, hasSelNC))
            OpenSetTrackProgramPopup();

        ImGui.Separator();

        if (ImGui.MenuItem($"Auto-Fill Empty Selected Names{selSuffix}", default, false, hasSelNC))
            FillSelectedEmptyTrackNames(MidiForgeTrackNameFillMode.Ffxiv);

        if (ImGui.MenuItem($"Auto-Fill Empty Selected Names (MIDI){selSuffix}", default, false, hasSelNC))
            FillSelectedEmptyTrackNames(MidiForgeTrackNameFillMode.Midi);

        if (ImGui.MenuItem($"Clear Selected Track Names{selSuffix}", default, false, hasSelNC))
            ClearSelectedTrackNames();

        ImGui.Separator();

        var canSplit = _selectedTrackIndex >= 0
            && _selectedTrackIndex < _file!.Tracks.Count
            && _file.Tracks[_selectedTrackIndex].HasMultipleChannels;

        if (ImGui.MenuItem("Split Selected Track by Channel", default, false, canSplit))
        {
            CaptureHistorySnapshot();
            _file!.SplitTrackByChannel(_selectedTrackIndex);
            if (_selectedTrackIndex >= _file.Tracks.Count)
            {
                _selectedTrackIndex = -1;
                _selectedEventIndices.Clear();
            }
            _selectedTrackIndices.Clear();
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

    private void FillSelectedEmptyTrackNames(MidiForgeTrackNameFillMode fillMode)
    {
        if (_file == null) return;

        var selectedIndices = GetSelectedPerformanceTrackIndices();
        var result = _editorCommandExecutor.Execute(
            new FillEmptyTrackNamesCommand(),
            CreateEditorCommandContext(),
            new FillEmptyTrackNamesOptions(selectedIndices, fillMode));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
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

        if (ImGui.MenuItem("Quick Prepare Whole File for Playback", default, false, _file != null))
            QuickPrepareForPlayback();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.QuickPrepareForPlayback);

        if (ImGui.MenuItem("Prepare Whole File for Playback...", default, false, _file != null))
            OpenPrepareForPlaybackPopup();

        ImGui.Separator();

        if (ImGui.MenuItem($"Adapt Selected Tracks to C3-C6{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenAdaptToRangePopup();

        if (ImGui.MenuItem($"Apply Track-Name Transposes{trackNameTransposeSuffix}...", default, false, selectedTrackNameTransposeTracks > 0))
            OpenApplyTrackNameTransposesPopup();

        if (ImGui.MenuItem($"Merge Guitar Tone Tracks{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenMergeGuitarToneTracksPopup();

        if (ImGui.MenuItem($"Auto Edit{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenAutoEditPopup();

        if (ImGui.MenuItem($"Split Chords{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenSplitChordsPopup();

        if (ImGui.MenuItem($"Split Notes by Tone Range{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenSplitNotesByToneRangePopup();

        if (ImGui.MenuItem($"Split Notes by Length Range{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenSplitNotesByLengthRangePopup();

        if (ImGui.MenuItem($"Split Overlapped Notes{suffix}", default, false, selectedPerformanceTracks > 0))
            SplitSelectedOverlappedNotes();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.SplitOverlappedNotes);

        if (ImGui.MenuItem($"Trim Overlapped Sustained Notes{suffix}", default, false, selectedPerformanceTracks > 0))
            TrimSelectedOverlappedSustainedNotes();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.TrimOverlappedSustainedNotes);

        if (ImGui.MenuItem($"Extend Notes Duration{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenExtendNotesDurationPopup();

        if (ImGui.MenuItem($"Split Equal Notes{suffix}...", default, false, selectedPerformanceTracks >= 2))
            OpenSplitEqualNotesPopup();

        if (ImGui.MenuItem($"Difference Tracks{suffix}...", default, false, selectedPerformanceTracks >= 2))
            OpenDifferenceTracksPopup();

        if (ImGui.MenuItem($"Split Notes Into Tracks{suffix}...", default, false, selectedPerformanceTracks > 0))
            OpenSplitNotesIntoTracksPopup();

        if (ImGui.MenuItem($"Generate Pitch-Bend Notes{pitchBendSuffix}...", default, false, selectedPitchBendTracks > 0))
            OpenGeneratePitchBendNotesPopup();

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

        if (ImGui.MenuItem($"Transpose Single-Note Tracks to Drum Note{singleNoteSuffix}...", default, false, selectedSingleNoteTracks > 0))
            OpenTransposeSingleNoteTracksToDrumNotePopup();

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
    }

    //  Popup open helpers
    private void OpenImportOptionsPopup()
    {
        _importSplitTracksByChannel = false;
        _importSortTracks = false;
        _importOverwriteTrackNames = false;
        _importRemoveNonLyricMetadata = true;
        _importRemoveLyricsAndText = false;
        _importRemoveSequencerSpecificEvents = true;
        _importOptimizeChannels = false;
        _importTrimStartModeIndex = 0;
        _pendingPopup = "##OpenWithOptionsPopup";
    }

    private void OpenImportFromUrlPopup()
    {
        _sourceImportUrl = string.Empty;
        _sourceImportError = string.Empty;
        _sourceImportStatus = string.Empty;
        _sourceImportClosePopup = false;
        _importSplitTracksByChannel = false;
        _importSortTracks = false;
        _importOverwriteTrackNames = false;
        _importRemoveNonLyricMetadata = true;
        _importRemoveLyricsAndText = false;
        _importRemoveSequencerSpecificEvents = true;
        _importOptimizeChannels = false;
        _importTrimStartModeIndex = 0;
        _pendingPopup = "##ImportFromUrlPopup";
    }

    private void OpenTransposePopup()
    {
        _transposeSemitones = 0;
        _transposeMinNoteNumber = 0;
        _transposeMaxNoteNumber = 127;
        _transposeCreateNewTracks = false;
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
        _quantizeNotesOnly = false;
        _pendingPopup = "##QuantizeTracksPopup";
    }

    private void OpenQuantizeNotesPopup()
    {
        _quantizeNotesOnly = true;
        _pendingPopup = "##QuantizeTracksPopup";
    }

    private void OpenChangeNoteLengthPopup()
    {
        _changeNoteLengthMinTicks = 0;
        _changeNoteLengthMaxTicks = 0;
        _changeNoteLengthNewTicks = 240;
        _changeNoteLengthDeleteOriginalTracks = false;
        _pendingPopup = "##ChangeNoteLengthPopup";
    }

    private void OpenSetTrackProgramPopup()
    {
        _setTrackProgramNumber = 0;
        _setTrackProgramReplaceAll = true;
        _setTrackProgramRenameTracks = true;
        _setTrackProgramRenameModeIndex = 0;
        _pendingPopup = "##SetTrackProgramPopup";
    }

    private void OpenMergeSongPopup()
    {
        _mergeSongMode = 0;
        _mergeSongDelayMs = 0;
        _pendingPopup = "##MergeSongPopup";
    }

    private void OpenSanitizePopup()
    {
        _pendingPopup = "##SanitizePopup";
    }

    private void OpenAdaptToRangePopup()
    {
        _adaptToRangeCreateNewTracks = true;
        _adaptToRangeStrategyIndex = GetRangeFitStrategyIndex(MidiForgeRangeFitStrategy.BestOctaveFit);
        _pendingPopup = "##AdaptToRangePopup";
    }

    private void OpenPrepareForPlaybackPopup()
    {
        GetPrepareForPlaybackPopupState();
        _pendingPopup = "##PrepareForPlaybackPopup";
    }

    private void QuickPrepareForPlayback()
    {
        if (_file == null)
            return;

        var result = _editorCommandExecutor.Execute(
            new PrepareForPlaybackConservativeCommand(),
            CreateEditorCommandContext(),
            new EditorOperationEmptyOptions());

        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void OpenApplyTrackNameTransposesPopup()
    {
        _applyTrackNameTransposeCreateNewTracks = false;
        _pendingPopup = "##ApplyTrackNameTransposesPopup";
    }

    private void OpenMergeGuitarToneTracksPopup()
    {
        _mergeGuitarToneDeleteOriginalTracks = false;
        _pendingPopup = "##MergeGuitarToneTracksPopup";
    }

    private void OpenAutoEditPopup()
    {
        GetAutoEditPopupState();
        _pendingPopup = "##AutoEditPopup";
    }

    private void OpenSplitChordsPopup()
    {
        _splitChordsStrategyIndex = 0;
        _splitChordsGroupModeIndex = 0;
        _splitChordsMinimumSimultaneousNotes = 2;
        _splitChordsInsertPartsAtEnd = true;
        _pendingPopup = "##SplitChordsPopup";
    }

    private void OpenSplitNotesByToneRangePopup()
    {
        _splitToneMinNote = MidiForgeAnalysis.PlayableLowestMidiNote;
        _splitToneMaxNote = MidiForgeAnalysis.PlayableHighestMidiNote;
        _pendingPopup = "##SplitNotesByToneRangePopup";
    }

    private void OpenSplitNotesByLengthRangePopup()
    {
        _splitLengthMinTicks = 0;
        _splitLengthMaxTicks = 0;
        _pendingPopup = "##SplitNotesByLengthRangePopup";
    }

    private void OpenExtendNotesDurationPopup()
    {
        _extendNotesMaximumDurationTicks = 0;
        _extendNotesRespectEmptyMeasures = true;
        _pendingPopup = "##ExtendNotesDurationPopup";
    }

    private void OpenSplitEqualNotesPopup()
    {
        _splitEqualNotesTargetRelIdx = 0;
        _pendingPopup = "##SplitEqualNotesPopup";
    }

    private void OpenDifferenceTracksPopup()
    {
        _differenceTracksTargetRelIdx = 0;
        _pendingPopup = "##DifferenceTracksPopup";
    }

    private void OpenSplitNotesIntoTracksPopup()
    {
        _splitIntoTracksNumberOfTracks = 2;
        _splitIntoTracksEveryNotesAmount = 1;
        _pendingPopup = "##SplitNotesIntoTracksPopup";
    }

    private void OpenGeneratePitchBendNotesPopup()
    {
        _generatePitchBendDeleteOriginalTracks = false;
        _pendingPopup = "##GeneratePitchBendNotesPopup";
    }

    private void OpenSplitDrumkitPopup()
    {
        _splitDrumkitTransposePresetIndex = 0;
        _splitDrumkitAutoEditAfterSplit = true;
        _splitDrumkitCreateRestTrack = true;
        _splitDrumkitMoveSourceTracksToEnd = true;
        _pendingPopup = "##SplitDrumkitPopup";
    }

    private void OpenDisassembleDrumkitPopup()
    {
        _disassembleDrumkitDeleteOriginalTracks = false;
        _pendingPopup = "##DisassembleDrumkitPopup";
    }

    private void OpenTransposeSingleNoteTracksToDrumNotePopup()
    {
        _transposeToDrumPresetIndex = 0;
        _transposeToDrumTargetIndex = 0;
        _transposeToDrumTrackName = MidiForgeDrumMaps.GetTransposeTargets(MidiForgeDrumTransposePreset.Default)[0].Category;
        _transposeToDrumDeleteOriginalTracks = true;
        _pendingPopup = "##TransposeSingleNoteTracksToDrumNotePopup";
    }
}
