using System;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const string TransposePopupStateKey = "track.transpose.popup";
    private const string TransposeNotesPopupStateKey = "note.transpose-selected.popup";
    private const string MergePopupStateKey = "track.merge.popup";
    private const string QuantizePopupStateKey = "track.quantize.popup";
    private const string MergeSongPopupStateKey = "track.merge-song.popup";
    private const string SanitizePopupStateKey = "track.sanitize.popup";
    private const string ChangeNoteLengthPopupStateKey = "track.change-note-length.popup";
    private const string SetTrackProgramPopupStateKey = "track.set-track-program.popup";
    private const string MapInstrumentsPopupStateKey = "track.map-instruments.popup";

    private static readonly string[] MapInstrumentsModeLabels =
    [
        "Empty names only",
        "Empty or generic names only",
        "Replace selected names",
    ];

    private static readonly string[] MapInstrumentsNameSourceLabels =
    [
        "Game instrument map",
        "MIDI program names",
    ];

    private static readonly string[] NoteNames = Enumerable.Range(0, 128)
        .Select(i => $"{i} ({MidiForgeNotePrimitives.GetMidiNoteName(i)})")
        .ToArray();

    private TransposePopupState GetTransposePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            TransposePopupStateKey,
            static () => new TransposePopupState());

    private TransposeNotesPopupState GetTransposeNotesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            TransposeNotesPopupStateKey,
            static () => new TransposeNotesPopupState());

    private MergePopupState GetMergePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            MergePopupStateKey,
            static () => new MergePopupState());

    private QuantizePopupState GetQuantizePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            QuantizePopupStateKey,
            static () => new QuantizePopupState());

    private MergeSongPopupState GetMergeSongPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            MergeSongPopupStateKey,
            static () => new MergeSongPopupState());

    private SanitizePopupState GetSanitizePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SanitizePopupStateKey,
            static () => new SanitizePopupState());

    private ChangeNoteLengthPopupState GetChangeNoteLengthPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            ChangeNoteLengthPopupStateKey,
            static () => new ChangeNoteLengthPopupState());

    private SetTrackProgramPopupState GetSetTrackProgramPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SetTrackProgramPopupStateKey,
            static () => new SetTrackProgramPopupState());

    private MapInstrumentsPopupState GetMapInstrumentsPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            MapInstrumentsPopupStateKey,
            static () => new MapInstrumentsPopupState());

    // Transpose Popup
    private void DrawTransposePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##TransposeTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetTransposePopupState();

        ImGui.Text("Transpose Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.Transpose);

        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Semitones##transpSemi", ref state.Semitones, 12, 12);

        ImGui.Spacing();
        ImGui.Text("Apply transposition to range:");
        // ImGui.DragIntRange2("##transposeNoteRange", ref state.MinimumNoteNumber, ref state.MaximumNoteNumber, 1f, 0, 127, NoteNames[state.MinimumNoteNumber], NoteNames[state.MaximumNoteNumber]);

        using (ImRaii.Group())
        {
            ImGui.Text("Min Note");
            ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("##transposeMinNote", NoteNames[state.MinimumNoteNumber]))
            {
                for (int i = 0; i <= 127; i++)
                {
                    if (ImGui.Selectable(NoteNames[i], state.MinimumNoteNumber == i))
                        state.MinimumNoteNumber = i;
                    if (state.MinimumNoteNumber == i) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.Text("Max Note");
            ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("##transposeMaxNote", NoteNames[state.MaximumNoteNumber]))
            {
                for (int i = 0; i <= 127; i++)
                {
                    if (ImGui.Selectable(NoteNames[i], state.MaximumNoteNumber == i))
                        state.MaximumNoteNumber = i;
                    if (state.MaximumNoteNumber == i) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        if (state.MinimumNoteNumber > state.MaximumNoteNumber)
            (state.MinimumNoteNumber, state.MaximumNoteNumber) = (state.MaximumNoteNumber, state.MinimumNoteNumber);

        ImGui.Spacing();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.LongArrowAltUp, "##transposeTrackUpperRange", "Upper Range 85 (C#6) - 127 (G9)"))
        {
            state.MinimumNoteNumber = 85;
            state.MaximumNoteNumber = 127;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.LongArrowAltRight, "##transposeTrackMiddleRange", "Middle Range 0 (C3) - 84 (C6)"))
        {
            state.MinimumNoteNumber = 48;
            state.MaximumNoteNumber = 84;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.LongArrowAltDown, "##transposeTrackLowerRange", "Lower Range 0 (C-1) - 47 (B2)"))
        {
            state.MinimumNoteNumber = 0;
            state.MaximumNoteNumber = 47;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##transposeTrackResetRange", "Reset Range"))
        {
            state.MinimumNoteNumber = 0;
            state.MaximumNoteNumber = 127;
        }

        ImGui.Checkbox("Create transposed tracks (keep originals)##transposeCreateNew", ref state.CreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TransposeCreateNew);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doTranspose"))
        {
            var result = _editorCommandExecutor.Execute(
                new TransposeTracksCommand(),
                CreateEditorCommandContext(),
                new TransposeTracksOptions(
                    _selectedTrackIndices.OrderBy(index => index).ToArray(),
                    state.Semitones,
                    state.MinimumNoteNumber,
                    state.MaximumNoteNumber,
                    state.CreateNewTracks));

            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                ImGui.CloseCurrentPopup();
            }
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

        var state = GetMergePopupState();

        ImGui.Text("Merge Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.Merge);

        ImGui.Checkbox("Include Program Change events", ref state.IncludeProgramChanges);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeEvents);
        ImGui.Checkbox("Include Pitch Bend events", ref state.IncludePitchBends);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeEvents);
        ImGui.Checkbox("Include Control Change events", ref state.IncludeControlChanges);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeEvents);
        ImGui.Checkbox("Remove duplicate equal notes", ref state.RemoveEqualNotes);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.MergeRemoveDuplicateEqualNotes);
        ImGui.Checkbox("Delete original tracks after merge", ref state.DeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeDeleteOriginal);
        ImGui.Spacing();
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Note merge tolerance (ms)##mergeTolerance", ref state.ToleranceMilliseconds, 10, 100);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.MergeNoteTolerance);
        state.ToleranceMilliseconds = Math.Max(0, state.ToleranceMilliseconds);

        ImGui.Spacing();
        ImGui.Text("Target track (merge INTO this track's clone):");

        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToList();

        if (state.TargetRelativeIndex >= validIndices.Count)
            state.TargetRelativeIndex = 0;

        for (int r = 0; r < validIndices.Count; r++)
        {
            var track = _file.Tracks[validIndices[r]];
            bool sel = state.TargetRelativeIndex == r;
            if (ImGui.RadioButton($"{track.DisplayName}##mergeTarget_{r}", sel))
                state.TargetRelativeIndex = r;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool canMerge = validIndices.Count >= 2;
        using (ImRaii.Disabled(!canMerge))
        {
            if (ImGuiUtil.SuccessButton("Merge##doMerge"))
            {
                var targetIdx = validIndices[state.TargetRelativeIndex];
                var result = _editorCommandExecutor.Execute(
                    new MergeTracksCommand(),
                    CreateEditorCommandContext(),
                    new MergeTracksOptions(
                        targetIdx,
                        validIndices,
                        state.IncludeProgramChanges,
                        state.IncludePitchBends,
                        state.IncludeControlChanges,
                        state.ToleranceMilliseconds,
                        state.RemoveEqualNotes,
                        state.DeleteOriginalTracks));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
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

        var state = GetQuantizePopupState();

        ImGui.Text(state.NotesOnly ? "Quantize Selected Notes" : "Quantize Selected Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.Quantize);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Grid##quantStep", ref state.StepIndex,
            QuantizeStepLabels, QuantizeStepLabels.Length);

        // Target: Start / End / Both
        int targetIdx = Array.IndexOf(QuantizeTargetValues, state.Target);
        if (targetIdx < 0) targetIdx = 0;
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("Target##quantTarget", ref targetIdx, QuantizeTargetLabels, QuantizeTargetLabels.Length))
            state.Target = QuantizeTargetValues[targetIdx];

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("Strength##quantLevel", ref state.Level, 0f, 1f, "%.2f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.QuantizeStrength);

        ImGui.Checkbox("Preserve note length##quantFixEnd", ref state.FixOppositeEnd);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.QuantizePreserveNoteLength);

        if (!state.NotesOnly)
        {
            ImGui.Checkbox("Create new quantized track (keep original)", ref state.CreateNewTracks);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doQuantize"))
        {
            var grid = BuildQuantizeGrid(state.StepIndex);
            var settings = new QuantizingSettings
            {
                Target = state.Target,
                QuantizingLevel = state.Level,
                FixOppositeEnd = state.FixOppositeEnd,
                QuantizingBeyondZeroPolicy = QuantizingBeyondZeroPolicy.FixAtZero,
                QuantizingBeyondFixedEndPolicy = QuantizingBeyondFixedEndPolicy.CollapseAndFix,
            };

            if (state.NotesOnly)
            {
                if (_selectedTrackIndex >= 0)
                {
                    var keys = MidiEditorSelectionKeys.FromSelectedEvents(CurrentEvents, _selectedEventIndices);
                    if (keys.Count > 0)
                    {
                        var result = _editorCommandExecutor.Execute(
                            new QuantizeSelectedNotesCommand(),
                            CreateEditorCommandContext(),
                            new QuantizeSelectedNotesOptions(
                                _selectedTrackIndex,
                                keys,
                                grid,
                                settings));
                        if (result.Succeeded)
                        {
                            ApplyEditorCommandRefreshHints();
                        }
                    }
                }
            }
            else
            {
                var result = _editorCommandExecutor.Execute(
                    new QuantizeTracksCommand(),
                    CreateEditorCommandContext(),
                    new QuantizeTracksOptions(
                        _selectedTrackIndices.OrderBy(index => index).ToArray(),
                        grid,
                        settings,
                        state.CreateNewTracks));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                }
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelQuantize"))
            ImGui.CloseCurrentPopup();
    }

    private static IGrid BuildQuantizeGrid(int stepIndex)
    {
        ITimeSpan[] steps =
        {
            MusicalTimeSpan.Quarter,
            MusicalTimeSpan.Eighth,
            MusicalTimeSpan.Sixteenth,
            MusicalTimeSpan.ThirtySecond,
            MusicalTimeSpan.SixtyFourth,
        };
        return new SteppedGrid(steps[Math.Clamp(stepIndex, 0, steps.Length - 1)]);
    }

    //  Change Note Length Popup
    private void DrawChangeNoteLengthPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##ChangeNoteLengthPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetChangeNoteLengthPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Change Selected Track Note Length");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ChangeNoteLength);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Min length ticks##changeLengthMin", ref state.MinimumLengthTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChangeNoteLengthRange);
        state.MinimumLengthTicks = Math.Max(0, state.MinimumLengthTicks);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Max length ticks##changeLengthMax", ref state.MaximumLengthTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChangeNoteLengthRange);
        state.MaximumLengthTicks = Math.Max(0, state.MaximumLengthTicks);
        if (state.MinimumLengthTicks > state.MaximumLengthTicks)
            (state.MinimumLengthTicks, state.MaximumLengthTicks) = (state.MaximumLengthTicks, state.MinimumLengthTicks);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("New length ticks##changeLengthNew", ref state.NewLengthTicks);
        state.NewLengthTicks = Math.Max(1, state.NewLengthTicks);

        if (ImGui.SmallButton("x2##changeLengthNewDouble"))
            state.NewLengthTicks = Math.Max(1, state.NewLengthTicks * 2);
        ImGui.SameLine();
        if (ImGui.SmallButton("/2##changeLengthNewHalf"))
            state.NewLengthTicks = Math.Max(1, state.NewLengthTicks / 2);

        ImGui.Checkbox("Delete original tracks after change length##changeLengthDeleteOriginal", ref state.DeleteOriginalTracks);
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
                            MinimumLengthTicks: state.MinimumLengthTicks,
                            MaximumLengthTicks: state.MaximumLengthTicks,
                            NewLengthTicks: state.NewLengthTicks,
                            DeleteOriginalTracks: state.DeleteOriginalTracks)));

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

        var state = GetSetTrackProgramPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        state.ProgramNumber = Math.Clamp(state.ProgramNumber, 0, 127);
        var preview = GmProgramComboItems[state.ProgramNumber];

        ImGui.Text("Set Selected Track MIDI Program");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SetTrackProgram);

        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Program##setTrackProgramCombo", preview))
        {
            for (int i = 0; i < GmProgramComboItems.Length; i++)
            {
                var selected = i == state.ProgramNumber;
                if (ImGui.Selectable(GmProgramComboItems[i], selected))
                    state.ProgramNumber = i;
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.Checkbox("Replace all existing Program Change events##setTrackProgramReplaceAll", ref state.ReplaceAllProgramChanges);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.SetTrackProgramReplaceAll);

        ImGui.Checkbox("Rename tracks from selected program##setTrackProgramRename", ref state.RenameTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SetTrackProgramRename);
        using (ImRaii.Disabled(!state.RenameTracks))
        {
            ImGui.RadioButton("FFXIV instrument name##setTrackProgramRenameFfxiv", ref state.RenameModeIndex, 0);
            ImGui.SameLine();
            ImGui.RadioButton("MIDI program name##setTrackProgramRenameMidi", ref state.RenameModeIndex, 1);
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
                            ProgramNumber: state.ProgramNumber,
                            ReplaceAllProgramChanges: state.ReplaceAllProgramChanges,
                            RenameTracks: state.RenameTracks,
                            RenameMode: state.RenameModeIndex == 0
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

    private void DrawMapInstrumentsPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##MapInstrumentsPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetMapInstrumentsPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        state.ModeIndex = Math.Clamp(state.ModeIndex, 0, MapInstrumentsModeLabels.Length - 1);
        state.NameSourceIndex = Math.Clamp(state.NameSourceIndex, 0, MapInstrumentsNameSourceLabels.Length - 1);

        ImGui.Text("Map Selected Instruments");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.MapInstruments);

        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        ImGui.Combo(
            "Name source##mapInstrumentsNameSource",
            ref state.NameSourceIndex,
            MapInstrumentsNameSourceLabels,
            MapInstrumentsNameSourceLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MapInstrumentsNameSource);

        ImGui.SetNextItemWidth(260f * ImGuiHelpers.GlobalScale);
        ImGui.Combo(
            "Rename mode##mapInstrumentsMode",
            ref state.ModeIndex,
            MapInstrumentsModeLabels,
            MapInstrumentsModeLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MapInstrumentsMode);

        ImGui.Checkbox("Include drum tracks##mapInstrumentsDrums", ref state.IncludeDrumTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MapInstrumentsDrums);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doMapInstruments"))
            {
                var result = _editorCommandExecutor.Execute(
                    new MapInstrumentsCommand(),
                    CreateEditorCommandContext(),
                    new MapInstrumentsCommandOptions(
                        validIndices,
                        new MidiForgeMapInstrumentsOptions(
                            GetMapInstrumentsMode(state.ModeIndex),
                            state.IncludeDrumTracks,
                            GetMapInstrumentsNameSource(state.NameSourceIndex))));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelMapInstruments"))
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

        var state = GetMergeSongPopupState();

        ImGui.Text("Merge Song");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("How to place the imported file:");
        ImGui.RadioButton("Simultaneously (overlay tracks at time 0)##mergeSongSim", ref state.Mode, 0);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.MergeSongSimultaneous);
        ImGui.RadioButton("Sequentially (append after this file)##mergeSongSeq", ref state.Mode, 1);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.MergeSongSequential);

        if (state.Mode == 0)
        {
            ImGui.Spacing();
            ImGui.Checkbox("Ignore different tempo maps##mergeSongIgnoreTempo", ref state.IgnoreDifferentTempoMaps);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.MergeSongIgnoreTempo);
            if (!state.IgnoreDifferentTempoMaps)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Orange))
                    ImGui.TextWrapped("Warning: both files must share an identical tempo map or an error will occur.");
            }
        }

        if (state.Mode == 1)
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(130f * ImGuiHelpers.GlobalScale);
            ImGui.InputInt("Delay between files (ms)##mergeSongDelay", ref state.DelayMilliseconds, 100, 1000);
            state.DelayMilliseconds = Math.Max(0, state.DelayMilliseconds);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Open File...##mergeSongOpen"))
        {
            state.Sequential = state.Mode == 1;
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

        var state = GetSanitizePopupState();

        ImGui.Text("Sanitize MIDI File");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Checkbox("Remove duplicated notes##sanDuplNotes", ref state.RemoveDuplicatedNotes);
        ImGui.Checkbox("Remove empty track chunks##sanEmptyTracks", ref state.RemoveEmptyTrackChunks);
        ImGui.Checkbox("Remove orphaned Note Off events##sanOrphanOff", ref state.RemoveOrphanedNoteOffEvents);

        ImGui.Spacing();
        string[] orphanOnLabels = { "Remove", "Ignore", "Complete note (use max length)" };
        int onPolicyIdx = (int)state.OrphanedNoteOnEventsPolicy;
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("Orphaned Note On##sanOrphanOn", ref onPolicyIdx, orphanOnLabels, orphanOnLabels.Length))
            state.OrphanedNoteOnEventsPolicy = (OrphanedNoteOnEventsPolicy)onPolicyIdx;

        ImGui.Checkbox("Remove duplicate Set Tempo events##sanDuplTempo", ref state.RemoveDuplicatedSetTempoEvents);
        ImGui.Checkbox("Remove duplicate Time Signature events##sanDuplTimeSig", ref state.RemoveDuplicatedTimeSignatureEvents);
        ImGui.Checkbox("Trim silence at start##sanTrim", ref state.Trim);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doSanitize"))
        {
            var settings = new SanitizingSettings
            {
                RemoveDuplicatedNotes = state.RemoveDuplicatedNotes,
                RemoveEmptyTrackChunks = state.RemoveEmptyTrackChunks,
                RemoveOrphanedNoteOffEvents = state.RemoveOrphanedNoteOffEvents,
                OrphanedNoteOnEventsPolicy = state.OrphanedNoteOnEventsPolicy,
                RemoveDuplicatedSetTempoEvents = state.RemoveDuplicatedSetTempoEvents,
                RemoveDuplicatedTimeSignatureEvents = state.RemoveDuplicatedTimeSignatureEvents,
                Trim = state.Trim,
            };
            var result = _editorCommandExecutor.Execute(
                new SanitizeFileCommand(),
                CreateEditorCommandContext(),
                new SanitizeFileOptions(settings));
            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                ImGui.CloseCurrentPopup();
            }
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

        var state = GetTransposeNotesPopupState();

        ImGui.Text("Transpose Selected Notes");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Semitones##transpNotesSemi", ref state.Semitones, 12, 12);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doTransposeNotes"))
        {
            TransposeSelectedNotes(state.Semitones);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelTransposeNotes"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class TransposePopupState
    {
        public int Semitones = 0;
        public int MinimumNoteNumber = 0;
        public int MaximumNoteNumber = 127;
        public bool CreateNewTracks = false;

        public void Reset()
        {
            Semitones = 0;
            MinimumNoteNumber = 0;
            MaximumNoteNumber = 127;
            CreateNewTracks = false;
        }
    }

    private sealed class TransposeNotesPopupState
    {
        public int Semitones = 0;

        public void Reset()
        {
            Semitones = 0;
        }
    }

    private sealed class MergePopupState
    {
        public bool IncludeProgramChanges = true;
        public bool IncludePitchBends = true;
        public bool IncludeControlChanges = true;
        public bool RemoveEqualNotes = true;
        public bool DeleteOriginalTracks = false;
        public int TargetRelativeIndex = 0;
        public int ToleranceMilliseconds = 0;

        public void ResetTarget()
        {
            TargetRelativeIndex = 0;
        }
    }

    private sealed class QuantizePopupState
    {
        public int StepIndex = 2;
        public bool CreateNewTracks = false;
        public QuantizerTarget Target = QuantizerTarget.Start;
        public float Level = 1.0f;
        public bool FixOppositeEnd = true;
        public bool NotesOnly = false;
    }

    private sealed class MergeSongPopupState
    {
        public bool Sequential = false;
        public int DelayMilliseconds = 0;
        public int Mode = 0;
        public bool IgnoreDifferentTempoMaps = true;

        public void ResetForOpen()
        {
            Mode = 0;
            DelayMilliseconds = 0;
        }
    }

    private sealed class SanitizePopupState
    {
        public bool RemoveDuplicatedNotes = true;
        public bool RemoveEmptyTrackChunks = true;
        public bool RemoveOrphanedNoteOffEvents = true;
        public OrphanedNoteOnEventsPolicy OrphanedNoteOnEventsPolicy = OrphanedNoteOnEventsPolicy.Remove;
        public bool RemoveDuplicatedSetTempoEvents = true;
        public bool RemoveDuplicatedTimeSignatureEvents = true;
        public bool Trim = false;
    }

    private sealed class ChangeNoteLengthPopupState
    {
        public int MinimumLengthTicks = 0;
        public int MaximumLengthTicks = 0;
        public int NewLengthTicks = 240;
        public bool DeleteOriginalTracks = false;

        public void Reset()
        {
            MinimumLengthTicks = 0;
            MaximumLengthTicks = 0;
            NewLengthTicks = 240;
            DeleteOriginalTracks = false;
        }
    }

    private sealed class SetTrackProgramPopupState
    {
        public int ProgramNumber = 0;
        public bool ReplaceAllProgramChanges = true;
        public bool RenameTracks = true;
        public int RenameModeIndex = 0;

        public void Reset()
        {
            ProgramNumber = 0;
            ReplaceAllProgramChanges = true;
            RenameTracks = true;
            RenameModeIndex = 0;
        }
    }

    private sealed class MapInstrumentsPopupState
    {
        public int NameSourceIndex = 0;
        public int ModeIndex = 1;
        public bool IncludeDrumTracks = true;

        public void Reset()
        {
            NameSourceIndex = 0;
            ModeIndex = 1;
            IncludeDrumTracks = true;
        }
    }

    private static MidiForgeMapInstrumentsMode GetMapInstrumentsMode(int index)
        => index switch
        {
            0 => MidiForgeMapInstrumentsMode.EmptyNamesOnly,
            2 => MidiForgeMapInstrumentsMode.ReplaceSelectedNames,
            _ => MidiForgeMapInstrumentsMode.EmptyOrGenericNamesOnly,
        };

    private static MidiForgeTrackNameFillMode GetMapInstrumentsNameSource(int index)
        => index == 1
            ? MidiForgeTrackNameFillMode.Midi
            : MidiForgeTrackNameFillMode.Ffxiv;
}
