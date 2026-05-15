using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Core;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.Commands.Guitar;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Managers;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const string PrepareForPlaybackPopupStateKey = "auto-edit.prepare-for-playback.popup";
    private const string AutoEditPopupStateKey = "auto-edit.selected-tracks.popup";
    private const string AdaptToRangePopupStateKey = "forge.adapt-to-range.popup";
    private const string ApplyTrackNameTransposesPopupStateKey = "forge.apply-track-name-transposes.popup";
    private const string MergeGuitarToneTracksPopupStateKey = "forge.merge-guitar-tone-tracks.popup";
    private const string SplitChordsPopupStateKey = "forge.split-chords.popup";
    private const string SplitToneRangePopupStateKey = "forge.split-tone-range.popup";
    private const string SplitLengthRangePopupStateKey = "forge.split-length-range.popup";
    private const string ExtendNotesDurationPopupStateKey = "forge.extend-notes-duration.popup";
    private const string SplitEqualNotesPopupStateKey = "forge.split-equal-notes.popup";
    private const string DifferenceTracksPopupStateKey = "forge.difference-tracks.popup";
    private const string SplitNotesIntoTracksPopupStateKey = "forge.split-notes-into-tracks.popup";
    private const string GeneratePitchBendNotesPopupStateKey = "forge.generate-pitch-bend-notes.popup";

    private static readonly string[] AutoEditPickStrategyLabels =
    {
        "Highest chord lines",
        "Odd chord lines",
    };

    private static readonly string[] SplitChordStrategyLabels =
    {
        "Same start tick",
        "Same start tick and length",
    };

    private static readonly string[] SplitChordGroupModeLabels =
    {
        "Merge by chord part",
        "Individual by chord size and part",
        "Group whole chords by size",
    };

    private static readonly string[] RangeFitStrategyLabels =
    {
        "Move each note into range",
        "Lower high notes first",
        "Find the best octave",
    };

    private static MidiForgeRangeFitStrategy GetRangeFitStrategy(int index)
        => index switch
        {
            1 => MidiForgeRangeFitStrategy.LowerHighNotesFirst,
            2 => MidiForgeRangeFitStrategy.BestOctaveFit,
            _ => MidiForgeRangeFitStrategy.FitNotesIndividually,
        };

    private static int GetRangeFitStrategyIndex(MidiForgeRangeFitStrategy strategy)
        => strategy switch
        {
            MidiForgeRangeFitStrategy.LowerHighNotesFirst => 1,
            MidiForgeRangeFitStrategy.BestOctaveFit => 2,
            _ => 0,
        };

    private PrepareForPlaybackPopupState GetPrepareForPlaybackPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            PrepareForPlaybackPopupStateKey,
            static () => new PrepareForPlaybackPopupState());

    private AutoEditPopupState GetAutoEditPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            AutoEditPopupStateKey,
            static () => new AutoEditPopupState());

    private AdaptToRangePopupState GetAdaptToRangePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            AdaptToRangePopupStateKey,
            static () => new AdaptToRangePopupState());

    private ApplyTrackNameTransposesPopupState GetApplyTrackNameTransposesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            ApplyTrackNameTransposesPopupStateKey,
            static () => new ApplyTrackNameTransposesPopupState());

    private MergeGuitarToneTracksPopupState GetMergeGuitarToneTracksPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            MergeGuitarToneTracksPopupStateKey,
            static () => new MergeGuitarToneTracksPopupState());

    private SplitChordsPopupState GetSplitChordsPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SplitChordsPopupStateKey,
            static () => new SplitChordsPopupState());

    private SplitToneRangePopupState GetSplitToneRangePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SplitToneRangePopupStateKey,
            static () => new SplitToneRangePopupState());

    private SplitLengthRangePopupState GetSplitLengthRangePopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SplitLengthRangePopupStateKey,
            static () => new SplitLengthRangePopupState());

    private ExtendNotesDurationPopupState GetExtendNotesDurationPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            ExtendNotesDurationPopupStateKey,
            static () => new ExtendNotesDurationPopupState());

    private SplitEqualNotesPopupState GetSplitEqualNotesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SplitEqualNotesPopupStateKey,
            static () => new SplitEqualNotesPopupState());

    private DifferenceTracksPopupState GetDifferenceTracksPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            DifferenceTracksPopupStateKey,
            static () => new DifferenceTracksPopupState());

    private SplitNotesIntoTracksPopupState GetSplitNotesIntoTracksPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            SplitNotesIntoTracksPopupStateKey,
            static () => new SplitNotesIntoTracksPopupState());

    private GeneratePitchBendNotesPopupState GetGeneratePitchBendNotesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            GeneratePitchBendNotesPopupStateKey,
            static () => new GeneratePitchBendNotesPopupState());

    private void DrawPrepareForPlaybackPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##PrepareForPlaybackPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetPrepareForPlaybackPopupState();
        var performanceTrackCount = _file.Tracks.Count(track => !track.IsConductorTrack);

        ImGui.Text("Prepare Whole File for Playback");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.PrepareForPlayback);

        ImGui.Checkbox("Fill empty track names##prepareFillEmptyTrackNames", ref state.FillEmptyTrackNames);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.PrepareFillEmptyTrackNames);

        ImGui.Checkbox("Apply track-name transposes##prepareApplyTrackNameTransposes", ref state.ApplyTrackNameTransposes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ApplyTrackNameTransposes);

        ImGui.Checkbox("Split drumkit tracks##prepareSplitDrumkits", ref state.SplitDrumkits);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.PrepareSplitDrumkits);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum simultaneous notes##prepareMaxSimultaneousNotes", ref state.MaxSimultaneousNotes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AutoEditMaxSimultaneousNotes);
        state.MaxSimultaneousNotes = int.Clamp(state.MaxSimultaneousNotes, 1, 3);

        ImGui.SetNextItemWidth(240f);
        state.PickStrategyIndex = int.Clamp(state.PickStrategyIndex, 0, AutoEditPickStrategyLabels.Length - 1);
        ImGui.Combo("Chord line choice##preparePickStrategy", ref state.PickStrategyIndex,
            AutoEditPickStrategyLabels, AutoEditPickStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AutoEditPickStrategy);

        ImGui.SetNextItemWidth(240f);
        state.RangeStrategyIndex = int.Clamp(state.RangeStrategyIndex, 0, RangeFitStrategyLabels.Length - 1);
        ImGui.Combo("Range fit##prepareRangeStrategy", ref state.RangeStrategyIndex,
            RangeFitStrategyLabels, RangeFitStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.RangeFitStrategy);

        ImGui.Spacing();
        ImGui.TextDisabled($"{performanceTrackCount} performance track(s) in file");
        ImGui.TextWrapped(MidiEditorOperationHelp.PrepareForPlaybackOptions);
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(performanceTrackCount == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doPrepareForPlayback"))
            {
                var result = _editorCommandExecutor.Execute(
                    new PrepareForPlaybackCommand(),
                    CreateEditorCommandContext(),
                    new PrepareForPlaybackCommandOptions(
                        new MidiForgePrepareForPlaybackOptions(
                            FillEmptyTrackNames: state.FillEmptyTrackNames,
                            ApplyTrackNameTransposes: state.ApplyTrackNameTransposes,
                            SplitDrumkits: state.SplitDrumkits,
                            MaxSimultaneousNotes: state.MaxSimultaneousNotes,
                            PickStrategy: state.PickStrategyIndex == 1
                                ? MidiForgeChordPickStrategy.OddChords
                                : MidiForgeChordPickStrategy.HighestChords,
                            RangeStrategy: GetRangeFitStrategy(state.RangeStrategyIndex))));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelPrepareForPlayback"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawAdaptToRangePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##AdaptToRangePopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetAdaptToRangePopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i >= 0 && i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Adapt Selected Tracks to C3-C6");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.AdaptToRange);

        ImGui.Checkbox("Create adapted tracks (keep originals)##adaptCreateNew", ref state.CreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);
        ImGui.SetNextItemWidth(240f);
        state.StrategyIndex = int.Clamp(state.StrategyIndex, 0, RangeFitStrategyLabels.Length - 1);
        ImGui.Combo("Range fit##adaptRangeStrategy", ref state.StrategyIndex,
            RangeFitStrategyLabels, RangeFitStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.RangeFitStrategy);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doAdaptToRange"))
            {
                var result = _editorCommandExecutor.Execute(
                    new AdaptTracksToPlayableRangeCommand(),
                    CreateEditorCommandContext(),
                    new AdaptTracksToPlayableRangeCommandOptions(
                        validIndices,
                        new MidiForgeAdaptToRangeOptions(
                            CreateNewTracks: state.CreateNewTracks,
                            RangeStrategy: GetRangeFitStrategy(state.StrategyIndex))));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelAdaptToRange"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawApplyTrackNameTransposesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##ApplyTrackNameTransposesPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetApplyTrackNameTransposesPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i >= 0 && i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();
        var transposedIndices = validIndices
            .Where(i => TrackInfo.GetTransposeByName(_file.Tracks[i].Name) != 0)
            .ToArray();

        ImGui.Text("Apply Track-Name Transposes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ApplyTrackNameTransposes);

        ImGui.Checkbox("Create migrated tracks (keep originals)##applyTrackNameTransposeCreateNew",
            ref state.CreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.TextDisabled($"{transposedIndices.Length} track(s) with track-name transpose");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(transposedIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doApplyTrackNameTransposes"))
            {
                var result = _editorCommandExecutor.Execute(
                    new ApplyTrackNameTransposesCommand(),
                    CreateEditorCommandContext(),
                    new ApplyTrackNameTransposesCommandOptions(
                        transposedIndices,
                        new MidiForgeApplyTrackNameTransposeOptions(
                            CreateNewTracks: state.CreateNewTracks)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelApplyTrackNameTransposes"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawMergeGuitarToneTracksPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##MergeGuitarToneTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = _selectedTrackIndices
            .Where(i => i >= 0 && i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();
        var toneResolution = ResolveSelectedGuitarToneTracks(validIndices);
        var toneByTrackIndex = toneResolution.ToneByTrackIndex;
        var resolvedCount = toneResolution.ResolvedTracks;
        var tooManyTracks = toneResolution.ExceedsMaximumResolvedTracks;
        var state = GetMergeGuitarToneTracksPopupState();

        ImGui.Text("Merge Guitar Tone Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.MergeGuitarToneTracks);

        ImGui.Checkbox("Delete original tracks after merge##mergeGuitarToneDeleteOriginal",
            ref state.DeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeGuitarToneDeleteOriginal);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.TextDisabled($"{resolvedCount} selected guitar tone track(s)");
        if (tooManyTracks)
            ImGui.TextDisabled($"Maximum mergeable guitar tone tracks: {MidiForgeGuitarTonePrimitives.MaximumMergeTracks}");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(resolvedCount == 0 || tooManyTracks))
        {
            if (ImGuiUtil.SuccessButton("Merge##doMergeGuitarToneTracks"))
            {
                var result = _editorCommandExecutor.Execute(
                    new MergeGuitarToneTracksCommand(),
                    CreateEditorCommandContext(),
                    new MergeGuitarToneTracksCommandOptions(
                        validIndices,
                        new MidiForgeMergeGuitarToneTracksOptions(
                            toneByTrackIndex,
                            DeleteOriginalTracks: state.DeleteOriginalTracks)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelMergeGuitarToneTracks"))
            ImGui.CloseCurrentPopup();
    }

    private MidiForgeResolveGuitarToneGroupsResult ResolveSelectedGuitarToneTracks(int[] validIndices)
    {
        var result = _editorQueryExecutor.Execute(
            new ResolveGuitarToneGroupsQuery(),
            CreateEditorQueryContext(),
            new ResolveGuitarToneGroupsQueryOptions(
                validIndices,
                CreateCurrentGuitarToneOverrideSnapshot(),
                TryLoadMidiFileConfigSnapshot()));

        if (result.Succeeded)
            return result.Result!.Value;

        return new MidiForgeResolveGuitarToneGroupsResult(
            Array.Empty<MidiForgeGuitarToneTrackResolution>(),
            new Dictionary<int, int>(),
            0,
            0,
            0,
            MidiForgeGuitarTonePrimitives.MaximumMergeTracks,
            false);
    }

    private MidiForgeGuitarToneOverrideSnapshot CreateCurrentGuitarToneOverrideSnapshot()
        => new(
            _plugin.Config.GuitarToneMode,
            _plugin.CurrentBardPlayback?.FilePath,
            _plugin.Config.TrackStatus
                .Select((status, index) => new KeyValuePair<int, int>(index, status.Tone))
                .ToDictionary(pair => pair.Key, pair => pair.Value));

    private MidiForgeGuitarToneJsonConfigSnapshot? TryLoadMidiFileConfigSnapshot()
    {
        if (_file?.FilePath == null)
            return null;

        var config = _plugin.MidiFileConfigManager.GetMidiConfigFromFile(_file.FilePath);
        if (config == null || config.Tracks.Count == 0)
        {
            return null;
        }

        return new MidiForgeGuitarToneJsonConfigSnapshot(
            config.Tracks
                .Select(track => new MidiForgeGuitarToneJsonTrack(
                    track.Index,
                    track.Name ?? string.Empty,
                    track.Instrument))
                .ToArray());
    }

    private void DrawAutoEditPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##AutoEditPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetAutoEditPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i >= 0 && i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Auto Edit");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.AutoEdit);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum simultaneous notes##autoEditMaxSimultaneousNotes", ref state.MaxSimultaneousNotes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AutoEditMaxSimultaneousNotes);
        state.MaxSimultaneousNotes = int.Clamp(state.MaxSimultaneousNotes, 1, 3);

        ImGui.SetNextItemWidth(240f);
        state.PickStrategyIndex = int.Clamp(state.PickStrategyIndex, 0, AutoEditPickStrategyLabels.Length - 1);
        ImGui.Combo("Chord line choice##autoEditPickStrategy", ref state.PickStrategyIndex,
            AutoEditPickStrategyLabels, AutoEditPickStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AutoEditPickStrategy);

        ImGui.Checkbox("Fit notes to C3-C6##autoEditAdaptOutOfRange", ref state.AdaptOutOfRange);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AdaptToRange);

        if (state.AdaptOutOfRange)
        {
            ImGui.SetNextItemWidth(240f);
            state.RangeStrategyIndex = int.Clamp(state.RangeStrategyIndex, 0, RangeFitStrategyLabels.Length - 1);
            ImGui.Combo("Range fit##autoEditRangeStrategy", ref state.RangeStrategyIndex,
                RangeFitStrategyLabels, RangeFitStrategyLabels.Length);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.RangeFitStrategy);
        }

        ImGui.Checkbox("Create edited tracks (keep originals)##autoEditCreateNew", ref state.CreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doAutoEdit"))
            {
                var result = _editorCommandExecutor.Execute(
                    new AutoEditSelectedTracksCommand(),
                    CreateEditorCommandContext(),
                    new AutoEditSelectedTracksCommandOptions(
                        validIndices,
                        new MidiForgeAutoEditOptions(
                            MaxSimultaneousNotes: state.MaxSimultaneousNotes,
                            PickStrategy: state.PickStrategyIndex == 1
                                ? MidiForgeChordPickStrategy.OddChords
                                : MidiForgeChordPickStrategy.HighestChords,
                            AdaptOutOfRangeNotes: state.AdaptOutOfRange,
                            CreateNewTracks: state.CreateNewTracks,
                            RangeStrategy: GetRangeFitStrategy(state.RangeStrategyIndex))));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelAutoEdit"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawSplitChordsPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitChordsPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSplitChordsPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Split Chords");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitChords);

        ImGui.SetNextItemWidth(240f);
        ImGui.Combo("Strategy##splitChordStrategy", ref state.StrategyIndex,
            SplitChordStrategyLabels, SplitChordStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordSplitStrategy);

        ImGui.SetNextItemWidth(240f);
        ImGui.Combo("Group mode##splitChordGroupMode", ref state.GroupModeIndex,
            SplitChordGroupModeLabels, SplitChordGroupModeLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordGroupMode);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Minimum simultaneous notes##splitChordMin", ref state.MinimumSimultaneousNotes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordMinimumSimultaneousNotes);
        state.MinimumSimultaneousNotes = int.Clamp(state.MinimumSimultaneousNotes, 2, 10);

        ImGui.Checkbox("Insert split tracks at end##splitChordInsertEnd", ref state.InsertPartsAtEnd);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordInsertPartsAtEnd);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitChords"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SplitTracksChordsCommand(),
                    CreateEditorCommandContext(),
                    new SplitTracksChordsCommandOptions(
                        validIndices,
                        new MidiForgeSplitChordsOptions(
                            Strategy: state.StrategyIndex == 1
                                ? MidiForgeChordSplitStrategy.SameStartTickAndLength
                                : MidiForgeChordSplitStrategy.SameStartTick,
                            GroupMode: state.GroupModeIndex switch
                            {
                                1 => MidiForgeChordGroupMode.Individual,
                                2 => MidiForgeChordGroupMode.Group,
                                _ => MidiForgeChordGroupMode.GroupMerged,
                            },
                            MinimumSimultaneousNotes: state.MinimumSimultaneousNotes,
                            InsertPartsAtEnd: state.InsertPartsAtEnd)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSplitChords"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawSplitNotesByToneRangePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitNotesByToneRangePopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSplitToneRangePopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Notes by Tone Range");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitToneRange);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Minimum note##splitToneMin", ref state.MinimumNote);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitToneMinimumNote);
        state.MinimumNote = int.Clamp(state.MinimumNote, 0, 127);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum note##splitToneMax", ref state.MaximumNote);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitToneMaximumNote);
        state.MaximumNote = int.Clamp(state.MaximumNote, 0, 127);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitNotesByToneRange"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SplitTracksByToneRangeCommand(),
                    CreateEditorCommandContext(),
                    new SplitTracksByToneRangeCommandOptions(
                        validIndices,
                        new MidiForgeSplitToneRangeOptions(
                            MinimumNote: state.MinimumNote,
                            MaximumNote: state.MaximumNote)));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSplitNotesByToneRange"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawSplitNotesByLengthRangePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitNotesByLengthRangePopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSplitLengthRangePopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Notes by Length Range");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitLengthRange);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Minimum ticks##splitLengthMin", ref state.MinimumLengthTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitLengthMinimumTicks);
        if (state.MinimumLengthTicks < 0)
            state.MinimumLengthTicks = 0;

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum ticks##splitLengthMax", ref state.MaximumLengthTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitLengthMaximumTicks);
        if (state.MaximumLengthTicks < 0)
            state.MaximumLengthTicks = 0;

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitNotesByLengthRange"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SplitTracksByLengthRangeCommand(),
                    CreateEditorCommandContext(),
                    new SplitTracksByLengthRangeCommandOptions(
                        validIndices,
                        new MidiForgeSplitLengthRangeOptions(
                            MinimumLengthTicks: state.MinimumLengthTicks,
                            MaximumLengthTicks: state.MaximumLengthTicks)));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSplitNotesByLengthRange"))
            ImGui.CloseCurrentPopup();
    }

    private void SplitSelectedOverlappedNotes()
    {
        if (_file == null) return;

        var validIndices = GetSelectedPerformanceTrackIndices();
        if (validIndices.Length == 0) return;

        var result = _editorCommandExecutor.Execute(
            new SplitTracksOverlappedNotesCommand(),
            CreateEditorCommandContext(),
            new SplitTracksOverlappedNotesCommandOptions(validIndices));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void TrimSelectedOverlappedSustainedNotes()
    {
        if (_file == null) return;

        var validIndices = GetSelectedPerformanceTrackIndices();
        if (validIndices.Length == 0) return;

        var result = _editorCommandExecutor.Execute(
            new TrimOverlappedSustainedNotesCommand(),
            CreateEditorCommandContext(),
            new TrimOverlappedSustainedNotesCommandOptions(validIndices));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }

    private void DrawExtendNotesDurationPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##ExtendNotesDurationPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetExtendNotesDurationPopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Extend Notes Duration");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ExtendNotesDuration);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum duration ticks (0 = unlimited)##extendMaxDuration", ref state.MaximumDurationTicks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ExtendNotesMaximumDuration);
        if (state.MaximumDurationTicks < 0)
            state.MaximumDurationTicks = 0;

        ImGui.Checkbox("Respect empty measures##extendRespectEmptyMeasures", ref state.RespectEmptyMeasures);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.RespectEmptyMeasures);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doExtendNotesDuration"))
            {
                var result = _editorCommandExecutor.Execute(
                    new ExtendNotesDurationCommand(),
                    CreateEditorCommandContext(),
                    new ExtendNotesDurationCommandOptions(
                        validIndices,
                        new MidiForgeExtendNotesDurationOptions(
                            MaximumDurationTicks: state.MaximumDurationTicks,
                            RespectEmptyMeasures: state.RespectEmptyMeasures)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelExtendNotesDuration"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawSplitEqualNotesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitEqualNotesPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSplitEqualNotesPopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Equal Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitEqualNotes);

        ImGui.Text("Target track:");
        ImGuiUtil.HelpMarker(MidiEditorOperationHelp.TargetTrack);
        DrawTargetTrackRadioButtons(validIndices, ref state.TargetRelIdx, "splitEqualTarget");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length < 2))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitEqualNotes"))
            {
                var targetIdx = validIndices[state.TargetRelIdx];
                var result = _editorCommandExecutor.Execute(
                    new SplitTracksEqualNotesCommand(),
                    CreateEditorCommandContext(),
                    new SplitTracksEqualNotesCommandOptions(validIndices, targetIdx));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSplitEqualNotes"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawDifferenceTracksPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##DifferenceTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetDifferenceTracksPopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Difference Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.DifferenceTracks);

        ImGui.Text("Target track:");
        ImGuiUtil.HelpMarker(MidiEditorOperationHelp.TargetTrack);
        DrawTargetTrackRadioButtons(validIndices, ref state.TargetRelIdx, "differenceTarget");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length < 2))
        {
            if (ImGuiUtil.SuccessButton("Apply##doDifferenceTracks"))
            {
                var targetIdx = validIndices[state.TargetRelIdx];
                var result = _editorCommandExecutor.Execute(
                    new DifferenceTracksCommand(),
                    CreateEditorCommandContext(),
                    new DifferenceTracksCommandOptions(validIndices, targetIdx));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelDifferenceTracks"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawSplitNotesIntoTracksPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitNotesIntoTracksPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSplitNotesIntoTracksPopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Notes Into Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitNotesIntoTracks);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Number of tracks##splitIntoTracksCount", ref state.NumberOfTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitNotesIntoTracksCount);
        state.NumberOfTracks = int.Clamp(state.NumberOfTracks, 1, 64);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Every N notes##splitIntoTracksEvery", ref state.EveryNotesAmount);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitNotesIntoTracksEvery);
        if (state.EveryNotesAmount < 1)
            state.EveryNotesAmount = 1;

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitNotesIntoTracks"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SplitNotesIntoTracksCommand(),
                    CreateEditorCommandContext(),
                    new SplitNotesIntoTracksCommandOptions(
                        validIndices,
                        new MidiForgeSplitNotesIntoTracksOptions(
                            NumberOfTracks: state.NumberOfTracks,
                            EveryNotesAmount: state.EveryNotesAmount)));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelSplitNotesIntoTracks"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawGeneratePitchBendNotesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##GeneratePitchBendNotesPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetGeneratePitchBendNotesPopupState();
        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count
                        && !_file.Tracks[i].IsConductorTrack
                        && MidiForgeAnalysis.AnalyzeTrack(_file.Tracks[i]).PitchBendCount > 0)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Generate Pitch-Bend Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.GeneratePitchBendNotes);

        ImGui.Checkbox("Delete original tracks after generation##generatePitchBendDeleteOriginal",
            ref state.DeleteOriginalTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.GeneratePitchBendDeleteOriginal);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected track(s) with pitch bend events");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doGeneratePitchBendNotes"))
            {
                var result = _editorCommandExecutor.Execute(
                    new GeneratePitchBendNotesCommand(),
                    CreateEditorCommandContext(),
                    new GeneratePitchBendNotesCommandOptions(
                        validIndices,
                        new MidiForgeGeneratePitchBendNotesOptions(
                            DeleteOriginalTracks: state.DeleteOriginalTracks)));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelGeneratePitchBendNotes"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawTargetTrackRadioButtons(int[] validIndices, ref int targetRelIdx, string idPrefix)
    {
        if (_file == null) return;

        if (targetRelIdx >= validIndices.Length)
            targetRelIdx = 0;

        for (int i = 0; i < validIndices.Length; i++)
        {
            var track = _file.Tracks[validIndices[i]];
            var selected = targetRelIdx == i;
            if (ImGui.RadioButton($"{track.DisplayName}##{idPrefix}_{i}", selected))
                targetRelIdx = i;
        }
    }

    private sealed class PrepareForPlaybackPopupState
    {
        public bool FillEmptyTrackNames = true;
        public bool ApplyTrackNameTransposes = true;
        public bool SplitDrumkits = true;
        public int MaxSimultaneousNotes = 1;
        public int PickStrategyIndex = 0;
        public int RangeStrategyIndex = 1;
    }

    private sealed class AutoEditPopupState
    {
        public int MaxSimultaneousNotes = 1;
        public int PickStrategyIndex = 0;
        public int RangeStrategyIndex = 0;
        public bool AdaptOutOfRange = true;
        public bool CreateNewTracks = true;
    }

    private sealed class AdaptToRangePopupState
    {
        public bool CreateNewTracks = true;
        public int StrategyIndex = 2;

        public void Reset()
        {
            CreateNewTracks = true;
            StrategyIndex = GetRangeFitStrategyIndex(MidiForgeRangeFitStrategy.BestOctaveFit);
        }
    }

    private sealed class ApplyTrackNameTransposesPopupState
    {
        public bool CreateNewTracks = false;

        public void Reset()
            => CreateNewTracks = false;
    }

    private sealed class MergeGuitarToneTracksPopupState
    {
        public bool DeleteOriginalTracks = false;

        public void Reset()
            => DeleteOriginalTracks = false;
    }

    private sealed class SplitChordsPopupState
    {
        public int StrategyIndex = 0;
        public int GroupModeIndex = 0;
        public int MinimumSimultaneousNotes = 2;
        public bool InsertPartsAtEnd = true;

        public void Reset()
        {
            StrategyIndex = 0;
            GroupModeIndex = 0;
            MinimumSimultaneousNotes = 2;
            InsertPartsAtEnd = true;
        }
    }

    private sealed class SplitToneRangePopupState
    {
        public int MinimumNote = MidiForgeAnalysis.PlayableLowestMidiNote;
        public int MaximumNote = MidiForgeAnalysis.PlayableHighestMidiNote;

        public void Reset()
        {
            MinimumNote = MidiForgeAnalysis.PlayableLowestMidiNote;
            MaximumNote = MidiForgeAnalysis.PlayableHighestMidiNote;
        }
    }

    private sealed class SplitLengthRangePopupState
    {
        public int MinimumLengthTicks = 0;
        public int MaximumLengthTicks = 0;

        public void Reset()
        {
            MinimumLengthTicks = 0;
            MaximumLengthTicks = 0;
        }
    }

    private sealed class ExtendNotesDurationPopupState
    {
        public int MaximumDurationTicks = 0;
        public bool RespectEmptyMeasures = true;

        public void Reset()
        {
            MaximumDurationTicks = 0;
            RespectEmptyMeasures = true;
        }
    }

    private sealed class SplitEqualNotesPopupState
    {
        public int TargetRelIdx = 0;

        public void Reset()
            => TargetRelIdx = 0;
    }

    private sealed class DifferenceTracksPopupState
    {
        public int TargetRelIdx = 0;

        public void Reset()
            => TargetRelIdx = 0;
    }

    private sealed class SplitNotesIntoTracksPopupState
    {
        public int NumberOfTracks = 2;
        public int EveryNotesAmount = 1;

        public void Reset()
        {
            NumberOfTracks = 2;
            EveryNotesAmount = 1;
        }
    }

    private sealed class GeneratePitchBendNotesPopupState
    {
        public bool DeleteOriginalTracks = false;

        public void Reset()
            => DeleteOriginalTracks = false;
    }
}
