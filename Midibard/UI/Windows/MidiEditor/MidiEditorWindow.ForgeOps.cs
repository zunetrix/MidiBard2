using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Core;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Managers;

namespace MidiBard;

public partial class MidiEditorWindow
{
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

    private static readonly string[] AutoEditPickStrategyLabels =
    {
        "Highest chord lines",
        "Odd chord lines",
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

    private void DrawAdaptToRangePopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##AdaptToRangePopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = _selectedTrackIndices
            .Where(i => i >= 0 && i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Adapt Selected Tracks to C3-C6");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.AdaptToRange);

        ImGui.Checkbox("Create adapted tracks (keep originals)##adaptCreateNew", ref _adaptToRangeCreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);
        ImGui.SetNextItemWidth(240f);
        _adaptToRangeStrategyIndex = int.Clamp(_adaptToRangeStrategyIndex, 0, RangeFitStrategyLabels.Length - 1);
        ImGui.Combo("Range fit##adaptRangeStrategy", ref _adaptToRangeStrategyIndex,
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
                CaptureHistorySnapshot();
                var selectedTrackIndex = _selectedTrackIndex;
                var replacingSelectedTrack = !_adaptToRangeCreateNewTracks
                    && selectedTrackIndex >= 0
                    && validIndices.Contains(selectedTrackIndex);

                MidiForgeOperations.AdaptTracksToPlayableRange(
                    _file,
                    validIndices,
                    new MidiForgeAdaptToRangeOptions(
                        CreateNewTracks: _adaptToRangeCreateNewTracks,
                        RangeStrategy: GetRangeFitStrategy(_adaptToRangeStrategyIndex)));

                if (replacingSelectedTrack && selectedTrackIndex < _file.Tracks.Count)
                {
                    _file.Tracks[selectedTrackIndex].LoadEvents(_file.TempoMap);
                    _selectedEventIndices.Clear();
                    _globalEventsChecked = false;
                }

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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
            ref _applyTrackNameTransposeCreateNewTracks);
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
                CaptureHistorySnapshot();
                var selectedTrackIndex = _selectedTrackIndex;
                var replacingSelectedTrack = !_applyTrackNameTransposeCreateNewTracks
                    && selectedTrackIndex >= 0
                    && transposedIndices.Contains(selectedTrackIndex);

                var result = MidiForgeOperations.ApplyTrackNameTransposes(
                    _file,
                    transposedIndices,
                    new MidiForgeApplyTrackNameTransposeOptions(
                        CreateNewTracks: _applyTrackNameTransposeCreateNewTracks));

                if (replacingSelectedTrack && selectedTrackIndex < _file.Tracks.Count)
                {
                    _file.Tracks[selectedTrackIndex].LoadEvents(_file.TempoMap);
                    _selectedEventIndices.Clear();
                    _globalEventsChecked = false;
                }

                if (result.CreatedTracks > 0 || result.ReplacedTracks > 0)
                {
                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                }

                ImGui.CloseCurrentPopup();
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
        var toneByTrackIndex = ResolveSelectedGuitarToneTracks(validIndices);
        var resolvedCount = validIndices.Count(index => toneByTrackIndex.ContainsKey(index));
        var tooManyTracks = resolvedCount > MidiForgeOperations.MaximumGuitarToneMergeTracks;

        ImGui.Text("Merge Guitar Tone Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.MergeGuitarToneTracks);

        ImGui.Checkbox("Delete original tracks after merge##mergeGuitarToneDeleteOriginal",
            ref _mergeGuitarToneDeleteOriginalTracks);
        ImGuiUtil.ToolTip("When enabled, replaces the selected guitar tone tracks with the generated ProgramElectricGuitar track.");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.TextDisabled($"{resolvedCount} selected guitar tone track(s)");
        if (tooManyTracks)
            ImGui.TextDisabled($"Maximum mergeable guitar tone tracks: {MidiForgeOperations.MaximumGuitarToneMergeTracks}");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(resolvedCount == 0 || tooManyTracks))
        {
            if (ImGuiUtil.SuccessButton("Merge##doMergeGuitarToneTracks"))
            {
                CaptureHistorySnapshot();
                var result = MidiForgeOperations.MergeGuitarToneTracks(
                    _file,
                    validIndices,
                    new MidiForgeMergeGuitarToneTracksOptions(
                        toneByTrackIndex,
                        DeleteOriginalTracks: _mergeGuitarToneDeleteOriginalTracks));

                if (result.CreatedTracks > 0 || result.DeletedSourceTracks > 0)
                {
                    SelectTrack(-1);
                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                    _selectedEventIndices.Clear();
                    _globalEventsChecked = false;
                }

                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelMergeGuitarToneTracks"))
            ImGui.CloseCurrentPopup();
    }

    private Dictionary<int, int> ResolveSelectedGuitarToneTracks(int[] validIndices)
    {
        var result = new Dictionary<int, int>();
        if (_file == null)
            return result;

        var jsonConfig = TryLoadMatchingMidiFileConfig();

        foreach (var trackIndex in validIndices)
        {
            if (TryResolveCurrentOverrideTrackTone(trackIndex, out var tone) ||
                TryResolveJsonTrackTone(jsonConfig, trackIndex, out tone) ||
                MidiForgeOperations.TryResolveGuitarToneFromTrackName(_file.Tracks[trackIndex].Name, out tone) ||
                TryResolveTrackProgramTone(trackIndex, out tone))
            {
                result[trackIndex] = tone;
            }
        }

        return result;
    }

    private bool TryResolveCurrentOverrideTrackTone(int trackIndex, out int tone)
    {
        tone = 0;
        if (_file?.FilePath == null ||
            _plugin.CurrentBardPlayback?.FilePath == null ||
            _plugin.Config.GuitarToneMode != GuitarToneMode.OverrideByTrack ||
            !IsSamePath(_file.FilePath, _plugin.CurrentBardPlayback.FilePath) ||
            (uint)trackIndex >= (uint)_plugin.Config.TrackStatus.Length)
        {
            return false;
        }

        tone = Math.Clamp(_plugin.Config.TrackStatus[trackIndex].Tone, 0, 4);
        return true;
    }

    private MidiFileConfig? TryLoadMatchingMidiFileConfig()
    {
        if (_file?.FilePath == null)
            return null;

        var config = _plugin.MidiFileConfigManager.GetMidiConfigFromFile(_file.FilePath);
        if (config == null || config.Tracks.Count == 0)
            return null;

        foreach (var dbTrack in config.Tracks)
        {
            if (dbTrack.Index < 0 || dbTrack.Index >= _file.Tracks.Count)
                return null;

            if (!string.Equals(dbTrack.Name, _file.Tracks[dbTrack.Index].Name, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return config;
    }

    private bool TryResolveJsonTrackTone(MidiFileConfig? config, int trackIndex, out int tone)
    {
        tone = 0;
        if (config == null || (uint)trackIndex >= (uint)config.Tracks.Count)
            return false;

        var dbTrack = config.Tracks[trackIndex];
        if (dbTrack.Index != trackIndex)
            return false;

        return MidiForgeOperations.TryResolveGuitarToneFromInstrumentId(dbTrack.Instrument, out tone);
    }

    private bool TryResolveTrackProgramTone(int trackIndex, out int tone)
    {
        tone = 0;
        if (_file == null || (uint)trackIndex >= (uint)_file.Tracks.Count)
            return false;

        var programChange = _file.Tracks[trackIndex]
            .CloneCurrentChunk()
            .Events
            .OfType<ProgramChangeEvent>()
            .FirstOrDefault();

        return programChange != null &&
               MidiForgeOperations.TryResolveGuitarToneFromProgram(programChange.ProgramNumber, out tone);
    }

    private static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DrawAutoEditPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##AutoEditPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Auto Edit");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.AutoEdit);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Max simultaneous notes##autoEditMax", ref _autoEditMaxSimultaneousNotes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AutoEditMaxSimultaneousNotes);
        _autoEditMaxSimultaneousNotes = int.Clamp(_autoEditMaxSimultaneousNotes, 1, 3);

        ImGui.SetNextItemWidth(240f);
        ImGui.Combo("Chord line strategy##autoEditStrategy", ref _autoEditPickStrategyIndex,
            AutoEditPickStrategyLabels, AutoEditPickStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AutoEditPickStrategy);

        ImGui.Checkbox("Adapt out-of-range notes to C3-C6##autoEditAdaptRange", ref _autoEditAdaptOutOfRange);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.AdaptToRange);
        if (_autoEditAdaptOutOfRange)
        {
            ImGui.SetNextItemWidth(240f);
            _autoEditRangeStrategyIndex = int.Clamp(_autoEditRangeStrategyIndex, 0, RangeFitStrategyLabels.Length - 1);
            ImGui.Combo("Range fit##autoEditRangeStrategy", ref _autoEditRangeStrategyIndex,
                RangeFitStrategyLabels, RangeFitStrategyLabels.Length);
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.RangeFitStrategy);
        }

        ImGui.Checkbox("Create edited tracks (keep originals)##autoEditCreateNew", ref _autoEditCreateNewTracks);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.CreateNewTracks);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doAutoEdit"))
            {
                CaptureHistorySnapshot();
                var selectedTrackIndex = _selectedTrackIndex;
                var replacingSelectedTrack = !_autoEditCreateNewTracks
                    && selectedTrackIndex >= 0
                    && validIndices.Contains(selectedTrackIndex);

                MidiForgeOperations.AutoEditTracks(
                    _file,
                    validIndices,
                    new MidiForgeAutoEditOptions(
                        MaxSimultaneousNotes: _autoEditMaxSimultaneousNotes,
                        PickStrategy: _autoEditPickStrategyIndex == 1
                            ? MidiForgeChordPickStrategy.OddChords
                            : MidiForgeChordPickStrategy.HighestChords,
                        AdaptOutOfRangeNotes: _autoEditAdaptOutOfRange,
                        CreateNewTracks: _autoEditCreateNewTracks,
                        RangeStrategy: GetRangeFitStrategy(_autoEditRangeStrategyIndex)));

                if (replacingSelectedTrack && selectedTrackIndex < _file.Tracks.Count)
                {
                    _file.Tracks[selectedTrackIndex].LoadEvents(_file.TempoMap);
                    _selectedEventIndices.Clear();
                    _globalEventsChecked = false;
                }

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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

        var validIndices = _selectedTrackIndices
            .Where(i => i < _file.Tracks.Count && !_file.Tracks[i].IsConductorTrack)
            .OrderBy(i => i)
            .ToArray();

        ImGui.Text("Split Chords");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitChords);

        ImGui.SetNextItemWidth(240f);
        ImGui.Combo("Strategy##splitChordStrategy", ref _splitChordsStrategyIndex,
            SplitChordStrategyLabels, SplitChordStrategyLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordSplitStrategy);

        ImGui.SetNextItemWidth(240f);
        ImGui.Combo("Group mode##splitChordGroupMode", ref _splitChordsGroupModeIndex,
            SplitChordGroupModeLabels, SplitChordGroupModeLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordGroupMode);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Minimum simultaneous notes##splitChordMin", ref _splitChordsMinimumSimultaneousNotes);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.ChordMinimumSimultaneousNotes);
        _splitChordsMinimumSimultaneousNotes = int.Clamp(_splitChordsMinimumSimultaneousNotes, 2, 10);

        ImGui.Checkbox("Insert split tracks at end##splitChordInsertEnd", ref _splitChordsInsertPartsAtEnd);
        ImGuiUtil.ToolTip("When enabled, split chord tracks are appended after existing tracks. When disabled, they are inserted after each source track.");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitChords"))
            {
                CaptureHistorySnapshot();
                MidiForgeOperations.SplitTracksChords(
                    _file,
                    validIndices,
                    new MidiForgeSplitChordsOptions(
                        Strategy: _splitChordsStrategyIndex == 1
                            ? MidiForgeChordSplitStrategy.SameStartTickAndLength
                            : MidiForgeChordSplitStrategy.SameStartTick,
                        GroupMode: _splitChordsGroupModeIndex switch
                        {
                            1 => MidiForgeChordGroupMode.Individual,
                            2 => MidiForgeChordGroupMode.Group,
                            _ => MidiForgeChordGroupMode.GroupMerged,
                        },
                        MinimumSimultaneousNotes: _splitChordsMinimumSimultaneousNotes,
                        InsertPartsAtEnd: _splitChordsInsertPartsAtEnd));

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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

        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Notes by Tone Range");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitToneRange);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Minimum note##splitToneMin", ref _splitToneMinNote);
        ImGuiUtil.ToolTip("The lowest MIDI note number included in the in-range output track.");
        _splitToneMinNote = int.Clamp(_splitToneMinNote, 0, 127);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum note##splitToneMax", ref _splitToneMaxNote);
        ImGuiUtil.ToolTip("The highest MIDI note number included in the in-range output track.");
        _splitToneMaxNote = int.Clamp(_splitToneMaxNote, 0, 127);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitNotesByToneRange"))
            {
                CaptureHistorySnapshot();
                MidiForgeOperations.SplitTracksByToneRange(
                    _file,
                    validIndices,
                    new MidiForgeSplitToneRangeOptions(
                        MinimumNote: _splitToneMinNote,
                        MaximumNote: _splitToneMaxNote));

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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

        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Notes by Length Range");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitLengthRange);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Minimum ticks##splitLengthMin", ref _splitLengthMinTicks);
        ImGuiUtil.ToolTip("The shortest note duration included in the in-range output track.");
        if (_splitLengthMinTicks < 0)
            _splitLengthMinTicks = 0;

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum ticks##splitLengthMax", ref _splitLengthMaxTicks);
        ImGuiUtil.ToolTip("The longest note duration included in the in-range output track. Use 0 to match only zero-length notes.");
        if (_splitLengthMaxTicks < 0)
            _splitLengthMaxTicks = 0;

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitNotesByLengthRange"))
            {
                CaptureHistorySnapshot();
                MidiForgeOperations.SplitTracksByLengthRange(
                    _file,
                    validIndices,
                    new MidiForgeSplitLengthRangeOptions(
                        MinimumLengthTicks: _splitLengthMinTicks,
                        MaximumLengthTicks: _splitLengthMaxTicks));

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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

        CaptureHistorySnapshot();
        var result = MidiForgeOperations.SplitTracksOverlappedNotes(_file, validIndices);
        if (result.CreatedTracks > 0)
        {
            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
        }
    }

    private void TrimSelectedOverlappedSustainedNotes()
    {
        if (_file == null) return;

        var validIndices = GetSelectedPerformanceTrackIndices();
        if (validIndices.Length == 0) return;

        CaptureHistorySnapshot();
        var result = MidiForgeOperations.TrimOverlappedSustainedNotes(_file, validIndices);
        if (result.CreatedTracks > 0)
        {
            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
        }
    }

    private void DrawExtendNotesDurationPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##ExtendNotesDurationPopup");
        if (!popup) return;
        if (_file == null) return;

        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Extend Notes Duration");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.ExtendNotesDuration);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Maximum duration ticks (0 = unlimited)##extendMaxDuration", ref _extendNotesMaximumDurationTicks);
        ImGuiUtil.ToolTip("Caps each extended note duration. Set to 0 to extend to the next note without a fixed maximum.");
        if (_extendNotesMaximumDurationTicks < 0)
            _extendNotesMaximumDurationTicks = 0;

        ImGui.Checkbox("Respect empty measures##extendRespectEmptyMeasures", ref _extendNotesRespectEmptyMeasures);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.RespectEmptyMeasures);

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doExtendNotesDuration"))
            {
                CaptureHistorySnapshot();
                MidiForgeOperations.ExtendNotesDuration(
                    _file,
                    validIndices,
                    new MidiForgeExtendNotesDurationOptions(
                        MaximumDurationTicks: _extendNotesMaximumDurationTicks,
                        RespectEmptyMeasures: _extendNotesRespectEmptyMeasures));

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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

        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Equal Notes");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitEqualNotes);

        ImGui.Text("Target track:");
        ImGuiUtil.HelpMarker(MidiEditorOperationHelp.TargetTrack);
        DrawTargetTrackRadioButtons(validIndices, ref _splitEqualNotesTargetRelIdx, "splitEqualTarget");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length < 2))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitEqualNotes"))
            {
                var targetIdx = validIndices[_splitEqualNotesTargetRelIdx];
                CaptureHistorySnapshot();
                var result = MidiForgeOperations.SplitTracksEqualNotes(_file, validIndices, targetIdx);
                if (result.CreatedTracks > 0)
                {
                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                }

                ImGui.CloseCurrentPopup();
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

        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Difference Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.DifferenceTracks);

