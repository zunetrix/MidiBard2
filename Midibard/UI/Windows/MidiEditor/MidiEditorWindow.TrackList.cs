using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

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
        if (!ImGui.BeginTable("##TrackTable", 6, tableFlags, tableAvailable)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##chk", fixedNR, frameH);
        ImGui.TableSetupColumn("##color", fixedNR, 20f * scale);
        ImGui.TableSetupColumn("#", fixedNR, 28f * scale);
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
        ImGuiUtil.ToolTip("Select / Unselect All");

        ImGui.TableNextColumn(); // ##color header

        ImGui.TableNextColumn();
        ImGui.Text("#");

        ImGui.TableNextColumn();
        ImGui.Text("Name");

        ImGui.TableNextColumn();
        ImGui.Text("Ch");

        ImGui.TableNextColumn();
        // Batch action bar - only visible when tracks are selected
        if (_selectedTrackIndices.Count > 0)
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Square, "##clearTrackSel", "Clear selection"))
                ClearTrackSelection();

            ImGui.SameLine();
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Trash, "##batchDelTracks",
               "Hold Ctrl to delete selected tracks"))
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

        //  Color / Visibility column
        ImGui.TableNextColumn();
        if (displayState != null && !track.IsConductorTrack)
        {
            var autoColor = PianoRollWindow.GetTrackColor(index, trackCount);
            var trackColor = displayState.Color ?? autoColor;
            bool isVisible = displayState.Visible;

            // Left-click toggles visibility, right-click opens color picker
            var renderColor = isVisible ? trackColor : trackColor * new Vector4(1f, 1f, 1f, 0.3f);
            string visTooltip = isVisible ? "Visible in piano roll (right-click for color)" : "Hidden in piano roll (right-click for color)";
            if (ImGui.ColorButton($"##prevcol{index}", renderColor, ImGuiColorEditFlags.NoTooltip,
                new Vector2(16f * ImGuiHelpers.GlobalScale, 16f * ImGuiHelpers.GlobalScale)))
            {
                displayState.Visible = !displayState.Visible;
                RefreshPreviewVoiceLimits();
            }
            ImGuiUtil.ToolTip(visTooltip);
            ImGui.OpenPopupOnItemClick($"##prevColorPicker{index}", ImGuiPopupFlags.MouseButtonRight);

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
        ImGui.Text($"{index + 1:00}");

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
            bool confirmed = _trackNameAutocomplete.Draw(
                "##inlineTrackNameEdit",
                ref _editTrackName,
                InstrumentOptions,
                i => i.FFXIVDisplayName,
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
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Blue, track.IsConductorTrack))
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
                                    if (_selectedTrackIndex == fromIdx) _selectedTrackIndex = index;
                                    _file!.MoveTrack(fromIdx, index);
                                    _selectedTrackIndices.Clear();
                                }
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                }
            }
        }
        ImGuiUtil.ToolTip("Drag to reorder");

        //  Channel column
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (track.IsConductorTrack)
            ImGui.TextDisabled("-");
        else
            ImGui.Text($"{track.Channel + 1}");

        //  Actions column
        ImGui.TableNextColumn();
        if (isEditingThis)
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Check, "##saveTrackName", "Save name"))
                SaveTrackName();

            ImGui.SameLine();

            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##cancelTrackName", "Cancel edit"))
                _editingTrack = null;
        }
        else
        {
            // Lock button
            bool isLocked = displayState?.IsLocked ?? false;
            if (displayState != null)
            {
                using (ImRaii.Disabled(track.IsConductorTrack))
                {
                    var lockIcon = isLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                    var lockTooltip = isLocked ? "Track locked (click to unlock)" : "Lock track (prevents note selection)";
                    using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Red, isLocked))
                    {
                        if (ImGuiUtil.IconButton(lockIcon, "##lockTrack", lockTooltip))
                            displayState.IsLocked = !isLocked;
                    }
                }
            }

            ImGui.SameLine();

            using (ImRaii.Disabled(track.IsConductorTrack || anyEditing))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##editTrack", "Edit track name"))
                {
                    _editingTrack = track;
                    _editTrackName = track.Name;
                    _editTrackFocusNext = true;
                }

                ImGui.SameLine();

                if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##delTrack", "Ctrl+Click to delete"))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        if (_selectedTrackIndex == index) SelectTrack(-1);
                        _selectedTrackIndices.Remove(index);
                        _file!.RemoveTrack(index);
                        ImGui.PopID();
                        return;
                    }
                }
            }
        }

        ImGui.PopID();
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

        if (ImGui.MenuItem("Clone Track"))
        {
            var wasLoaded = _selectedTrackIndex == index && track.Events != null;
            _file!.CloneTrack(index);
            _selectedTrackIndices.Clear();
            if (wasLoaded)
                _file.Tracks[index].LoadEvents(_file.TempoMap);
        }

        if (ImGui.MenuItem("Split by Channel", default, false, track.HasMultipleChannels))
        {
            _file!.SplitTrackByChannel(index);
            if (_selectedTrackIndex >= _file.Tracks.Count)
            {
                _selectedTrackIndex = -1;
                _selectedEventIndices.Clear();
            }
            _selectedTrackIndices.Clear();
        }
    }

    private void SaveTrackName()
    {
        if (_editingTrack == null) return;
        _editingTrack.Name = _editTrackName;
        _editingTrack.MarkNameDirty();
        _file!.IsDirty = true;
        _editingTrack = null;
    }
}
