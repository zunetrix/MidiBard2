using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;
using MidiBard.Extensions.Dalamud;
using MidiBard.Util;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const float TrackInstrumentIconScale = 1.45f;
    private const float TrackRowMinHeight = 30f;
    private const float TrackBadgeSize = 11f;
    private const float TrackBadgeGap = 2f;
    private const float TrackInlineToolAlpha = 0.85f;

    private static Vector2 TrackInlineToolFramePadding()
        => new(2f * ImGuiHelpers.GlobalScale, 0f);

    private void DrawTrackListPanel()
    {
        var available = ImGui.GetContentRegionAvail();
        using var child = ImRaii.Child("##TrackListChild", available, false);
        if (!child) return;

        var frameH = ImGui.GetFrameHeight();
        var scale = ImGuiHelpers.GlobalScale;
        var fixedNR = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
                       | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit
                       | ImGuiTableFlags.ScrollY;

        var tableAvailable = ImGui.GetContentRegionAvail();
        using var table = ImRaii.Table("##TrackTable", 3, tableFlags, tableAvailable);
        if (!table) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##chk", fixedNR, frameH);
        ImGui.TableSetupColumn("##color", fixedNR, 20f * scale);
        ImGui.TableSetupColumn("Track", ImGuiTableColumnFlags.WidthStretch);

        // Manual header row with global checkbox in the ##chk column
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableNextColumn();
        if (ImGui.Checkbox("##GlobTrackChk", ref _globalTracksChecked))
        {
            if (_globalTracksChecked) SelectAllTracks();
            else ClearTrackSelection();
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TrackSelectAll);

        ImGui.TableNextColumn();

        ImGui.TableNextColumn();
        ImGui.Text("Name");
        ImGui.SameLine();
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

        // Ensure display numbers are built before the loop
        if (_trackDisplayNumbers == null || _trackDisplayNumbers.Length != tracks.Count)
            RebuildTrackDisplayNumbers();

        var clipper = new ImGuiListClipper();
        // Workaround for ImGui table clipper calculating rows short when a frozen header is present. Scroll wont show all tracks
        var frozenHeaderPaddingRows = 5;
        var rowHeight = MathF.Max(
            TrackRowMinHeight * scale,
            ImGui.GetFrameHeight() * TrackInstrumentIconScale + ImGui.GetStyle().CellPadding.Y * 2f);
        clipper.Begin(tracks.Count + frozenHeaderPaddingRows, rowHeight);
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= tracks.Count) break;
                DrawTrackEntry(tracks[i], i);
            }
        }
        clipper.End();
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

        //  Track details + actions column
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

            if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
            {
                SaveTrackName();
                ImGui.PopID();
                return;
            }

            if (!track.IsConductorTrack)
            {
                if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Check, "##saveTrackName", MidiEditorOperationHelp.TrackSaveName))
                    SaveTrackName();

                ImGui.SameLine();

                if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancelTrackName", MidiEditorOperationHelp.TrackCancelNameEdit))
                    _editingTrack = null;
            }
        }
        else
        {
            var rowMin = ImGui.GetCursorScreenPos();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var rowHovered = ImGui.IsMouseHoveringRect(rowMin, rowMin + new Vector2(rowWidth, GetTrackRowHeight()), true);
            var showTools = rowHovered && !track.IsConductorTrack && displayState != null;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4f * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            var trackNumber = GetCachedTrackDisplayNumber(index);
            var numberStartX = ImGui.GetCursorPosX();
            var numberWidth = MathF.Max(
                ImGui.CalcTextSize("00").X,
                ImGui.CalcTextSize(trackNumber).X);
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Components.TextDisabled))
                ImGui.TextUnformatted(trackNumber);
            ImGui.SameLine();
            ImGui.SetCursorPosX(numberStartX + numberWidth + ImGui.GetStyle().ItemInnerSpacing.X);

            var iconDrawn = DrawTrackNameInstrumentPicker(track, index);
            if (iconDrawn)
                ImGui.SameLine();

            var trackNameStartX = ImGui.GetCursorPosX();
            var style = ImGui.GetStyle();
            var scale = ImGuiHelpers.GlobalScale;

            var diagWidth = ImGui.GetFrameHeight();
            var channelWidth = track.IsConductorTrack
                ? 0f
                : ImGui.CalcTextSize(FormatTrackChannelLabel(9)).X + style.FramePadding.X * 2f;

            var hoverToolsWidth = showTools
                ? GetTrackHoverToolsWidth() + style.ItemSpacing.X
                : 0f;

            var baseTrailingWidth = diagWidth + style.ItemSpacing.X + channelWidth;

            var rightEdgeX = trackNameStartX + ImGui.GetContentRegionAvail().X;
            var minUsefulNameWidth = 32f * scale;
            var canShowTools = showTools &&
                rightEdgeX - trackNameStartX >= baseTrailingWidth + hoverToolsWidth + minUsefulNameWidth;

            if (!canShowTools)
                hoverToolsWidth = 0f;

            var showBadges = !canShowTools && displayState != null && (displayState.IsLocked || !displayState.Visible);
            var badgeWidth = showBadges
                ? GetTrackStateBadgeRailInlineWidth(displayState) + style.ItemSpacing.X
                : 0f;

            var trailingWidth =
                hoverToolsWidth +
                badgeWidth +
                baseTrailingWidth;

            var trailingStartX = MathF.Max(
                trackNameStartX,
                rightEdgeX - trailingWidth);

            var nameWidth = MathF.Max(
                0f,
                trailingStartX - trackNameStartX - style.ItemSpacing.X);

            var trackNameColor = Style.Components.Text;
            if (track.IsConductorTrack)
                trackNameColor = Style.Colors.Blue;
            else if (displayState != null && !displayState.Visible)
                trackNameColor = Vector4.Lerp(Style.Components.TextDisabled, Style.Components.Text, 0.45f);

            using (ImRaii.PushColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered, isRowSelected)
               .Push(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered, isRowSelected)
               .Push(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered, isRowSelected)
               .Push(ImGuiCol.Text, trackNameColor))
            {
                if (ImGui.Selectable($"{track.DisplayName}##DndTrack_{index}", isRowSelected,
                    ImGuiSelectableFlags.None, new Vector2(nameWidth, 0)))
                    SelectTrack(index);
            }

            var trackNameHovered = ImGui.IsItemHovered();
            if (!track.IsConductorTrack && !anyEditing && trackNameHovered && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var tooltip = MidiEditorOperationHelp.TrackDragToReorder
                    + "\nDouble-click to edit"
                    + "\nRight-click for track actions";
                ImGuiUtil.ToolTip(tooltip);
            }

            if (!track.IsConductorTrack && !anyEditing && trackNameHovered
                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                BeginTrackNameEdit(track);
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

            if (!track.IsConductorTrack)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(trailingStartX);

                if (canShowTools)
                {
                    DrawTrackInlineHoverTools(track, index, displayState!, anyEditing);
                    ImGui.SameLine();
                }
                else if (showBadges)
                {
                    DrawTrackStateBadgeRailInline(displayState);
                    ImGui.SameLine();
                }

                DrawTrackDiagnosticsIndicator(track);
                ImGui.SameLine();
                DrawTrackChannelPicker(track, index, channelWidth);
            }
        }

        ImGui.PopID();
    }

    private void DrawTrackChannelPicker(EditableTrack track, int index, float width)
    {
        string chPopupId = $"##chPop_{index}";
        if (ImGui.Selectable(
                $"{FormatTrackChannelLabel(track.Channel)}{chPopupId}",
                false,
                ImGuiSelectableFlags.None,
                new Vector2(width, 0f)))
            ImGui.OpenPopup(chPopupId);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.TrackChangeChannel);

        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        if (!ImGui.BeginPopup(chPopupId))
            return;

        for (int c = 0; c < 16; c++)
        {
            var optionLabel = $"{FormatTrackChannelLabel(c)}{(c + 1 == 10 ? " (Drums)" : "")}##chOpt_{index}_{c}";
            if (ImGui.Selectable(optionLabel, track.Channel == c))
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

    private static string FormatTrackChannelLabel(int zeroBasedChannel)
        => $"Ch {zeroBasedChannel + 1:00}";

    private void DrawTrackDiagnosticsIndicator(EditableTrack track)
    {
        if (track.IsConductorTrack) return;

        var analysis = GetTrackAnalysis(track);
        if (analysis == null) return;

        if (!_trackDiagnosticsStringsByIndex.TryGetValue(track.Index, out var strings))
            return;

        ImGui.AlignTextToFramePadding();
        ImGuiUtil.TextIcon(FontAwesomeIcon.InfoCircle, strings.Warnings.Count > 0 ? Style.Colors.Yellow : Style.Colors.Gray);
        ImGuiUtil.ToolTip(string.Join("\n", strings.TooltipLines));
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
            _trackDiagnosticsStringsByIndex = new Dictionary<int, (IReadOnlyList<string>, IReadOnlyList<string>)>();
            return;
        }

        _trackDiagnosticsFile = _file;
        _trackDiagnosticsVersion = _file.Version;
        _trackDiagnosticsTrackCount = _file.Tracks.Count;
        RebuildTrackDisplayNumbers();

        var analysisDict = new Dictionary<int, MidiForgeTrackAnalysis>();
        var stringsDict = new Dictionary<int, (IReadOnlyList<string>, IReadOnlyList<string>)>();
        var mapProvider = CreateEditorMidiMapProvider();

        foreach (var track in _file.Tracks)
        {
            var notes = track.Chunk.GetNotes().ToArray();
            var analysis = MidiForgeAnalysis.AnalyzeTrack(track, notes);
            analysisDict[track.Index] = analysis;
            var warnings = MidiForgeAnalysis.GetTrackDiagnostics(analysis, mapProvider);
            var tooltipLines = MidiForgeAnalysis.GetTrackDiagnosticTooltipLines(analysis, mapProvider);
            stringsDict[track.Index] = (warnings, tooltipLines);
        }

        _trackDiagnosticsByIndex = analysisDict;
        _trackDiagnosticsStringsByIndex = stringsDict;
    }

    private bool DrawResolvedTrackInstrumentIcon(EditableTrack track, int index)
    {
        if (!TryResolveTrackInstrumentIcon(track, index, out var iconId, out var instrumentName))
            return false;

        var iconSize = ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight() * TrackInstrumentIconScale);
        DalamudApi.TextureProvider.DrawIcon(iconId, iconSize);
        if (ImGui.IsItemHovered())
            ImGuiUtil.ToolTip(BuildTrackInstrumentIconTooltip(instrumentName, includePickerHelp: false));

        return true;
    }

    private bool DrawTrackNameInstrumentPicker(EditableTrack track, int index)
    {
        if (!TryResolveTrackInstrumentIcon(track, index, out var iconId, out var instrumentName))
            return false;

        _frameQuickPickerOptions ??= MidiEditorTrackNameOptions.GetQuickPickerOptions(GetTrackNameOptions());
        _framePickerItems ??= BuildTrackNamePickerItems(_frameQuickPickerOptions);
        var items = _framePickerItems;
        if (items.Count == 0)
        {
            var iconSize = ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight() * TrackInstrumentIconScale);
            DalamudApi.TextureProvider.DrawIcon(iconId, iconSize);
            if (ImGui.IsItemHovered())
                ImGuiUtil.ToolTip(BuildTrackInstrumentIconTooltip(instrumentName, includePickerHelp: false));
            return true;
        }

        if (UiComponents.IconGridPicker(
                $"##TrackNameInstrumentPopup_{index}",
                iconId,
                BuildTrackInstrumentIconTooltip(instrumentName, includePickerHelp: true),
                items,
                out var selectedValue))
        {
            var selectedIndex = (int)selectedValue;
            if ((uint)selectedIndex < (uint)_frameQuickPickerOptions.Count)
                RenameTrackFromInstrumentPicker(index, _frameQuickPickerOptions[selectedIndex].DisplayName);
        }

        return true;
    }

    private static void DrawTrackStateBadge(
        Vector2 min,
        float size,
        FontAwesomeIcon icon,
        Vector4 iconColor,
        string? tooltip = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var max = min + new Vector2(size, size);

        drawList.AddRectFilled(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.62f)),
            size * 0.25f);

        var iconFont = UiBuilder.IconFont;
        var targetFontSize = MathF.Max(1f, size * 0.88f);

        using var font = ImRaii.PushFont(iconFont);
        var text = icon.ToIconString();
        var textSize = ImGui.CalcTextSize(text);
        var scaleRatio = targetFontSize / ImGui.GetFontSize();
        var scaledSize = textSize * scaleRatio;
        var textPos = min + (new Vector2(size, size) - scaledSize) * 0.5f;

        drawList.AddText(
            iconFont,
            targetFontSize,
            textPos,
            ImGui.ColorConvertFloat4ToU32(iconColor),
            text);

        if (!string.IsNullOrWhiteSpace(tooltip) &&
            ImGui.IsMouseHoveringRect(min, max))
        {
            ImGuiUtil.ToolTip(tooltip);
        }
    }

    private static void DrawTrackStateBadgeRailInline(TrackDisplayState displayState)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var badgeSize = TrackBadgeSize * scale;
        var badgeGap = TrackBadgeGap * scale;
        var frameHeight = ImGui.GetFrameHeight();
        var y = ImGui.GetCursorScreenPos().Y + MathF.Max(0f, frameHeight - badgeSize) * 0.5f;

        if (displayState.IsLocked)
        {
            var cursor = ImGui.GetCursorScreenPos();
            DrawTrackStateBadge(new Vector2(cursor.X, y), badgeSize, FontAwesomeIcon.Lock, TrackLockBadgeIconColor(), "Locked");
            ImGui.Dummy(new Vector2(badgeSize, frameHeight));
            if (!displayState.Visible)
                ImGui.SameLine(0f, badgeGap);
        }

        if (!displayState.Visible)
        {
            var cursor = ImGui.GetCursorScreenPos();
            DrawTrackStateBadge(new Vector2(cursor.X, y), badgeSize, FontAwesomeIcon.EyeSlash, TrackHiddenBadgeIconColor(), "Hidden");
            ImGui.Dummy(new Vector2(badgeSize, frameHeight));
        }
    }

    private static float GetTrackStateBadgeRailInlineWidth(TrackDisplayState displayState)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var badgeSize = TrackBadgeSize * scale;
        var badgeGap = TrackBadgeGap * scale;
        var count = 0;
        if (displayState.IsLocked)
            count++;
        if (!displayState.Visible)
            count++;

        return count == 0
            ? 0f
            : badgeSize * count + badgeGap * (count - 1);
    }

    private void DrawTrackInlineHoverTools(
        EditableTrack track,
        int index,
        TrackDisplayState displayState,
        bool anyEditing)
    {
        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, TrackInlineToolFramePadding());

        var secondaryActionColor = Vector4.Lerp(
            Style.Components.TextDisabled,
            Style.Components.Text,
            TrackInlineToolAlpha);

        using var colors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f))
            .Push(ImGuiCol.ButtonHovered, Style.Components.FrameBgHovered with { W = 0.45f })
            .Push(ImGuiCol.ButtonActive, Style.Components.FrameBgActive with { W = 0.55f })
            .Push(ImGuiCol.Text, secondaryActionColor);

        if (displayState.IsLocked)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, TrackLockBadgeIconColor()))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Lock, "##lockTrack", "Track locked (click to unlock)"))
                    displayState.IsLocked = false;
            }
        }
        else if (ImGuiUtil.IconButton(FontAwesomeIcon.LockOpen, "##lockTrack", "Lock track (prevents note selection)"))
        {
            displayState.IsLocked = true;
        }

        ImGui.SameLine();

        var visibleIcon = displayState.Visible
            ? FontAwesomeIcon.Eye
            : FontAwesomeIcon.EyeSlash;

        var visTooltip = displayState.Visible
            ? MidiEditorOperationHelp.TrackVisibleInPianoRoll
            : MidiEditorOperationHelp.TrackHiddenInPianoRoll;

        if (ImGuiUtil.IconButton(visibleIcon, "##ShwHideTrack", visTooltip))
        {
            displayState.Visible = !displayState.Visible;
            RefreshPreviewVoiceLimits();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(anyEditing))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##editTrack", MidiEditorOperationHelp.TrackEditName))
            {
                _editingTrack = track;
                _editTrackName = track.Name;
                _editTrackFocusNext = true;
            }
        }
    }

    private static float GetTrackRowHeight()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var style = ImGui.GetStyle();
        var iconSize = ImGui.GetFrameHeight() * TrackInstrumentIconScale;
        return MathF.Max(
            TrackRowMinHeight * scale,
            iconSize + style.CellPadding.Y * 2f);
    }

    private static float GetTrackHoverToolsWidth()
    {
        var fp2 = TrackInlineToolFramePadding().X * 2f;
        var sp = ImGui.GetStyle().ItemSpacing.X;

        var lockW = ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.Lock).X + fp2;
        var eyeW = ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.Eye).X + fp2;
        var editW = ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.Edit).X + fp2;

        return lockW + sp + eyeW + sp + editW;
    }

    private static Vector4 TrackLockBadgeIconColor()
        => Vector4.Lerp(Style.Components.TextDisabled, Style.Colors.Red, 0.72f) with { W = 0.9f };

    private static Vector4 TrackHiddenBadgeIconColor()
        => Style.Components.ButtonBlueActive with { W = 0.9f };

    private bool TryResolveTrackInstrumentIcon(
        EditableTrack track,
        int index,
        out uint iconId,
        out string instrumentName)
    {
        iconId = MidiEditorTrackNameOptions.DefaultIconId;
        instrumentName = string.Empty;

        if (track.IsConductorTrack ||
            InstrumentHelper.Instruments == null ||
            InstrumentHelper.Instruments.Length == 0)
        {
            return false;
        }

        var instrumentId = _playbackPreview.GetResolvedInstrumentIdForTrack(index, track.Channel);
        if (instrumentId == null ||
            instrumentId == 0 ||
            instrumentId.Value >= (uint)InstrumentHelper.Instruments.Length)
        {
            return true;
        }

        var instrument = InstrumentHelper.Instruments[(int)instrumentId.Value];
        iconId = instrument.IconId;
        instrumentName = instrument.FFXIVDisplayName;
        return true;
    }

    private static string BuildTrackInstrumentIconTooltip(string instrumentName, bool includePickerHelp)
    {
        var text = string.IsNullOrWhiteSpace(instrumentName)
            ? MidiEditorOperationHelp.TrackUnknownInstrument
            : instrumentName;

        return includePickerHelp
            ? $"{text}\n{MidiEditorOperationHelp.TrackPickInstrumentName}"
            : text;
    }

    private static IReadOnlyList<IconPickerItem> BuildTrackNamePickerItems(
        IReadOnlyList<MidiEditorTrackNameOption> options)
    {
        var items = new List<IconPickerItem>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var instrumentId = option.PickerInstrumentId.GetValueOrDefault();
            items.Add(new IconPickerItem(
                (uint)i,
                option.IconId,
                option.DisplayName,
                UiComponents.IsInstrumentGroupBreak(instrumentId)));
        }

        return items;
    }

    private void RenameTrackFromInstrumentPicker(int trackIndex, string trackName)
    {
        if (_file == null || (uint)trackIndex >= (uint)_file.Tracks.Count)
            return;

        if (string.Equals(_file.Tracks[trackIndex].Name, trackName, System.StringComparison.Ordinal))
            return;

        var result = _editorCommandExecutor.Execute(
            new RenameTrackCommand(),
            CreateEditorCommandContext(),
            new RenameTrackOptions(trackIndex, trackName));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
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

    private void RebuildTrackDisplayNumbers()
    {
        if (_file == null)
        {
            _trackDisplayNumbers = null;
            return;
        }

        var numbers = new string[_file.Tracks.Count];
        int playableIndex = 0;
        for (int i = 0; i < _file.Tracks.Count; i++)
        {
            if (_file.Tracks[i].IsConductorTrack)
                numbers[i] = "00";
            else
                numbers[i] = $"{++playableIndex:00}";
        }

        _trackDisplayNumbers = numbers;
    }

    private string GetCachedTrackDisplayNumber(int index)
    {
        if (_trackDisplayNumbers != null && (uint)index < (uint)_trackDisplayNumbers.Length)
            return _trackDisplayNumbers[index];
        return "--";
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

        if (ImGui.MenuItem("Edit Name", default, false, !track.IsConductorTrack && _editingTrack == null))
            BeginTrackNameEdit(track);

        if (ImGui.MenuItem("Add Blank Track After", default, false, !track.IsConductorTrack))
            AddBlankTrackAfter(index);

        if (ImGui.MenuItem("Clone Track", default, false, !track.IsConductorTrack))
        {
            var result = _editorCommandExecutor.Execute(
                new CloneTracksCommand(),
                CreateEditorCommandContext(),
                new CloneTracksOptions(new[] { index }));
            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
        }

        DrawDeleteTrackMenuItem(index, track);

        ImGui.Separator();

        var displayState = (_previewTracks != null && index < _previewTracks.Length) ? _previewTracks[index] : null;
        if (displayState != null && !track.IsConductorTrack)
            DrawTrackDisplayStateMenuItems(displayState, index);

        ImGui.Separator();

        if (ImGui.MenuItem("Split by Channel", default, false, track.HasMultipleChannels))
        {
            var result = _editorCommandExecutor.Execute(
                new SplitTrackByChannelCommand(),
                CreateEditorCommandContext(),
                new SplitTrackByChannelOptions(index));
            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
        }

        if (ImGui.MenuItem("Transpose Up 1 Octave", default, false, !track.IsConductorTrack))
            TransposeTrackFromContextMenu(index, 12);

        if (ImGui.MenuItem("Transpose Down 1 Octave", default, false, !track.IsConductorTrack))
            TransposeTrackFromContextMenu(index, -12);
    }

    private void BeginTrackNameEdit(EditableTrack track)
    {
        _editingTrack = track;
        _editTrackName = track.Name;
        _editTrackFocusNext = true;
    }

    private void DrawDeleteTrackMenuItem(int index, EditableTrack track)
    {
        if (track.IsConductorTrack)
        {
            ImGui.MenuItem("Delete Track", default, false, false);
            return;
        }

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var label = ctrlHeld ? "Delete Track" : "Hold Ctrl to Delete Track";

        if (ImGui.MenuItem(label, default, false, ctrlHeld))
        {
            var result = _editorCommandExecutor.Execute(
                new DeleteTracksCommand(),
                CreateEditorCommandContext(),
                new DeleteTracksOptions(new[] { index }));

            if (result.Succeeded)
                ApplyEditorCommandRefreshHints();
        }
    }

    private void DrawTrackDisplayStateMenuItems(TrackDisplayState displayState, int index)
    {
        bool isLocked = displayState.IsLocked;
        var lockText = isLocked ? "Unlock Track" : "Lock Track";
        if (ImGui.MenuItem(lockText))
            displayState.IsLocked = !isLocked;

        bool isVisible = displayState.Visible;
        var visibleText = isVisible ? "Hide Track" : "Show Track";
        if (ImGui.MenuItem(visibleText))
        {
            displayState.Visible = !isVisible;
            RefreshPreviewVoiceLimits();
        }

        bool useTrackNameTranspose = displayState.UseTrackNameTranspose;
        if (ImGui.Checkbox($"Track Name Transpose##TrackNameTranspose_{index}", ref useTrackNameTranspose))
            displayState.UseTrackNameTranspose = useTrackNameTranspose;

        bool useAutoAdapt = displayState.UseAutoAdapt;
        if (ImGui.Checkbox($"Auto Adapt to C3-C6##AutoAdapt_{index}", ref useAutoAdapt))
            displayState.UseAutoAdapt = useAutoAdapt;
    }

    private void TransposeTrackFromContextMenu(int trackIndex, int semitones)
    {
        var result = _editorCommandExecutor.Execute(
            new TransposeTracksCommand(),
            CreateEditorCommandContext(),
            new TransposeTracksOptions(new[] { trackIndex }, semitones));

        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
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
