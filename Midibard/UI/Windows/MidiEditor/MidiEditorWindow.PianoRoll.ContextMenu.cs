using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands.Note;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private long _contextMenuTick;
    private int _contextMenuNoteHitIndex = -1;
    private bool _contextMenuRequested;
    private Vector2 _contextMenuMousePos;

    private void CapturePianoRollContextMenuTick(PianoRenderContext ctx, Vector2 mousePos)
    {
        if (_file == null) return;

        var tmap = _file.TempoMap;
        double sec = ctx.ScreenXToTime(mousePos.X);
        _contextMenuTick = TimeConverter.ConvertFrom(
            new MetricTimeSpan((long)(Math.Max(0.0, sec) * 1_000_000.0)), tmap);
        _contextMenuTick = SnapTickToGrid(_contextMenuTick, tmap);

        var (hitIdx, _) = HitTestNote(mousePos);
        _contextMenuNoteHitIndex = hitIdx;

        _contextMenuRequested = true;
        _contextMenuMousePos = mousePos;
    }

    private void DrawPianoRollContextMenu()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##PianoRollContextMenu");
        if (!popup) return;
        if (_file == null) return;

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal))
        {
            ImGui.Button($"Position: {FormatBarBeatTick(_contextMenuTick)}", new Vector2(-1, 0));
        }

        ImGui.Separator();

        var hasLoadedTrack = CurrentEvents != null;
        var hasSelNotes = HasSelectedNotes();
        var selCount = _selectedEventIndices.Count;
        var canPaste = _editorCommandSession.NoteClipboard.HasNotes;
        var hasFile = _file != null;

        // --- Conductor ---
        if (ImGui.BeginMenu("Conductor", hasFile))
        {
            if (ImGui.MenuItem("Set BPM Here##ctxSetTempo"))
                OpenSetTempoPopup(_contextMenuTick);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.ConductorSetTempo);

            if (ImGui.MenuItem("Set Time Signature Here##ctxSetTimeSig"))
                OpenSetTimeSignaturePopup(_contextMenuTick);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.ConductorSetTimeSignature);

            ImGui.EndMenu();
        }

        // --- Measures ---
        if (ImGui.BeginMenu("Measures", hasFile))
        {
            if (ImGui.MenuItem("Insert Measures Here...##ctxInsertMeasures"))
                OpenInsertMeasuresPopup(_contextMenuTick);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.InsertMeasures);

            if (ImGui.MenuItem("Delete Measures Here...##ctxDeleteMeasures"))
                OpenDeleteMeasuresPopup(_contextMenuTick);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.DeleteMeasures);

            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Select ---
        if (ImGui.BeginMenu("Select", hasFile))
        {
            if (ImGui.MenuItem("Select All Left##ctxSelectLeft", default, false, hasLoadedTrack))
            {
                SelectNotesBeforeTick(_contextMenuTick);
                if (_contextMenuNoteHitIndex >= 0)
                    _selectedEventIndices.Add(_contextMenuNoteHitIndex);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.SelectAllLeft);

            if (ImGui.MenuItem("Select All Right##ctxSelectRight", default, false, hasLoadedTrack))
            {
                SelectNotesAfterTick(_contextMenuTick);
                if (_contextMenuNoteHitIndex >= 0)
                    _selectedEventIndices.Add(_contextMenuNoteHitIndex);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.SelectAllRight);

            if (ImGui.MenuItem("Select All in Track##ctxSelectAll", default, false, hasLoadedTrack))
                SelectAllNotesInTrack();

            if (ImGui.MenuItem("Clear Selection##ctxClearSel", default, false, selCount > 0))
                _selectedEventIndices.Clear();

            ImGui.EndMenu();
        }

        // --- Edit ---
        if (ImGui.BeginMenu("Edit", hasSelNotes || selCount >= 2))
        {
            if (ImGui.MenuItem("Split in Half##ctxSplit", default, false, hasSelNotes))
                SplitSelectedNotesInHalf();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.SplitSelectedNotesInHalf);

            if (ImGui.MenuItem("Repeat...##ctxRepeat", default, false, hasSelNotes))
            {
                var state = GetRepeatLoopPopupState();
                state.IntervalIndex = 0;
                state.EndConditionIndex = 3;
                state.RepeatCount = 2;
                OpenRepeatLoopPopup();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.RepeatLoop);

            if (ImGui.MenuItem("Strum Notes...##ctxStrum", default, false, hasSelNotes))
                OpenStrumNotesPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.StrumNotes);

            if (ImGui.MenuItem("Quantize Notes...##ctxQuantize", default, false, hasSelNotes))
                OpenQuantizeNotesPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.QuantizeSelectedNotes);

            if (ImGui.MenuItem("Glue Notes##ctxGlue", default, false, selCount >= 2))
                OpenGlueNotesPopup();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.GlueNotes);

            ImGui.EndMenu();
        }

        // --- Nudge ---
        if (ImGui.BeginMenu("Nudge", hasSelNotes))
        {
            if (ImGui.MenuItem("Nudge Left##ctxNudgeLeft"))
                NudgeSelectedNotesByGrid(-1);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.NudgeLeft);

            if (ImGui.MenuItem("Nudge Right##ctxNudgeRight"))
                NudgeSelectedNotesByGrid(1);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.NudgeRight);

            ImGui.EndMenu();
        }

        // --- Transpose ---
        if (ImGui.BeginMenu("Transpose##ctxTranspose", hasSelNotes))
        {
            if (ImGui.MenuItem("+12##ctxTransposeUp12"))
                TransposeSelectedNotes(12);
            if (ImGui.MenuItem("-12##ctxTransposeDown12"))
                TransposeSelectedNotes(-12);
            ImGui.Separator();
            if (ImGui.MenuItem("+1##ctxTransposeUp1"))
                TransposeSelectedNotes(1);
            if (ImGui.MenuItem("-1##ctxTransposeDown1"))
                TransposeSelectedNotes(-1);
            ImGui.Separator();
            if (ImGui.MenuItem("Custom...##ctxTransposeCustom"))
                OpenTransposeNotesPopup();
            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Clipboard ---
        if (ImGui.BeginMenu("Clipboard"))
        {
            if (ImGui.MenuItem("Copy Notes##ctxCopy", default, false, hasSelNotes))
                CopySelectedNotes();

            if (ImGui.MenuItem("Paste Notes Here##ctxPaste", default, false, canPaste && hasLoadedTrack))
                PasteCopiedNotesAtTick(_contextMenuTick);

            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Delete ---
        if (ImGui.MenuItem("Delete Note(s)##ctxDelete", default, false, hasSelNotes || _contextMenuNoteHitIndex >= 0))
        {
            if (_contextMenuNoteHitIndex >= 0 && !hasSelNotes)
            {
                _selectedEventIndices.Clear();
                _selectedEventIndices.Add(_contextMenuNoteHitIndex);
            }
            DeleteSelectedNotes();
        }
    }

    private void SelectNotesBeforeTick(long tick)
    {
        var events = CurrentEvents;
        if (events == null) return;
        _selectedEventIndices.Clear();
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].NoteOffSource != null && events[i].Tick <= tick)
                _selectedEventIndices.Add(i);
        }
    }

    private void SelectNotesAfterTick(long tick)
    {
        var events = CurrentEvents;
        if (events == null) return;
        _selectedEventIndices.Clear();
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].NoteOffSource != null && events[i].Tick >= tick)
                _selectedEventIndices.Add(i);
        }
    }

    private void PasteCopiedNotesAtTick(long anchorTick)
    {
        if (_file == null || !_editorCommandSession.NoteClipboard.HasNotes)
            return;
        if (_selectedTrackIndex < 0 || _selectedTrackIndex >= _file.Tracks.Count)
            return;

        var track = _file.Tracks[_selectedTrackIndex];
        if (track.IsConductorTrack) return;

        var result = _editorCommandExecutor.Execute(
            new PasteCopiedNotesCommand(),
            CreateEditorCommandContext(),
            new PasteCopiedNotesOptions(
                _selectedTrackIndex,
                anchorTick,
                _editorCommandSession.NoteClipboard.Notes));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
    }
}
