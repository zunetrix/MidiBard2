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
        var actsWidth = frameH * 2 + spacing;
        var scale = ImGuiHelpers.GlobalScale;
        var fixedNR = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
                       | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit
                       | ImGuiTableFlags.ScrollY;

        var tableAvailable = ImGui.GetContentRegionAvail();
        if (!ImGui.BeginTable("##TrackTable", 5, tableFlags, tableAvailable)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##chk", fixedNR, frameH);
        ImGui.TableSetupColumn("#", fixedNR, 28f * scale);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
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

        ImGui.TableNextColumn();
        ImGui.Text("#");

        ImGui.TableNextColumn();
        ImGui.Text("Name");

        ImGui.TableNextColumn();
        ImGui.Text("Ch");

        ImGui.TableNextColumn();
        // Batch action bar — only visible when tracks are selected
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

        //  Checkbox column — skipped for conductor track
        ImGui.TableNextColumn();
        if (!track.IsConductorTrack)
        {
            bool isChecked = _selectedTrackIndices.Contains(index);
            if (ImGui.Checkbox("##trkChk", ref isChecked))
            {
                if (isChecked) _selectedTrackIndices.Add(index);
                else _selectedTrackIndices.Remove(index);
            }
        }

        //  # column — click to select, drag handle, right-click menu
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{index + 1:00}");

        //  Name column — text display
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Blue, track.IsConductorTrack))
        {
            if (ImGui.Selectable($"{track.DisplayName}##DndTrack_{index}", isRowSelected,
                ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, 0)))
                SelectTrack(index);
        }

        ImGui.OpenPopupOnItemClick("##TrackContextMenu", ImGuiPopupFlags.MouseButtonRight);
        DrawTrackContextMenu(track, index);

        // Drag source
        if (ImGui.BeginDragDropSource())
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

        // Drop target
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

        //  Channel column
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (track.IsConductorTrack)
            ImGui.TextDisabled("-");
        else
            ImGui.Text($"{track.Channel + 1}");

        //  Actions column
        ImGui.TableNextColumn();
        //  buttons suppressed for conductor track
        using (ImRaii.Disabled(track.IsConductorTrack))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##editTrack", "Edit track name"))
            {
                _editingTrack = track;
                _editTrackName = track.Name;
                _pendingPopup = "##TrackEditPopup";
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

    private void DrawTrackEditPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##TrackEditPopup");
        if (!popup) return;
        if (_editingTrack == null) return;

        ImGui.Text("Edit Track Name");
        ImGui.Separator();

        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);

        bool confirmed = _trackNameAutocomplete.Draw(
            "Track name",
            ref _editTrackName,
            InstrumentOptions,
            i => i.FFXIVDisplayName,
            i => i.IconId);

        if (confirmed)
        {
            SaveTrackName();
            return;
        }

        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Save##saveTrackName"))
            SaveTrackName();

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelTrackName"))
        {
            _editingTrack = null;
            ImGui.CloseCurrentPopup();
        }
    }

    private void SaveTrackName()
    {
        if (_editingTrack == null) return;
        _editingTrack.Name = _editTrackName;
        _editingTrack.MarkNameDirty();
        _file!.IsDirty = true;
        _editingTrack = null;
        ImGui.CloseCurrentPopup();
    }
}
