using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;

namespace MidiBard;

public partial class MidiEditorWindow
{

    // Transpose Popup
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
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.Transpose);

        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Semitones##transpSemi", ref _transposeSemitones, 12, 12);

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Min note##transposeMinNote", ref _transposeMinNoteNumber);
        _transposeMinNoteNumber = Math.Clamp(_transposeMinNoteNumber, 0, 127);

        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Max note##transposeMaxNote", ref _transposeMaxNoteNumber);
        _transposeMaxNoteNumber = Math.Clamp(_transposeMaxNoteNumber, 0, 127);
        if (_transposeMinNoteNumber > _transposeMaxNoteNumber)
            (_transposeMinNoteNumber, _transposeMaxNoteNumber) = (_transposeMaxNoteNumber, _transposeMinNoteNumber);

        ImGui.Checkbox("Create transposed tracks (keep originals)##transposeCreateNew", ref _transposeCreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TransposeCreateNew);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doTranspose"))
        {
            if (_transposeSemitones != 0)
                CaptureHistorySnapshot();

            bool needsReload = _selectedTrackIndex >= 0
                && _selectedTrackIndex < _file.Tracks.Count
                && _selectedTrackIndices.Contains(_selectedTrackIndex)
                && !_transposeCreateNewTracks;

            _file.TransposeTracks(
                _selectedTrackIndices,
                _transposeSemitones,
                _transposeMinNoteNumber,
                _transposeMaxNoteNumber,
                _transposeCreateNewTracks);

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
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.Merge);

        ImGui.Checkbox("Include Program Change events", ref _mergeIncludePC);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeEvents);
        ImGui.Checkbox("Include Pitch Bend events", ref _mergeIncludePB);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeEvents);
        ImGui.Checkbox("Include Control Change events", ref _mergeIncludeCC);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeEvents);
        ImGui.Checkbox("Remove duplicate equal notes", ref _mergeRemoveEqualNotes);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Removes duplicate notes with the same MIDI note number and start tick.");
        ImGui.Checkbox("Delete original tracks after merge", ref _mergeDeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeDeleteOriginal);
        ImGui.Spacing();
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Note merge tolerance (ms)##mergeTolerance", ref _mergeToleranceMs, 10, 100);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When > 0 overlapping or adjacent same-pitch notes are merged\ninto a single longer note using DryWetMidi's native merger.");
        _mergeToleranceMs = Math.Max(0, _mergeToleranceMs);

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
                CaptureHistorySnapshot();
                _file.MergeTracks(
                    targetIdx,
                    validIndices,
                    includeProgramChange: _mergeIncludePC,
                    includePitchBend: _mergeIncludePB,
                    includeControlChange: _mergeIncludeCC,
                    toleranceMs: _mergeToleranceMs,
                    removeEqualNotes: _mergeRemoveEqualNotes,
                    deleteOriginalTracks: _mergeDeleteOriginalTracks);
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
    private static readonly string[] QuantizeStepLabels =
        { "1/4 Note", "1/8 Note", "1/16 Note", "1/32 Note", "1/64 Note" };

    private static readonly string[] QuantizeTargetLabels = { "Start", "End", "Start & End" };
    private static readonly QuantizerTarget[] QuantizeTargetValues =
        { QuantizerTarget.Start, QuantizerTarget.End, QuantizerTarget.Start | QuantizerTarget.End };

    private void DrawQuantizePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##QuantizeTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        ImGui.Text(_quantizeNotesOnly ? "Quantize Selected Notes" : "Quantize Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.Quantize);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Grid##quantStep", ref _quantizeStepIndex,
            QuantizeStepLabels, QuantizeStepLabels.Length);

        // Target: Start / End / Both
        int targetIdx = Array.IndexOf(QuantizeTargetValues, _quantizeTarget);
        if (targetIdx < 0) targetIdx = 0;
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("Target##quantTarget", ref targetIdx, QuantizeTargetLabels, QuantizeTargetLabels.Length))
            _quantizeTarget = QuantizeTargetValues[targetIdx];

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("Strength##quantLevel", ref _quantizeLevel, 0f, 1f, "%.2f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("1.0 = fully snapped to grid, 0.5 = halfway, 0.0 = no change.");

        ImGui.Checkbox("Preserve note length##quantFixEnd", ref _quantizeFixOppositeEnd);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When quantizing Start, moves the NoteOff by the same delta so duration is preserved.");

        if (!_quantizeNotesOnly)
        {
            ImGui.Checkbox("Create new quantized track (keep original)", ref _quantizeToNewTrack);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doQuantize"))
        {
            var grid = BuildQuantizeGrid();
            var settings = new QuantizingSettings
            {
                Target = _quantizeTarget,
                QuantizingLevel = _quantizeLevel,
                FixOppositeEnd = _quantizeFixOppositeEnd,
                QuantizingBeyondZeroPolicy = QuantizingBeyondZeroPolicy.FixAtZero,
                QuantizingBeyondFixedEndPolicy = QuantizingBeyondFixedEndPolicy.CollapseAndFix,
            };

            if (_quantizeNotesOnly)
            {
                if (_selectedTrackIndex >= 0)
                {
                    var keys = MidiEditorSelectionKeys.FromSelectedEvents(CurrentEvents, _selectedEventIndices);
                    if (keys.Count > 0)
                    {
                        var pendingHistory = _history.BeginPendingCapture(_file);
                        var changed = _file.QuantizeNotes(_selectedTrackIndex, keys, grid, settings);
                        if (changed && _history.CommitPendingCapture(_file, pendingHistory))
                        {
                            _file.Tracks[_selectedTrackIndex].LoadEvents(_file.TempoMap);
                            _selectedEventIndices.Clear();
                            _globalEventsChecked = false;
                        }
                    }
                }
            }
            else
            {
                bool needsReload = !_quantizeToNewTrack
                    && _selectedTrackIndex >= 0
                    && _selectedTrackIndex < _file.Tracks.Count
                    && _selectedTrackIndices.Contains(_selectedTrackIndex);

                var pendingHistory = _history.BeginPendingCapture(_file);
                var changedTracks = _file.QuantizeTracks(_selectedTrackIndices, grid, settings, _quantizeToNewTrack);

                if (changedTracks > 0 && _history.CommitPendingCapture(_file, pendingHistory))
                {
                    if (needsReload)
                    {
                        _file.Tracks[_selectedTrackIndex].LoadEvents(_file.TempoMap);
                        _selectedEventIndices.Clear();
                        _globalEventsChecked = false;
                    }

                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                }
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelQuantize"))
            ImGui.CloseCurrentPopup();
    }

    private IGrid BuildQuantizeGrid()
    {
        ITimeSpan[] steps =
        {
            MusicalTimeSpan.Quarter,
            MusicalTimeSpan.Eighth,
            MusicalTimeSpan.Sixteenth,
            MusicalTimeSpan.ThirtySecond,
            MusicalTimeSpan.SixtyFourth,
        };
        return new SteppedGrid(steps[Math.Clamp(_quantizeStepIndex, 0, steps.Length - 1)]);
    }

    //  Change Note Length Popup
    private void DrawChangeNoteLengthPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##ChangeNoteLengthPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Change Selected Track Note Length");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ChangeNoteLength);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Min length ticks##changeLengthMin", ref _changeNoteLengthMinTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChangeNoteLengthRange);
        _changeNoteLengthMinTicks = Math.Max(0, _changeNoteLengthMinTicks);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Max length ticks##changeLengthMax", ref _changeNoteLengthMaxTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChangeNoteLengthRange);
        _changeNoteLengthMaxTicks = Math.Max(0, _changeNoteLengthMaxTicks);
        if (_changeNoteLengthMinTicks > _changeNoteLengthMaxTicks)
            (_changeNoteLengthMinTicks, _changeNoteLengthMaxTicks) = (_changeNoteLengthMaxTicks, _changeNoteLengthMinTicks);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("New length ticks##changeLengthNew", ref _changeNoteLengthNewTicks);
        _changeNoteLengthNewTicks = Math.Max(1, _changeNoteLengthNewTicks);

        if (ImGui.SmallButton("x2##changeLengthNewDouble"))
            _changeNoteLengthNewTicks = Math.Max(1, _changeNoteLengthNewTicks * 2);
        ImGui.SameLine();
        if (ImGui.SmallButton("/2##changeLengthNewHalf"))
            _changeNoteLengthNewTicks = Math.Max(1, _changeNoteLengthNewTicks / 2);

        ImGui.Checkbox("Delete original tracks after change length##changeLengthDeleteOriginal", ref _changeNoteLengthDeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChangeNoteLengthDeleteOriginal);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doChangeNoteLength"))
            {
                var result = _editorCommandExecutor.Execute(
                    new ChangeTrackNoteLengthsCommand(),
                    CreateEditorCommandContext(),
                    new ChangeTrackNoteLengthsCommandOptions(
                        validIndices,
                        new MidiForgeChangeNoteLengthOptions(
                            MinimumLengthTicks: _changeNoteLengthMinTicks,
                            MaximumLengthTicks: _changeNoteLengthMaxTicks,
                            NewLengthTicks: _changeNoteLengthNewTicks,
                            DeleteOriginalTracks: _changeNoteLengthDeleteOriginalTracks)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelChangeNoteLength"))
            ImGui.CloseCurrentPopup();
    }

    //  Set Track Program Popup
    private void DrawSetTrackProgramPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SetTrackProgramPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        _setTrackProgramNumber = Math.Clamp(_setTrackProgramNumber, 0, 127);
        var preview = GmProgramComboItems[_setTrackProgramNumber];

        ImGui.Text("Set Selected Track MIDI Program");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SetTrackProgram);

        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Program##setTrackProgramCombo", preview))
        {
            for (int i = 0; i < GmProgramComboItems.Length; i++)
            {
                var selected = i == _setTrackProgramNumber;
                if (ImGui.Selectable(GmProgramComboItems[i], selected))
                    _setTrackProgramNumber = i;
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.Checkbox("Replace all existing Program Change events##setTrackProgramReplaceAll", ref _setTrackProgramReplaceAll);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When off, only the earliest Program Change event is updated. Tracks without one get a new event at tick 0.");

        ImGui.Checkbox("Rename tracks from selected program##setTrackProgramRename", ref _setTrackProgramRenameTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SetTrackProgramRename);
        using (ImRaii.Disabled(!_setTrackProgramRenameTracks))
        {
            ImGui.RadioButton("FFXIV instrument name##setTrackProgramRenameFfxiv", ref _setTrackProgramRenameModeIndex, 0);
            ImGui.SameLine();
            ImGui.RadioButton("MIDI program name##setTrackProgramRenameMidi", ref _setTrackProgramRenameModeIndex, 1);
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSetTrackProgram"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SetTrackProgramsCommand(),
                    CreateEditorCommandContext(),
                    new SetTrackProgramsCommandOptions(
                        validIndices,
                        new MidiForgeSetTrackProgramOptions(
                        ProgramNumber: _setTrackProgramNumber,
                        ReplaceAllProgramChanges: _setTrackProgramReplaceAll,
                        RenameTracks: _setTrackProgramRenameTracks,
                        RenameMode: _setTrackProgramRenameModeIndex == 0
                            ? MidiForgeTrackNameFillMode.Ffxiv
                            : MidiForgeTrackNameFillMode.Midi)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSetTrackProgram"))
            ImGui.CloseCurrentPopup();
    }

    //  Merge Song Popup
    private void DrawMergeSongPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##MergeSongPopup");
        if (!popup) return;
        if (_file == null) return;

        ImGui.Text("Merge Song");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("How to place the imported file:");
        ImGui.RadioButton("Simultaneously (overlay tracks at time 0)##mergeSongSim", ref _mergeSongMode, 0);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("All tracks from both files start at time 0.\nUse when the two files play together (ensemble parts).");
        ImGui.RadioButton("Sequentially (append after this file)##mergeSongSeq", ref _mergeSongMode, 1);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The imported file is placed after the current file ends.\nUse for medleys or song sections.");

        if (_mergeSongMode == 0)
        {
            ImGui.Spacing();
            ImGui.Checkbox("Ignore different tempo maps##mergeSongIgnoreTempo", ref _mergeSongIgnoreDifferentTempo);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, uses this file's tempo map and ignores the imported file's tempo.\nRequired when the two files have different BPM/time signatures.");
            if (!_mergeSongIgnoreDifferentTempo)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Orange))
                    ImGui.TextWrapped("Warning: both files must share an identical tempo map or an error will occur.");
            }
        }

        if (_mergeSongMode == 1)
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(130f * ImGuiHelpers.GlobalScale);
            ImGui.InputInt("Delay between files (ms)##mergeSongDelay", ref _mergeSongDelayMs, 100, 1000);
            _mergeSongDelayMs = Math.Max(0, _mergeSongDelayMs);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Open File...##mergeSongOpen"))
        {
            _mergeSongSequential = _mergeSongMode == 1;
            ImGui.CloseCurrentPopup();
            OpenMergeSongDialog();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelMergeSong"))
            ImGui.CloseCurrentPopup();
    }

    //  Sanitize Popup
    private void DrawSanitizePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SanitizePopup");
        if (!popup) return;
        if (_file == null) return;

        ImGui.Text("Sanitize MIDI File");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Checkbox("Remove duplicated notes##sanDuplNotes", ref _sanitizeRemoveDuplNotes);
        ImGui.Checkbox("Remove empty track chunks##sanEmptyTracks", ref _sanitizeRemoveEmptyTracks);
        ImGui.Checkbox("Remove orphaned Note Off events##sanOrphanOff", ref _sanitizeRemoveOrphanedNoteOff);

        ImGui.Spacing();
        string[] orphanOnLabels = { "Remove", "Ignore", "Complete note (use max length)" };
        int onPolicyIdx = (int)_sanitizeOrphanedNoteOnPolicy;
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("Orphaned Note On##sanOrphanOn", ref onPolicyIdx, orphanOnLabels, orphanOnLabels.Length))
            _sanitizeOrphanedNoteOnPolicy = (OrphanedNoteOnEventsPolicy)onPolicyIdx;

        ImGui.Checkbox("Remove duplicate Set Tempo events##sanDuplTempo", ref _sanitizeRemoveDuplTempo);
        ImGui.Checkbox("Remove duplicate Time Signature events##sanDuplTimeSig", ref _sanitizeRemoveDuplTimeSig);
        ImGui.Checkbox("Trim silence at start##sanTrim", ref _sanitizeTrim);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doSanitize"))
        {
            var settings = new SanitizingSettings
            {
                RemoveDuplicatedNotes = _sanitizeRemoveDuplNotes,
                RemoveEmptyTrackChunks = _sanitizeRemoveEmptyTracks,
                RemoveOrphanedNoteOffEvents = _sanitizeRemoveOrphanedNoteOff,
                OrphanedNoteOnEventsPolicy = _sanitizeOrphanedNoteOnPolicy,
                RemoveDuplicatedSetTempoEvents = _sanitizeRemoveDuplTempo,
                RemoveDuplicatedTimeSignatureEvents = _sanitizeRemoveDuplTimeSig,
                Trim = _sanitizeTrim,
            };
            CaptureHistorySnapshot();
            _file.SanitizeFile(settings);
            SelectTrack(-1);
            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
            _globalEventsChecked = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSanitize"))
            ImGui.CloseCurrentPopup();
    }

    //  Transpose Notes Popup
    private void DrawTransposeNotesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##TransposeNotesPopup");
        if (!popup) return;

        ImGui.Text("Transpose Selected Notes");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Semitones##transpNotesSemi", ref _transposeNotesSemitones, 12, 12);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doTransposeNotes"))
        {
            TransposeSelectedNotes(_transposeNotesSemitones);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelTransposeNotes"))
            ImGui.CloseCurrentPopup();
    }
}
