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
    private const string PianoRollContextMenuStateKey = "piano-roll.context-menu.popup";

    private PianoRollContextMenuState GetPianoRollContextMenuState()
        => _editorCommandSession.PopupStates.GetOrCreate(
            PianoRollContextMenuStateKey,
            static () => new PianoRollContextMenuState());

    private static Vector2 GetContextMenuPopupAnchor()
        => ImGui.GetItemRectMax() + new Vector2(8f, -ImGui.GetFrameHeight());

    private void CapturePianoRollContextMenuTick(PianoRenderContext ctx, Vector2 mousePos)
    {
        if (_file == null) return;

        var state = GetPianoRollContextMenuState();
        var tmap = _file.TempoMap;
        double sec = ctx.ScreenXToTime(mousePos.X);
        state.Tick = TimeConverter.ConvertFrom(
            new MetricTimeSpan((long)(Math.Max(0.0, sec) * 1_000_000.0)), tmap);
        state.Tick = SnapTickToGrid(state.Tick, tmap);

        var (hitIdx, _) = HitTestNote(mousePos);
        state.NoteHitIndex = hitIdx;

        state.Requested = true;
        state.MousePos = mousePos;
    }

    private void DrawPianoRollContextMenu()
    {
        using var border = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##PianoRollContextMenu");
        if (!popup) return;
        if (_file == null) return;

        var state = GetPianoRollContextMenuState();

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal))
        {
            ImGui.Button($"Position: {FormatBarBeatTick(state.Tick)}", new Vector2(-1, 0));
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
                OpenSetTempoPopup(state.Tick, GetContextMenuPopupAnchor());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.ConductorSetTempo);

            if (ImGui.MenuItem("Set Time Signature Here##ctxSetTimeSig"))
                OpenSetTimeSignaturePopup(state.Tick, GetContextMenuPopupAnchor());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.ConductorSetTimeSignature);

            ImGui.EndMenu();
        }

        // --- Measures ---
        if (ImGui.BeginMenu("Measures", hasFile))
        {
            if (ImGui.MenuItem("Insert Measures Here...##ctxInsertMeasures"))
                OpenInsertMeasuresPopup(state.Tick, GetContextMenuPopupAnchor());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.InsertMeasures);

            if (ImGui.MenuItem("Delete Measures Here...##ctxDeleteMeasures"))
                OpenDeleteMeasuresPopup(state.Tick, GetContextMenuPopupAnchor());
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
                SelectNotesBeforeTick(state.Tick);
                if (state.NoteHitIndex >= 0)
                    _selectedEventIndices.Add(state.NoteHitIndex);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.SelectAllLeft);

            if (ImGui.MenuItem("Select All Right##ctxSelectRight", default, false, hasLoadedTrack))
            {
                SelectNotesAfterTick(state.Tick);
                if (state.NoteHitIndex >= 0)
                    _selectedEventIndices.Add(state.NoteHitIndex);
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
                var repeatState = GetRepeatLoopPopupState();
                repeatState.IntervalIndex = 0;
                repeatState.EndConditionIndex = 3;
                repeatState.RepeatCount = 2;
                OpenRepeatLoopPopup(GetContextMenuPopupAnchor());
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.RepeatLoop);

            if (ImGui.MenuItem("Strum Notes...##ctxStrum", default, false, hasSelNotes))
                OpenStrumNotesPopup(GetContextMenuPopupAnchor());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.StrumNotes);

            if (ImGui.MenuItem("Quantize Notes...##ctxQuantize", default, false, hasSelNotes))
                OpenQuantizeNotesPopup(GetContextMenuPopupAnchor());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(MidiEditorOperationHelp.QuantizeSelectedNotes);

            if (ImGui.MenuItem("Glue Notes##ctxGlue", default, false, selCount >= 2))
                OpenGlueNotesPopup(GetContextMenuPopupAnchor());
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
                OpenTransposeNotesPopup(GetContextMenuPopupAnchor());
            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Clipboard ---
        if (ImGui.BeginMenu("Clipboard"))
        {
            if (ImGui.MenuItem("Copy Notes##ctxCopy", default, false, hasSelNotes))
                CopySelectedNotes();

            if (ImGui.MenuItem("Paste Notes Here##ctxPaste", default, false, canPaste && hasLoadedTrack))
                PasteCopiedNotesAtTick(state.Tick);

            ImGui.EndMenu();
        }

        ImGui.Separator();

        // --- Delete ---
        if (ImGui.MenuItem("Delete Note(s)##ctxDelete", default, false, hasSelNotes || state.NoteHitIndex >= 0))
        {
            if (state.NoteHitIndex >= 0 && !hasSelNotes)
            {
                _selectedEventIndices.Clear();
                _selectedEventIndices.Add(state.NoteHitIndex);
            }
            DeleteSelectedNotes();
        }
    }

    private sealed class PianoRollContextMenuState
    {
        public long Tick;
        public int NoteHitIndex = -1;
        public bool Requested;
        public Vector2 MousePos;
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
