using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.Commands.Guitar;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const string PrepareForPlaybackPopupStateKey = "auto-edit.prepare-for-playback.popup";
    private const string AutoEditPopupStateKey = "auto-edit.selected-tracks.popup";
    private const string AdaptToRangePopupStateKey = "forge.adapt-to-range.popup";
    private const string ApplyTrackNameTransposesPopupStateKey = "forge.apply-track-name-transposes.popup";
    private const string MergeGuitarToneTracksPopupStateKey = "forge.merge-guitar-tone-tracks.popup";
    private const string SplitChordsPopupStateKey = "forge.split-chords.popup";
    private const string LimitSimultaneousNotesPopupStateKey = "forge.limit-simultaneous-notes.popup";
    private const string StrumNotesPopupStateKey = "forge.strum-notes.popup";
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

    private static readonly string[] ChordTimingToleranceLabels =
    {
        "Exact starts",
        "Small timing drift",
        "Loose timing drift",
        "Custom ticks",
    };

    private static readonly string[] LimitModeLabels =
    {
        "Same-start chords",
        "Active overlaps",
    };

    private static readonly string[] NoteKeepPolicyLabels =
    {
        "Keep highest",
        "Keep lowest",
        "Keep middle",
    };

    private static readonly string[] StrumDirectionLabels =
    {
        "Low to high",
        "High to low",
        "Alternate",
    };

    private static readonly string[] RangeFitStrategyLabels =
    {
        "Move each note into range",
        "Lower high notes first",
        "Find the best octave",
        "Phrase-aware octave fit",
    };

    private static readonly string[] GuitarToneMergeChannelLayoutLabels =
    {
        "One guitar track",
        "Keep overlapping tones",
    };

    private static MidiForgeGuitarToneMergeChannelLayout GetGuitarToneMergeChannelLayout(int index)
        => index == 1
            ? MidiForgeGuitarToneMergeChannelLayout.SeparateChannels
            : MidiForgeGuitarToneMergeChannelLayout.SingleChannelToneSwitches;

    private static MidiForgeRangeFitStrategy GetRangeFitStrategy(int index)
        => index switch
        {
            1 => MidiForgeRangeFitStrategy.LowerHighNotesFirst,
            2 => MidiForgeRangeFitStrategy.BestOctaveFit,
            3 => MidiForgeRangeFitStrategy.PhraseAwareOctaveFit,
            _ => MidiForgeRangeFitStrategy.FitNotesIndividually,
        };

    private static int GetRangeFitStrategyIndex(MidiForgeRangeFitStrategy strategy)
        => strategy switch
        {
            MidiForgeRangeFitStrategy.LowerHighNotesFirst => 1,
            MidiForgeRangeFitStrategy.BestOctaveFit => 2,
            MidiForgeRangeFitStrategy.PhraseAwareOctaveFit => 3,
            _ => 0,
        };

    private static MidiForgeChordTimingToleranceOptions GetChordTimingToleranceOptions(
        int modeIndex,
        int customTicks)
        => new(
            modeIndex switch
            {
                1 => MidiForgeChordTimingToleranceMode.OneOver128Note,
                2 => MidiForgeChordTimingToleranceMode.OneOver64Note,
                3 => MidiForgeChordTimingToleranceMode.CustomTicks,
                _ => MidiForgeChordTimingToleranceMode.Exact,
            },
            Math.Max(0, customTicks));

    private static MidiForgeNoteKeepPolicy GetNoteKeepPolicy(int index)
        => index switch
        {
            1 => MidiForgeNoteKeepPolicy.Lowest,
            2 => MidiForgeNoteKeepPolicy.Middle,
            _ => MidiForgeNoteKeepPolicy.Highest,
        };

    private static MidiForgeStrumDirection GetStrumDirection(int index)
        => index switch
        {
            1 => MidiForgeStrumDirection.HighToLow,
            2 => MidiForgeStrumDirection.Alternate,
            _ => MidiForgeStrumDirection.LowToHigh,
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

    private LimitSimultaneousNotesPopupState GetLimitSimultaneousNotesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            LimitSimultaneousNotesPopupStateKey,
            static () => new LimitSimultaneousNotesPopupState());

    private StrumNotesPopupState GetStrumNotesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            StrumNotesPopupStateKey,
            static () => new StrumNotesPopupState());

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

    private static void DrawChordTimingToleranceControls(
        ref int modeIndex,
        ref int customTicks,
        string idSuffix)
    {
        modeIndex = int.Clamp(modeIndex, 0, ChordTimingToleranceLabels.Length - 1);
        ImGui.SetNextItemWidth(240f);
        ImGui.Combo($"Chord timing tolerance##{idSuffix}ChordTolerance", ref modeIndex,
            ChordTimingToleranceLabels, ChordTimingToleranceLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordTimingTolerance);

        if (modeIndex == 3)
        {
            ImGui.SetNextItemWidth(120f);
            ImGui.InputInt($"Custom tolerance ticks##{idSuffix}ChordToleranceTicks", ref customTicks);
            customTicks = int.Clamp(customTicks, 0, 9600);
        }
    }

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

        ImGui.Checkbox("Apply track-name transposes##prepareApplyTrackNameTransposes", ref state.ApplyTrackNameTransposes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ApplyTrackNameTransposes);

        ImGui.Checkbox("Map instruments##prepareMapInstruments", ref state.MapInstruments);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.PrepareMapInstruments);
        using (ImRaii.Disabled(!state.MapInstruments))
        {
            state.MapInstrumentsNameSourceIndex = int.Clamp(
                state.MapInstrumentsNameSourceIndex,
                0,
                MapInstrumentsNameSourceLabels.Length - 1);
            ImGui.SetNextItemWidth(240f);
            ImGui.Combo(
                "Name source##prepareMapInstrumentsNameSource",
                ref state.MapInstrumentsNameSourceIndex,
                MapInstrumentsNameSourceLabels,
                MapInstrumentsNameSourceLabels.Length);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.MapInstrumentsNameSource);

            state.MapInstrumentsModeIndex = int.Clamp(state.MapInstrumentsModeIndex, 0, MapInstrumentsModeLabels.Length - 1);
            ImGui.SetNextItemWidth(240f);
            ImGui.Combo(
                "Map mode##prepareMapInstrumentsMode",
                ref state.MapInstrumentsModeIndex,
                MapInstrumentsModeLabels,
                MapInstrumentsModeLabels.Length);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.MapInstrumentsMode);
        }

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

        DrawChordTimingToleranceControls(
            ref state.ChordTimingToleranceIndex,
            ref state.ChordTimingToleranceCustomTicks,
            "prepare");

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
                            ApplyTrackNameTransposes: state.ApplyTrackNameTransposes,
                            MapInstruments: state.MapInstruments,
                            MapInstrumentsMode: GetMapInstrumentsMode(state.MapInstrumentsModeIndex),
                            MapInstrumentsNameSource: GetMapInstrumentsNameSource(state.MapInstrumentsNameSourceIndex),
                            SplitDrumkits: state.SplitDrumkits,
                            MaxSimultaneousNotes: state.MaxSimultaneousNotes,
                            PickStrategy: state.PickStrategyIndex == 1
                                ? MidiForgeChordPickStrategy.OddChords
                                : MidiForgeChordPickStrategy.HighestChords,
                            RangeStrategy: GetRangeFitStrategy(state.RangeStrategyIndex),
                            ChordTimingTolerance: GetChordTimingToleranceOptions(
                                state.ChordTimingToleranceIndex,
                                state.ChordTimingToleranceCustomTicks))));

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
        var state = GetMergeGuitarToneTracksPopupState();
        state.ChannelLayoutIndex = int.Clamp(
            state.ChannelLayoutIndex,
            0,
            GuitarToneMergeChannelLayoutLabels.Length - 1);
        var channelLayout = GetGuitarToneMergeChannelLayout(state.ChannelLayoutIndex);
        var tooManyTracks = channelLayout == MidiForgeGuitarToneMergeChannelLayout.SeparateChannels &&
                            toneResolution.ExceedsMaximumResolvedTracks;

        ImGui.Text("Merge Guitar Tone Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.MergeGuitarToneTracks);

        ImGui.SetNextItemWidth(260f);
        ImGui.Combo(
            "Merge style##mergeGuitarToneChannelLayout",
            ref state.ChannelLayoutIndex,
            GuitarToneMergeChannelLayoutLabels,
            GuitarToneMergeChannelLayoutLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.MergeGuitarToneChannelLayout);

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
                            DeleteOriginalTracks: state.DeleteOriginalTracks,
                            ChannelLayout: channelLayout)));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    DalamudApi.PrintError(result.Message);
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
                null,
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

        DrawChordTimingToleranceControls(
            ref state.ChordTimingToleranceIndex,
            ref state.ChordTimingToleranceCustomTicks,
            "autoEdit");

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
                            RangeStrategy: GetRangeFitStrategy(state.RangeStrategyIndex),
                            ChordTimingTolerance: GetChordTimingToleranceOptions(
                                state.ChordTimingToleranceIndex,
                                state.ChordTimingToleranceCustomTicks))));

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

        DrawChordTimingToleranceControls(
            ref state.ChordTimingToleranceIndex,
            ref state.ChordTimingToleranceCustomTicks,
            "splitChords");

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
                            InsertPartsAtEnd: state.InsertPartsAtEnd,
                            ChordTimingTolerance: GetChordTimingToleranceOptions(
                                state.ChordTimingToleranceIndex,
                                state.ChordTimingToleranceCustomTicks))));

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

    private void DrawLimitSimultaneousNotesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##LimitSimultaneousNotesPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetLimitSimultaneousNotesPopupState();
        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Limit Simultaneous Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.LimitSimultaneousNotes);

        ImGui.SetNextItemWidth(240f);
        state.LimitModeIndex = int.Clamp(state.LimitModeIndex, 0, LimitModeLabels.Length - 1);
        ImGui.Combo("Limit mode##limitSimultaneousMode", ref state.LimitModeIndex,
            LimitModeLabels, LimitModeLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.LimitSimultaneousMode);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum active notes##limitSimultaneousMax", ref state.MaximumActiveNotes);
        state.MaximumActiveNotes = int.Clamp(state.MaximumActiveNotes, 1, 8);

        ImGui.SetNextItemWidth(240f);
        state.KeepPolicyIndex = int.Clamp(state.KeepPolicyIndex, 0, NoteKeepPolicyLabels.Length - 1);
        ImGui.Combo("Keep##limitSimultaneousKeep", ref state.KeepPolicyIndex,
            NoteKeepPolicyLabels, NoteKeepPolicyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.NoteKeepPolicy);

        ImGui.Checkbox("Create limited tracks (keep originals)##limitSimultaneousCreateNew", ref state.CreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doLimitSimultaneous"))
            {
                var result = _editorCommandExecutor.Execute(
                    new LimitSimultaneousNotesCommand(),
                    CreateEditorCommandContext(),
                    new LimitSimultaneousNotesCommandOptions(
                        validIndices,
                        new MidiForgeLimitSimultaneousNotesOptions(
                            CreateNewTracks: state.CreateNewTracks,
                            LimitMode: state.LimitModeIndex == 0
                                ? MidiForgeSimultaneousLimitMode.SameStartChordsOnly
                                : MidiForgeSimultaneousLimitMode.ActiveOverlaps,
                            MaximumActiveNotes: state.MaximumActiveNotes,
                            KeepPolicy: GetNoteKeepPolicy(state.KeepPolicyIndex))));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    if (!string.IsNullOrWhiteSpace(result.Result?.UserMessage))
                        DalamudApi.PrintEcho(result.Result.UserMessage);
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelLimitSimultaneous"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawStrumNotesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##StrumNotesPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetStrumNotesPopupState();
        var selectedNoteKeys = GetSelectedNoteKeys();
        var validIndices = GetSelectedPerformanceTrackIndices();
        var hasSelectedNotes = selectedNoteKeys.Count > 0;

        ImGui.Text("Strum Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.StrumNotes);

        ImGui.SetNextItemWidth(240f);
        state.DirectionIndex = int.Clamp(state.DirectionIndex, 0, StrumDirectionLabels.Length - 1);
        ImGui.Combo("Direction##strumDirection", ref state.DirectionIndex,
            StrumDirectionLabels, StrumDirectionLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.StrumDirection);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Step ticks##strumStepTicks", ref state.StepTicks);
        state.StepTicks = int.Clamp(state.StepTicks, 0, 9600);

        DrawChordTimingToleranceControls(
            ref state.ChordTimingToleranceIndex,
            ref state.ChordTimingToleranceCustomTicks,
            "strum");

        ImGui.Checkbox("Preserve note ends##strumPreserveEnds", ref state.PreserveNoteEnds);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.StrumPreserveEnds);

        using (ImRaii.Disabled(hasSelectedNotes))
        {
            ImGui.Checkbox("Create strummed tracks (keep originals)##strumCreateNew", ref state.CreateNewTracks);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);

            ImGui.Checkbox("Use start tick##strumUseStartTick", ref state.UseStartTick);
            using (ImRaii.Disabled(!state.UseStartTick))
            {
                ImGui.SetNextItemWidth(120f);
                ImGui.InputInt("Start tick##strumStartTick", ref state.StartTick);
                state.StartTick = Math.Max(0, state.StartTick);
            }

            ImGui.Checkbox("Use end tick##strumUseEndTick", ref state.UseEndTick);
            using (ImRaii.Disabled(!state.UseEndTick))
            {
                ImGui.SetNextItemWidth(120f);
                ImGui.InputInt("End tick##strumEndTick", ref state.EndTick);
                state.EndTick = Math.Max(0, state.EndTick);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled(hasSelectedNotes
            ? $"{selectedNoteKeys.Count} selected note(s)"
            : $"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(!hasSelectedNotes && validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doStrumNotes"))
            {
                var result = _editorCommandExecutor.Execute(
                    new StrumNotesCommand(),
                    CreateEditorCommandContext(),
                    new StrumNotesCommandOptions(
                        validIndices,
                        _selectedTrackIndex,
                        selectedNoteKeys,
                        new MidiForgeStrumNotesOptions(
                            CreateNewTracks: state.CreateNewTracks,
                            Direction: GetStrumDirection(state.DirectionIndex),
                            StepTicks: state.StepTicks,
                            PreserveNoteEnds: state.PreserveNoteEnds,
                            StartTick: state.UseStartTick ? (long?)state.StartTick : null,
                            EndTick: state.UseEndTick ? (long?)state.EndTick : null,
                            ChordTimingTolerance: GetChordTimingToleranceOptions(
                                state.ChordTimingToleranceIndex,
                                state.ChordTimingToleranceCustomTicks))));

                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelStrumNotes"))
            ImGui.CloseCurrentPopup();
    }

    private IReadOnlyList<NoteSelectionKey> GetSelectedNoteKeys()
        => _selectedEventIndices
            .Where(index => CurrentEvents is { Count: var count } && (uint)index < (uint)count)
            .Select(TryCreateNoteSelectionKey)
            .Where(key => key.HasValue)
            .Select(key => key.Value)
            .ToArray();

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
        ImGui.InputText("Minimum note##splitToneMin", ref state.MinimumNote, 16);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitToneMinimumNote);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputText("Maximum note##splitToneMax", ref state.MaximumNote, 16);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitToneMaximumNote);

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
        public bool ApplyTrackNameTransposes = true;
        public bool MapInstruments = true;
        public int MapInstrumentsNameSourceIndex = 0;
        public int MapInstrumentsModeIndex = 1;
        public bool SplitDrumkits = true;
        public int MaxSimultaneousNotes = 3;
        public int PickStrategyIndex = 0;
        public int RangeStrategyIndex = GetRangeFitStrategyIndex(MidiForgeRangeFitStrategy.BestOctaveFit);
        public int ChordTimingToleranceIndex = 1;
        public int ChordTimingToleranceCustomTicks = 0;
    }

    private sealed class AutoEditPopupState
    {
        public int MaxSimultaneousNotes = 3;
        public int PickStrategyIndex = 0;
        public int RangeStrategyIndex = GetRangeFitStrategyIndex(MidiForgeRangeFitStrategy.BestOctaveFit);
        public int ChordTimingToleranceIndex = 1;
        public int ChordTimingToleranceCustomTicks = 0;
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
        public int ChannelLayoutIndex = 0;

        public void Reset()
        {
            DeleteOriginalTracks = false;
            ChannelLayoutIndex = 0;
        }
    }

    private sealed class SplitChordsPopupState
    {
        public int StrategyIndex = 0;
        public int GroupModeIndex = 0;
        public int ChordTimingToleranceIndex = 0;
        public int ChordTimingToleranceCustomTicks = 0;
        public int MinimumSimultaneousNotes = 2;
        public bool InsertPartsAtEnd = true;

        public void Reset()
        {
            StrategyIndex = 0;
            GroupModeIndex = 0;
            ChordTimingToleranceIndex = 0;
            ChordTimingToleranceCustomTicks = 0;
            MinimumSimultaneousNotes = 2;
            InsertPartsAtEnd = true;
        }
    }

    private sealed class LimitSimultaneousNotesPopupState
    {
        public int LimitModeIndex = 1;
        public int MaximumActiveNotes = 1;
        public int KeepPolicyIndex = 0;
        public bool CreateNewTracks = true;

        public void Reset()
        {
            LimitModeIndex = 1;
            MaximumActiveNotes = 1;
            KeepPolicyIndex = 0;
            CreateNewTracks = true;
        }
    }

    private sealed class StrumNotesPopupState
    {
        public int DirectionIndex = 0;
        public int StepTicks = 5;
        public int ChordTimingToleranceIndex = 0;
        public int ChordTimingToleranceCustomTicks = 0;
        public bool PreserveNoteEnds = true;
        public bool CreateNewTracks = true;
        public bool UseStartTick = false;
        public bool UseEndTick = false;
        public int StartTick = 0;
        public int EndTick = 0;

        public void Reset()
        {
            DirectionIndex = 0;
            StepTicks = 5;
            ChordTimingToleranceIndex = 0;
            ChordTimingToleranceCustomTicks = 0;
            PreserveNoteEnds = true;
            CreateNewTracks = true;
            UseStartTick = false;
            UseEndTick = false;
            StartTick = 0;
            EndTick = 0;
        }
    }

    private sealed class SplitToneRangePopupState
    {
        public string MinimumNote = MidiForgeNotePrimitives.GetMidiNoteName(MidiForgeAnalysis.PlayableLowestMidiNote);
        public string MaximumNote = MidiForgeNotePrimitives.GetMidiNoteName(MidiForgeAnalysis.PlayableHighestMidiNote);

        public void Reset()
        {
            MinimumNote = MidiForgeNotePrimitives.GetMidiNoteName(MidiForgeAnalysis.PlayableLowestMidiNote);
            MaximumNote = MidiForgeNotePrimitives.GetMidiNoteName(MidiForgeAnalysis.PlayableHighestMidiNote);
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

    // ==================== New Operation Popups ====================

    private const string GlueNotesPopupStateKey = "note.glue-same-pitch.popup";
    private const string SplitAtPositionPopupStateKey = "note.split-at-position.popup";
    private const string RepeatLoopPopupStateKey = "note.repeat-loop.popup";
    private const string InsertMeasuresPopupStateKey = "file.insert-measures.popup";
    private const string DeleteMeasuresPopupStateKey = "file.delete-measures.popup";

    private static readonly string[] RepeatLoopIntervalLabels =
    {
        "1/2 Bar",
        "1 Bar",
        "2 Bars",
        "4 Bars",
        "1 Beat",
        "2 Beats",
        "4 Beats",
    };

    private static readonly MidiForgeRepeatLoopInterval[] RepeatLoopIntervalValues =
    {
        MidiForgeRepeatLoopInterval.HalfBar,
        MidiForgeRepeatLoopInterval.OneBar,
        MidiForgeRepeatLoopInterval.TwoBars,
        MidiForgeRepeatLoopInterval.FourBars,
        MidiForgeRepeatLoopInterval.OneBeat,
        MidiForgeRepeatLoopInterval.TwoBeats,
        MidiForgeRepeatLoopInterval.FourBeats,
    };

    private static readonly string[] RepeatLoopEndConditionLabels =
    {
        "End of song",
        "Until next note on track",
        "Until tick",
        "Repeat count",
    };

    private static readonly MidiForgeRepeatLoopEndCondition[] RepeatLoopEndConditionValues =
    {
        MidiForgeRepeatLoopEndCondition.EndOfSong,
        MidiForgeRepeatLoopEndCondition.UntilNextNoteOnTrack,
        MidiForgeRepeatLoopEndCondition.UntilTick,
        MidiForgeRepeatLoopEndCondition.RepeatCount,
    };

    // --- Glue Notes ---

    private GlueNotesPopupState GetGlueNotesPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(GlueNotesPopupStateKey, static () => new GlueNotesPopupState());

    private void DrawGlueNotesPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##GlueNotesPopup");
        if (!popup) return;
        if (_file == null) return;

        var selectedNoteKeys = GetSelectedNoteKeys();

        ImGui.Text("Glue Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.GlueNotes);

        ImGui.Spacing();
        ImGui.TextDisabled($"{selectedNoteKeys.Count} selected note(s) on track {_selectedTrackIndex}");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(selectedNoteKeys.Count < 2))
        {
            if (ImGuiUtil.SuccessButton("Apply##doGlueNotes"))
            {
                var result = _editorCommandExecutor.Execute(
                    new GlueNotesCommand(),
                    CreateEditorCommandContext(),
                    new GlueNotesOptions(_selectedTrackIndex, selectedNoteKeys));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##cancelGlueNotes"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class GlueNotesPopupState { }

    // --- Split at Position ---

    private SplitAtPositionPopupState GetSplitAtPositionPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(SplitAtPositionPopupStateKey, static () => new SplitAtPositionPopupState());

    private void DrawSplitAtPositionPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##SplitAtPositionPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetSplitAtPositionPopupState();

        ImGui.Text("Split Notes at Position");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitAtPosition);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Split tick##splitPosTick", ref state.SplitTick);
        state.SplitTick = Math.Max(1, state.SplitTick);

        ImGui.Spacing();
        ImGui.TextDisabled($"Split all notes on track {_selectedTrackIndex} that span tick {state.SplitTick}");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(_selectedTrackIndex < 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitAtPosition"))
            {
                var result = _editorCommandExecutor.Execute(
                    new SplitAtPositionCommand(),
                    CreateEditorCommandContext(),
                    new SplitAtPositionOptions(_selectedTrackIndex, state.SplitTick));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##cancelSplitAtPosition"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class SplitAtPositionPopupState
    {
        public int SplitTick = 0;
    }

    // --- Repeat Selected Notes ---

    private RepeatLoopPopupState GetRepeatLoopPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(RepeatLoopPopupStateKey, static () => new RepeatLoopPopupState());

    private void DrawRepeatLoopPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##RepeatLoopPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetRepeatLoopPopupState();
        var selectedNoteKeys = GetSelectedNoteKeys();

        ImGui.Text("Repeat Selected Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.RepeatLoop);

        state.IntervalIndex = int.Clamp(state.IntervalIndex, 0, RepeatLoopIntervalLabels.Length - 1);
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("Interval##repeatInterval", ref state.IntervalIndex,
            RepeatLoopIntervalLabels, RepeatLoopIntervalLabels.Length);

        state.EndConditionIndex = int.Clamp(state.EndConditionIndex, 0, RepeatLoopEndConditionLabels.Length - 1);
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        ImGui.Combo("End condition##repeatEndCondition", ref state.EndConditionIndex,
            RepeatLoopEndConditionLabels, RepeatLoopEndConditionLabels.Length);

        var endCondition = RepeatLoopEndConditionValues[state.EndConditionIndex];
        if (endCondition == MidiForgeRepeatLoopEndCondition.RepeatCount)
        {
            ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
            ImGui.InputInt("Repeat count##repeatCount", ref state.RepeatCount);
            state.RepeatCount = int.Clamp(state.RepeatCount, 1, 128);
        }
        else if (endCondition == MidiForgeRepeatLoopEndCondition.UntilTick)
        {
            ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
            ImGui.InputInt("End tick##repeatEndTick", ref state.EndTick);
            state.EndTick = Math.Max(0, state.EndTick);
        }

        ImGui.Checkbox("Trim to fit##repeatTrimToFit", ref state.TrimToFit);
        ImGuiUtil.ToolTip("Skip looped notes that would overlap existing notes of the same pitch on the same track.");

        ImGui.Spacing();
        ImGui.TextDisabled($"{selectedNoteKeys.Count} selected note(s) on track {_selectedTrackIndex}");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(selectedNoteKeys.Count == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doRepeatLoop"))
            {
                var interval = RepeatLoopIntervalValues[state.IntervalIndex];
                var result = _editorCommandExecutor.Execute(
                    new RepeatLoopCommand(),
                    CreateEditorCommandContext(),
                    new RepeatLoopOptions(
                        _selectedTrackIndex,
                        selectedNoteKeys,
                        interval,
                        endCondition,
                        state.RepeatCount,
                        state.EndTick,
                        state.TrimToFit));
                if (result.Succeeded)
                {
                    ApplyEditorCommandRefreshHints();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##cancelRepeatLoop"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class RepeatLoopPopupState
    {
        public int IntervalIndex = 1;
        public int EndConditionIndex = 1;
        public int RepeatCount = 4;
        public int EndTick = 0;
        public bool TrimToFit = true;
    }

    // --- Insert Measures ---

    private InsertMeasuresPopupState GetInsertMeasuresPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(InsertMeasuresPopupStateKey, static () => new InsertMeasuresPopupState());

    private void DrawInsertMeasuresPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##InsertMeasuresPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetInsertMeasuresPopupState();

        ImGui.Text("Insert Measures");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.InsertMeasures);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("After measure##insertAfterMeasure", ref state.AfterMeasure);
        state.AfterMeasure = Math.Max(0, state.AfterMeasure);

        ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Count##insertMeasureCount", ref state.MeasureCount);
        state.MeasureCount = int.Clamp(state.MeasureCount, 1, 256);

        ImGui.Checkbox("Shift tempo events##insertShiftTempo", ref state.ShiftTempoEvents);
        ImGui.Checkbox("Shift time signature events##insertShiftTimeSig", ref state.ShiftTimeSigEvents);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doInsertMeasures"))
        {
            var result = _editorCommandExecutor.Execute(
                new InsertMeasuresCommand(),
                CreateEditorCommandContext(),
                new InsertMeasuresOptions(
                    state.AfterMeasure,
                    state.MeasureCount,
                    state.ShiftTempoEvents,
                    state.ShiftTimeSigEvents));
            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##cancelInsertMeasures"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class InsertMeasuresPopupState
    {
        public int AfterMeasure = 0;
        public int MeasureCount = 4;
        public bool ShiftTempoEvents = true;
        public bool ShiftTimeSigEvents = true;
    }

    // --- Delete Measures ---

    private DeleteMeasuresPopupState GetDeleteMeasuresPopupState()
        => _editorCommandSession.PopupStates.GetOrCreate(DeleteMeasuresPopupStateKey, static () => new DeleteMeasuresPopupState());

    private void DrawDeleteMeasuresPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##DeleteMeasuresPopup");
        if (!popup) return;
        if (_file == null) return;

        var state = GetDeleteMeasuresPopupState();

        ImGui.Text("Delete Measures");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.DeleteMeasures);

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Start measure##deleteStartMeasure", ref state.StartMeasure);
        state.StartMeasure = Math.Max(1, state.StartMeasure);

        ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Count##deleteMeasureCount", ref state.MeasureCount);
        state.MeasureCount = int.Clamp(state.MeasureCount, 1, 256);

        ImGui.Checkbox("Shift tempo events##deleteShiftTempo", ref state.ShiftTempoEvents);
        ImGui.Checkbox("Shift time signature events##deleteShiftTimeSig", ref state.ShiftTimeSigEvents);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Apply##doDeleteMeasures"))
        {
            var result = _editorCommandExecutor.Execute(
                new DeleteMeasuresCommand(),
                CreateEditorCommandContext(),
                new DeleteMeasuresOptions(
                    state.StartMeasure,
                    state.MeasureCount,
                    state.ShiftTempoEvents,
                    state.ShiftTimeSigEvents));
            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##cancelDeleteMeasures"))
            ImGui.CloseCurrentPopup();
    }

    private sealed class DeleteMeasuresPopupState
    {
        public int StartMeasure = 1;
        public int MeasureCount = 1;
        public bool ShiftTempoEvents = true;
        public bool ShiftTimeSigEvents = true;
    }
}
