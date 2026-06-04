using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands.Event;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawEventListPanel()
    {
        // Search + filter button
        var headerHeight = ImGui.GetFrameHeightWithSpacing();
        using (var headerChild = ImRaii.Child("##EvHeader", new Vector2(-1, headerHeight), false,
            ImGuiWindowFlags.NoScrollbar))
        {
            if (headerChild)
            {
                ImGui.SetNextItemWidth(-Style.Dimensions.ButtonLarge.X - ImGui.GetStyle().ItemSpacing.X);
                ImGui.InputTextWithHint("##evSearch", "Search events...", ref _eventSearch, 128);

                ImGui.SameLine();

                if (ImGuiUtil.IconButton(FontAwesomeIcon.Filter, "##evFilter", MidiEditorOperationHelp.EventFilterTypes,
                    size: Style.Dimensions.ButtonLarge))
                    _pendingPopup = "##EventFilterPopup";
            }
        }

        // Events table
        var track = _selectedTrackIndex >= 0 && _file != null && _selectedTrackIndex < _file.Tracks.Count
            ? _file.Tracks[_selectedTrackIndex] : null;

        var tableAvailable = ImGui.GetContentRegionAvail();
        using var tableChild = ImRaii.Child("##EvTableChild", tableAvailable, false);
        if (!tableChild) return;

        if (track == null)
        {
            ImGui.TextDisabled("Select a track to view its events.");
            return;
        }

        if (track.Events == null)
        {
            ImGui.TextDisabled("Loading events...");
            return;
        }

        var frameH = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var actsWidth = frameH * 2 + spacing;
        var scale = ImGuiHelpers.GlobalScale;
        var fixedNR = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV
                       | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit
                       | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##EventTable", 7, tableFlags, tableAvailable)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("##chk", fixedNR, frameH);
        ImGui.TableSetupColumn("Time", fixedNR, 65f * scale);
        ImGui.TableSetupColumn("Tick", fixedNR, 58f * scale);
        ImGui.TableSetupColumn("Duration", fixedNR, 52f * scale);
        ImGui.TableSetupColumn("Type", fixedNR, 110f * scale);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##acts", fixedNR, actsWidth);

        // Manual header row with global checkbox in ##chk column
        var events = track.Events;
        var search = _eventSearch.ToLowerInvariant();

        // Rebuild visible event indices cache when track, filter, search, or version changes
        if (_visibleEventsTrackIndex != _selectedTrackIndex
            || _visibleEventsFilter != _eventFilter
            || _visibleEventsSearch != search
            || (_file != null && _visibleEventsVersion != _file.Version))
        {
            RebuildVisibleEventIndices(events, search);
        }

        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableNextColumn(); // ##chk
        if (ImGui.Checkbox("##GlobEvChk", ref _globalEventsChecked))
        {
            if (_globalEventsChecked)
            {
                // Select all visible (filtered + searched) events
                foreach (var idx in _visibleEventIndices)
                    _selectedEventIndices.Add(idx);
            }
            else
            {
                ClearEventSelection();
            }
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.EventSelectAll);

        ImGui.TableNextColumn();
        ImGui.Text("Time");

        ImGui.TableNextColumn();
        ImGui.Text("Tick");

        ImGui.TableNextColumn();
        ImGui.Text("Duration");

        ImGui.TableNextColumn();
        ImGui.Text("Type");

        ImGui.TableNextColumn();
        ImGui.Text("Value");

        ImGui.TableNextColumn();
        // Batch action bar - visible only when events are selected
        if (_selectedEventIndices.Count > 0)
        {
            if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.Eraser, "##clearEvSel", MidiEditorOperationHelp.EventClearSelection))
                ClearEventSelection();

            ImGui.SameLine();
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Trash, "##batchDelEvs",
                MidiEditorOperationHelp.EventDeleteSelected))
            {
                if (ImGui.GetIO().KeyCtrl)
                    DeleteSelectedEvents();
            }
        }

        // Event rows (clipped for performance)
        var visibleCount = _visibleEventIndices.Count;
        if (visibleCount > 0)
        {
            var clipper = new ImGuiListClipper();
            clipper.Begin(visibleCount, ImGui.GetTextLineHeightWithSpacing());
            while (clipper.Step())
            {
                for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    var eventIndex = _visibleEventIndices[row];
                    var ev = events[eventIndex];
                    DrawEventEntry(ev, eventIndex, track);
                }
            }
        }

        ImGui.EndTable();
    }

    private void RebuildVisibleEventIndices(IReadOnlyList<EditableEvent> events, string search)
    {
        _visibleEventIndices.Clear();
        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (!ev.MatchesFilter(_eventFilter)) continue;

            if (!string.IsNullOrEmpty(search))
            {
                var display = $"{ev.TypeName} {ev.GetValueDisplay()}".ToLowerInvariant();
                if (!display.Contains(search)) continue;
            }

            _visibleEventIndices.Add(i);
        }

        _visibleEventsTrackIndex = _selectedTrackIndex;
        _visibleEventsFilter = _eventFilter;
        _visibleEventsSearch = search;
        _visibleEventsVersion = _file?.Version ?? -1;
    }

    private void DrawEventEntry(EditableEvent ev, int index, EditableTrack track)
    {
        ImGui.TableNextRow();
        ImGui.PushID(index);

        // Checkbox column
        ImGui.TableNextColumn();
        bool isChecked = _selectedEventIndices.Contains(index);
        if (ImGui.Checkbox("##evChk", ref isChecked))
        {
            if (isChecked) _selectedEventIndices.Add(index);
            else _selectedEventIndices.Remove(index);
        }

        // Time (bar:beat)
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(ev.Tick.ToDisplayTime(_file.TempoMap));

        // Tick
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{ev.Tick}");

        // Duration (NoteOn only)
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (ev.NoteOffSource != null)
            ImGui.Text($"{ev.DurationTicks}");
        else
            ImGui.TextDisabled("-");

        // Type
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered, isChecked)
               .Push(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered, isChecked)
               .Push(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered, isChecked)
               .Push(ImGuiCol.Text, Style.Colors.Blue, track.IsConductorTrack))
        {
            if (ImGui.Selectable($"{ev.TypeName}##evType", isChecked))
            {
                double timeSec = TimeConverter.ConvertTo<MetricTimeSpan>(ev.Tick, _file.TempoMap).TotalMicroseconds / 1_000_000.0;
                CenterPreviewViewOnTime(timeSec, _pianoRollWidthCache);
            }
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.EventScrollTo);

        // Value
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(ev.GetValueDisplay());

        // Actions
        ImGui.TableNextColumn();
        var isOther = ev.Category == MidiEventFilter.Other;
        using (ImRaii.Disabled(isOther))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##editEv",
                isOther ? MidiEditorOperationHelp.EventEditUnavailable : MidiEditorOperationHelp.EventEdit))
            {
                _editingEvent = ev;
                ev.RefreshEditValues();
                _pendingPopup = "##EventEditPopup";
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Trash, "##delEv", MidiEditorOperationHelp.EventDelete))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                var result = _editorCommandExecutor.Execute(
                    new DeleteEventCommand(),
                    CreateEditorCommandContext(),
                    new DeleteEventOptions(
                        _selectedTrackIndex,
                        EventSelectionKey.FromEvent(index, ev)));
                if (result.Succeeded)
                    ApplyEditorCommandRefreshHints();
                ImGui.PopID();
                return;
            }
        }

        // Scroll this row into view when triggered by piano roll click
        if (_pianoRollScrollToSelected && _pianoRollScrollTarget == index)
        {
            ImGui.SetScrollHereY(0.5f);
            _pianoRollScrollToSelected = false;
        }

        ImGui.PopID();
    }

    private void DrawEventEditPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##EventEditPopup");
        if (!popup) return;
        if (_editingEvent == null) return;

        ImGui.Text($"Edit {_editingEvent.TypeName}");
        ImGui.Separator();

        var fieldWidth = 180f * ImGuiHelpers.GlobalScale;
        var (label1, label2) = _editingEvent.GetEditLabels();

        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputInt("Tick##evEditTick", ref _editingEvent.EditTick);

        if (_editingEvent.NoteOffSource != null)
        {
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.InputInt("Duration (ticks)##evEditDur", ref _editingEvent.EditDuration);
        }

        // Program Change: GM instrument combo
        if (_editingEvent.Source.Event is ProgramChangeEvent)
        {
            var clamped = Math.Clamp(_editingEvent.EditValue1, 0, 127);
            var preview = GmProgramComboItems[clamped];
            ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("Program##evPCCombo", preview))
            {
                for (int i = 0; i < GmProgramComboItems.Length; i++)
                {
                    bool sel = i == _editingEvent.EditValue1;
                    if (ImGui.Selectable(GmProgramComboItems[i], sel))
                        _editingEvent.EditValue1 = i;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(label1))
            {
                ImGui.SetNextItemWidth(fieldWidth);
                ImGui.InputInt($"{label1}##evEdit1", ref _editingEvent.EditValue1);
            }

            if (!string.IsNullOrEmpty(label2))
            {
                ImGui.SetNextItemWidth(fieldWidth);
                ImGui.InputInt($"{label2}##evEdit2", ref _editingEvent.EditValue2);
            }
        }

        ImGui.Spacing();

        if (ImGuiUtil.SuccessButton("Save##saveEv"))
        {
            var eventIndex = CurrentEvents?.IndexOf(_editingEvent) ?? -1;
            var result = _editorCommandExecutor.Execute(
                new EditEventCommand(),
                CreateEditorCommandContext(),
                new EditEventOptions(
                    _selectedTrackIndex,
                    EventSelectionKey.FromEvent(eventIndex, _editingEvent),
                    new EventEditValues(
                        _editingEvent.EditTick,
                        _editingEvent.EditValue1,
                        _editingEvent.EditValue2,
                        _editingEvent.EditDuration)));
            if (result.Succeeded)
            {
                ApplyEditorCommandRefreshHints();
                _editingEvent = null;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton("Cancel##cancelEv"))
        {
            _editingEvent = null;
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawEventFilterPopup()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##EventFilterPopup");
        if (!popup) return;

        ImGui.Text("Show Event Types");
        ImGui.Separator();

        DrawFilterCheckbox("Notes", MidiEventFilter.Notes);
        DrawFilterCheckbox("Program Change", MidiEventFilter.ProgramChange);
        DrawFilterCheckbox("Pitch Bend", MidiEventFilter.PitchBend);
        DrawFilterCheckbox("Tempo", MidiEventFilter.Tempo);
        DrawFilterCheckbox("Other", MidiEventFilter.Other);

        ImGui.Separator();

        if (ImGui.MenuItem("Select All")) _eventFilter = MidiEventFilter.All;
        if (ImGui.MenuItem("Deselect All")) _eventFilter = 0;
    }

    private void DrawFilterCheckbox(string label, MidiEventFilter flag)
    {
        var enabled = (_eventFilter & flag) != 0;
        if (ImGui.Checkbox(label, ref enabled))
            _eventFilter = enabled ? _eventFilter | flag : _eventFilter & ~flag;
    }
}
