using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Extensions.Dalamud;
using MidiBard.Util;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawTrackListPanel()
    {
        var available = ImGui.GetContentRegionAvail();
        using var child = ImRaii.Child("##TrackListChild", available, false);
        if (!child) return;

        var frameH = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var actsWidth = frameH * 3 + spacing * 2;
        var scale = ImGuiHelpers.GlobalScale;
        var fixedNR = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
                       | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit
                       | ImGuiTableFlags.ScrollY;

        var tableAvailable = ImGui.GetContentRegionAvail();
        if (!ImGui.BeginTable("##TrackTable", 7, tableFlags, tableAvailable)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##chk", fixedNR, frameH);
        ImGui.TableSetupColumn("##color", fixedNR, 20f * scale);
        ImGui.TableSetupColumn("#", fixedNR, 28f * scale);
        ImGui.TableSetupColumn("##diag", fixedNR, frameH);
        ImGui.TableSetupColumn("Track", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Ch", fixedNR, 28f * scale);
        ImGui.TableSetupColumn("##acts", fixedNR, actsWidth);

        // Manual header row with global checkbox in the ##chk column
        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableNextColumn();
        if (ImGui.Checkbox("##GlobTrackChk", ref _globalTracksChecked))
        {
            if (_globalTracksChecked) SelectAllTracks();
            else ClearTrackSelection();
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TrackSelectAll);

        ImGui.TableNextColumn(); // ##color header

        ImGui.TableNextColumn();
        ImGui.Text("#");

        ImGui.TableNextColumn(); // ##diag header

        ImGui.TableNextColumn();
        ImGui.Text("Name");

        ImGui.TableNextColumn();
        ImGui.Text("Ch");

        ImGui.TableNextColumn();
        if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Eye, "##ToggleAllTracksVisibility", MidiEditorOperationHelp.ToggleAllTracksVisibility))
            ToggleAllTracksVisibility();

        // Batch action bar
        using (ImRaii.Disabled(_selectedTrackIndices.Count == 0))
        {
            ImGui.SameLine();
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Eraser, "##clearTrackSel", MidiEditorOperationHelp.TrackClearSelection))
                ClearTrackSelection();

            ImGui.SameLine();
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Trash, "##batchDelTracks",
               MidiEditorOperationHelp.TrackDeleteSelected))
            {
                if (ImGui.GetIO().KeyCtrl)
                    DeleteSelectedTracks();
            }
        }

        var tracks = _file!.Tracks;
        for (int i = 0; i < tracks.Count; i++)
            DrawTrackEntry(tracks[i], i);

        ImGui.EndTable();
    }

    private void DrawTrackEntry(EditableTrack track, int index)
    {
        ImGui.TableNextRow();
        ImGui.PushID(index);

        var isRowSelected = _selectedTrackIndex == index;
        bool isEditingThis = _editingTrack == track;
        bool anyEditing = _editingTrack != null;
        int trackCount = _file?.Tracks.Count ?? 1;
        var displayState = (_previewTracks != null && index < _previewTracks.Length) ? _previewTracks[index] : null;

        //  Checkbox column - skipped for conductor track and during inline edit
        ImGui.TableNextColumn();
        if (!track.IsConductorTrack && !isEditingThis)
        {
            bool isChecked = _selectedTrackIndices.Contains(index);
            if (ImGui.Checkbox("##trkChk", ref isChecked))
            {
                if (isChecked) _selectedTrackIndices.Add(index);
                else _selectedTrackIndices.Remove(index);
            }
        }

        //  Color column
        ImGui.TableNextColumn();
        if (displayState != null && !track.IsConductorTrack)
        {
            var autoColor = PianoRollWindow.GetTrackColor(index, trackCount);
            var trackColor = displayState.Color ?? autoColor;
            if (ImGui.ColorButton($"##prevcol{index}", trackColor, ImGuiColorEditFlags.NoTooltip,
                new Vector2(16f * ImGuiHelpers.GlobalScale, 16f * ImGuiHelpers.GlobalScale)))
            {
                ImGui.OpenPopup($"##prevColorPicker{index}");
            }
            if (ImGui.BeginPopup($"##prevColorPicker{index}"))
            {
                var pickerColor = displayState.Color ?? autoColor;
                if (ImGui.ColorPicker4($"##prevpicker{index}", ref pickerColor, ImGuiColorEditFlags.AlphaBar))
                    displayState.Color = pickerColor;
                if (displayState.Color.HasValue && ImGui.Button("Reset##prevColorReset"))
                    displayState.Color = null;
                ImGui.EndPopup();
            }
        }

        //  # column
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetTrackDisplayNumber(_file!.Tracks, index));

        //  Diagnostics column
        ImGui.TableNextColumn();
        DrawTrackDiagnosticsIndicator(track);

        //  Name column
        ImGui.TableNextColumn();
        if (isEditingThis)
        {
            if (_editTrackFocusNext)
            {
                _trackNameAutocomplete.RequestOpen();
                _editTrackFocusNext = false;
            }
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            var iconDrawn = DrawResolvedTrackInstrumentIcon(track, index);
            if (iconDrawn)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            }
            bool confirmed = _trackNameAutocomplete.Draw(
                "##inlineTrackNameEdit",
                ref _editTrackName,
                GetTrackNameOptions(),
                i => i.DisplayName,
                i => i.IconId);
            if (confirmed)
            {
                SaveTrackName();
                ImGui.PopID();
                return;
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                _editingTrack = null;
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            var iconDrawn = DrawResolvedTrackInstrumentIcon(track, index);
            if (iconDrawn)
                ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered, isRowSelected)
               .Push(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered, isRowSelected)
               .Push(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered, isRowSelected)
               .Push(ImGuiCol.Text, Style.Colors.Blue, track.IsConductorTrack))
            {
                if (ImGui.Selectable($"{track.DisplayName}##DndTrack_{index}", isRowSelected,
                    ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                    SelectTrack(index);
            }

            ImGui.OpenPopupOnItemClick("##TrackContextMenu", ImGuiPopupFlags.MouseButtonRight);
            DrawTrackContextMenu(track, index);

            if (!track.IsConductorTrack && !anyEditing && ImGui.BeginDragDropSource())
            {
                unsafe
                {
                    int from = index;
                    ImGui.SetDragDropPayload("DND_MIDI_TRACK",
                        new System.ReadOnlySpan<byte>(&from, sizeof(int)), ImGuiCond.None);
                }
                ImGui.Text($"Track {index + 1}: {track.DisplayName}");
                ImGui.EndDragDropSource();
            }

            if (!track.IsConductorTrack && !anyEditing)
            {
                using (ImRaii.PushColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget))
                {
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("DND_MIDI_TRACK");
                        if (!payload.IsNull && payload.IsDelivery())
                        {
                            unsafe
                            {
                                int fromIdx = *(int*)payload.Data;
                                if (fromIdx != index)
                                {
                                    var result = _editorCommandExecutor.Execute(
                                        new ReorderTrackCommand(),
                                        CreateEditorCommandContext(),
                                        new ReorderTrackOptions(fromIdx, index));
                                    if (result.Succeeded)
                                    {
                                        if (_selectedTrackIndex == fromIdx)
                                            _selectedTrackIndex = index;
                                        ApplyEditorCommandRefreshHints();
                                    }
                                }
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                }
            }
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TrackDragToReorder);

        //  Channel column
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (track.IsConductorTrack)
        {
            ImGui.TextDisabled("-");
        }
        else
        {
            string chPopupId = $"##chPop_{index}";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.Selectable($"{track.Channel + 1}{chPopupId}", false, ImGuiSelectableFlags.None))
            {
                ImGui.OpenPopup(chPopupId);
            }
            ImGuiUtil.ToolTip(MidiEditorOperationHelp.TrackChangeChannel);

            using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
            {
                using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
                {
                    if (ImGui.BeginPopup(chPopupId))
                    {
                        for (int c = 0; c < 16; c++)
                        {
                            if (ImGui.Selectable($"Ch {c + 1}{(c + 1 == 10 ? " (Drums)" : "")}##chOpt_{index}_{c}", track.Channel == c))
                            {
                                if (track.Channel != c)
                                {
                                    var result = _editorCommandExecutor.Execute(
                                        new SetTrackChannelCommand(),
                                        CreateEditorCommandContext(),
                                        new SetTrackChannelOptions(index, c));
                                    if (result.Succeeded)
                                        ApplyEditorCommandRefreshHints();
                                }
                            }
                            if (track.Channel == c) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndPopup();
                    }
                }
            }
        }

        //  Actions column
        ImGui.TableNextColumn();
        if (isEditingThis)
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Check, "##saveTrackName", MidiEditorOperationHelp.TrackSaveName))
                SaveTrackName();

            ImGui.SameLine();

            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancelTrackName", MidiEditorOperationHelp.TrackCancelNameEdit))
                _editingTrack = null;
        }
        else
        {
            if (displayState != null)
            {
                using (ImRaii.Disabled(track.IsConductorTrack))
                {
                    // show hide button
                    bool isVisible = displayState.Visible;
                    var visibleIcon = isVisible ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash;
                    string visTooltip = isVisible
                        ? MidiEditorOperationHelp.TrackVisibleInPianoRoll
                        : MidiEditorOperationHelp.TrackHiddenInPianoRoll;
                    if (ImGuiUtil.IconButton(visibleIcon, "##ShwHideTrack", visTooltip))
                    {
                        displayState.Visible = !displayState.Visible;
                        RefreshPreviewVoiceLimits();
                    }

                    // ImGui.SameLine();
                    // // Lock button
                    // bool isLocked = displayState.IsLocked;

                    // var lockIcon = isLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                    // var lockTooltip = isLocked ? "Track locked (click to unlock)" : "Lock track (prevents note selection)";
                    // using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Red, isLocked))
                    // {
                    //     if (ImGuiUtil.IconButton(lockIcon, "##lockTrack", lockTooltip))
                    //         displayState.IsLocked = !isLocked;
                    // }
                }
            }

            ImGui.SameLine();

            using (ImRaii.Disabled(track.IsConductorTrack || anyEditing))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##editTrack", MidiEditorOperationHelp.TrackEditName))
                {
                    _editingTrack = track;
                    _editTrackName = track.Name;
                    _editTrackFocusNext = true;
                }

                ImGui.SameLine();

                if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##delTrack", MidiEditorOperationHelp.TrackDelete))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        var result = _editorCommandExecutor.Execute(
                            new DeleteTracksCommand(),
                            CreateEditorCommandContext(),
                            new DeleteTracksOptions(new[] { index }));
                        if (result.Succeeded)
                            ApplyEditorCommandRefreshHints();
                        ImGui.PopID();
                        return;
                    }
                }
            }
        }

        ImGui.PopID();
    }

    private void DrawTrackDiagnosticsIndicator(EditableTrack track)
    {
        if (track.IsConductorTrack) return;

        var analysis = GetTrackAnalysis(track);
        if (analysis == null) return;

        var mapProvider = CreateEditorMidiMapProvider();
        var warnings = MidiForgeAnalysis.GetTrackDiagnostics(analysis, mapProvider);
        var tooltipLines = MidiForgeAnalysis.GetTrackDiagnosticTooltipLines(analysis, mapProvider);

        ImGui.AlignTextToFramePadding();
        ImGuiUtil.TextIcon(FontAwesomeIcon.InfoCircle, warnings.Count > 0 ? Style.Colors.Yellow : Style.Colors.Gray);
        ImGuiUtil.ToolTip(string.Join("\n", tooltipLines));
    }

    private MidiForgeTrackAnalysis? GetTrackAnalysis(EditableTrack track)
    {
        if (_file == null) return null;

        if (!ReferenceEquals(_trackDiagnosticsFile, _file)
            || _trackDiagnosticsVersion != _file.Version
            || _trackDiagnosticsTrackCount != _file.Tracks.Count)
        {
            RefreshTrackDiagnosticsCache();
        }

        return _trackDiagnosticsByIndex.TryGetValue(track.Index, out var diagnostics)
            ? diagnostics
            : null;
    }

    private void RefreshTrackDiagnosticsCache()
    {
        if (_file == null)
        {
            _trackDiagnosticsFile = null;
            _trackDiagnosticsVersion = -1;
            _trackDiagnosticsTrackCount = -1;
            _trackDiagnosticsByIndex = new Dictionary<int, MidiForgeTrackAnalysis>();
            return;
        }

        _trackDiagnosticsFile = _file;
        _trackDiagnosticsVersion = _file.Version;
        _trackDiagnosticsTrackCount = _file.Tracks.Count;
        _trackDiagnosticsByIndex = _file.Tracks.ToDictionary(
            track => track.Index,
            MidiForgeAnalysis.AnalyzeTrack);
    }

    private bool DrawResolvedTrackInstrumentIcon(EditableTrack track, int index)
    {
        if (track.IsConductorTrack ||
            InstrumentHelper.Instruments == null ||
            InstrumentHelper.Instruments.Length == 0)
            return false;

        var instrumentId = _playbackPreview.GetResolvedInstrumentIdForTrack(index, track.Channel);
        var iconSize = ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight());
        if (instrumentId == null ||
            instrumentId == 0 ||
            instrumentId.Value >= (uint)InstrumentHelper.Instruments.Length)
        {
            uint undefinedInstrumentIconId = 60042;
            DalamudApi.TextureProvider.DrawIcon(undefinedInstrumentIconId, iconSize);
            return true;
        }

        var instrument = InstrumentHelper.Instruments[(int)instrumentId.Value];
        DalamudApi.TextureProvider.DrawIcon(instrument.IconId, iconSize);
        if (ImGui.IsItemHovered())
            ImGuiUtil.ToolTip(instrument.FFXIVDisplayName);

        return true;
    }

    internal static string GetTrackDisplayNumber(IReadOnlyList<EditableTrack> tracks, int index)
    {
        if ((uint)index >= (uint)tracks.Count)
            return "--";

        if (tracks[index].IsConductorTrack)
            return "00";

        var playableIndex = 0;
        for (var i = 0; i <= index; i++)
        {
            if (!tracks[i].IsConductorTrack)
                playableIndex++;
        }

        return $"{playableIndex:00}";
    }

    private void DrawTrackContextMenu(EditableTrack track, int index)
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##TrackContextMenu");
        if (!popup) return;

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal))
        {
            ImGui.Button(track.DisplayName, new Vector2(-1, 0));
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Clone Track", default, false, !track.IsConductorTrack))
        {
            var result = _editorCommandExecutor.Execute(
                new CloneTracksCommand(),
                CreateEditorCommandContext(),
                new CloneTracksOptions(new[] { index }));
            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
        }

        if (ImGui.MenuItem("Split by Channel", default, false, track.HasMultipleChannels))
        {
            var result = _editorCommandExecutor.Execute(
                new SplitTrackByChannelCommand(),
                CreateEditorCommandContext(),
                new SplitTrackByChannelOptions(index));
            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
        }

        var displayState = (_previewTracks != null && index < _previewTracks.Length) ? _previewTracks[index] : null;

        // lock track
        if (displayState == null)
            return;

        bool isLocked = displayState.IsLocked;
        var lockText = isLocked ? "Unlock Track" : "Lock Track";
        if (ImGui.MenuItem(lockText))
            displayState.IsLocked = !isLocked;

        bool adapted = displayState.ShowAdaptedNotes;
        if (ImGui.Checkbox($"Show Adapted Notes##ShowAdaptedNotes_{index}", ref adapted))
        {
            displayState.ShowAdaptedNotes = adapted;
        }
    }

    private void SaveTrackName()
    {
        if (_editingTrack == null) return;
        if (_editingTrack.Name == _editTrackName)
        {
            _editingTrack = null;
            return;
        }

        var result = _editorCommandExecutor.Execute(
            new RenameTrackCommand(),
            CreateEditorCommandContext(),
            new RenameTrackOptions(_editingTrack.Index, _editTrackName));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
        _editingTrack = null;
    }
}