        ImGui.Text("Target track:");
        ImGuiUtil.HelpMarker(MidiEditorOperationHelp.TargetTrack);
        DrawTargetTrackRadioButtons(validIndices, ref _differenceTracksTargetRelIdx, "differenceTarget");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length < 2))
        {
            if (ImGuiUtil.SuccessButton("Apply##doDifferenceTracks"))
            {
                var targetIdx = validIndices[_differenceTracksTargetRelIdx];
                CaptureHistorySnapshot();
                var result = MidiForgeOperations.DifferenceTracks(_file, validIndices, targetIdx);
                if (result.CreatedTracks > 0)
                {
                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                }

                ImGui.CloseCurrentPopup();
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

        var validIndices = GetSelectedPerformanceTrackIndices();

        ImGui.Text("Split Notes Into Tracks");
        ImGui.Separator();
        ImGui.Spacing();
        MidiEditorOperationHelp.DrawDescription(MidiEditorOperationHelp.SplitNotesIntoTracks);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Number of tracks##splitIntoTracksCount", ref _splitIntoTracksNumberOfTracks);
        ImGuiUtil.ToolTip("How many generated tracks to distribute each source track's notes across.");
        _splitIntoTracksNumberOfTracks = int.Clamp(_splitIntoTracksNumberOfTracks, 1, 64);

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Every N notes##splitIntoTracksEvery", ref _splitIntoTracksEveryNotesAmount);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.SplitNotesIntoTracksEvery);
        if (_splitIntoTracksEveryNotesAmount < 1)
            _splitIntoTracksEveryNotesAmount = 1;

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected performance track(s)");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doSplitNotesIntoTracks"))
            {
                CaptureHistorySnapshot();
                var result = MidiForgeOperations.SplitNotesIntoTracks(
                    _file,
                    validIndices,
                    new MidiForgeSplitNotesIntoTracksOptions(
                        NumberOfTracks: _splitIntoTracksNumberOfTracks,
                        EveryNotesAmount: _splitIntoTracksEveryNotesAmount));
                if (result.CreatedTracks > 0)
                {
                    _selectedTrackIndices.Clear();
                    _globalTracksChecked = false;
                }

                ImGui.CloseCurrentPopup();
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
            ref _generatePitchBendDeleteOriginalTracks);
        ImGuiUtil.ToolTip("When enabled, replaces the source tracks. When disabled, generated note-segment tracks are inserted after the source tracks.");

        ImGui.Spacing();
        ImGui.TextDisabled($"{validIndices.Length} selected track(s) with pitch bend events");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(validIndices.Length == 0))
        {
            if (ImGuiUtil.SuccessButton("Apply##doGeneratePitchBendNotes"))
            {
                CaptureHistorySnapshot();
                var selectedTrackIndex = _selectedTrackIndex;
                var replacingSelectedTrack = _generatePitchBendDeleteOriginalTracks
                    && selectedTrackIndex >= 0
                    && validIndices.Contains(selectedTrackIndex);

                MidiForgeOperations.GeneratePitchBendNotes(
                    _file,
                    validIndices,
                    new MidiForgeGeneratePitchBendNotesOptions(
                        DeleteOriginalTracks: _generatePitchBendDeleteOriginalTracks));

                if (replacingSelectedTrack && selectedTrackIndex < _file.Tracks.Count)
                {
                    _file.Tracks[selectedTrackIndex].LoadEvents(_file.TempoMap);
                    _selectedEventIndices.Clear();
                    _globalEventsChecked = false;
                }

                _selectedTrackIndices.Clear();
                _globalTracksChecked = false;
                ImGui.CloseCurrentPopup();
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
}
