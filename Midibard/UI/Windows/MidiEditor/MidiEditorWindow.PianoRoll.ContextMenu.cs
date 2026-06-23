using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private long _contextMenuTick;
    private int _contextMenuNoteHitIndex = -1;

    /// <summary>
    /// Called from HandleEditorInteraction when the user right-clicks the piano roll
    /// outside of pencil mode. Captures the click position for context-sensitive menu items.
    /// The popup itself is opened via ImGui.OpenPopupOnItemClick in DrawPianoRollPanel so
    /// that it shares the same ID-stack context as the InvisibleButton.
    /// </summary>
    private void CapturePianoRollContextMenuTick(PianoRenderContext ctx, System.Numerics.Vector2 mousePos)
    {
        if (_file == null) return;

        var tmap = _file.TempoMap;
        double sec = ctx.ScreenXToTime(mousePos.X);
        _contextMenuTick = TimeConverter.ConvertFrom(
            new MetricTimeSpan((long)(Math.Max(0.0, sec) * 1_000_000.0)), tmap);
        _contextMenuTick = SnapTickToGrid(_contextMenuTick, tmap);

        var (hitIdx, _) = HitTestNote(mousePos);
        _contextMenuNoteHitIndex = hitIdx;
    }

    private void DrawPianoRollContextMenu()
    {
        using var popup = ImRaii.Popup("##PianoRollContextMenu");
        if (!popup) return;
        if (_file == null) return;

        var hasLoadedTrack = CurrentEvents != null;
        var hasSelNotes = HasSelectedNotes();
        var selCount = _selectedEventIndices.Count;

        // --- Conductor operations ---
        if (ImGui.MenuItem("Set BPM Here...##ctxSetTempo", default, false, _file != null))
            OpenSetTempoPopup(_contextMenuTick);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.ConductorSetTempo);

        if (ImGui.MenuItem("Set Time Signature Here...##ctxSetTimeSig", default, false, _file != null))
            OpenSetTimeSignaturePopup(_contextMenuTick);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.ConductorSetTimeSignature);

        ImGui.Separator();

        // --- Selection ---
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

        ImGui.Separator();

        // --- Note operations ---
        if (ImGui.MenuItem("Split in Half##ctxSplit", default, false, hasSelNotes))
            SplitSelectedNotesInHalf();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.SplitSelectedNotesInHalf);

        if (ImGui.MenuItem("Repeat...##ctxRepeat", default, false, hasSelNotes))
            OpenRepeatLoopPopup();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.RepeatLoop);

        if (ImGui.MenuItem("Glue Notes##ctxGlue", default, false, selCount >= 2))
            OpenGlueNotesPopup();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(MidiEditorOperationHelp.GlueNotes);

        if (ImGui.MenuItem("Delete Note(s)##ctxDelete", default, false, hasSelNotes || _contextMenuNoteHitIndex >= 0))
        {
            if (_contextMenuNoteHitIndex >= 0 && !hasSelNotes)
            {
                _selectedEventIndices.Clear();
                _selectedEventIndices.Add(_contextMenuNoteHitIndex);
            }
            DeleteSelectedNotes();
        }

        ImGui.Separator();

        // --- Quick transpose ---
        if (ImGui.BeginMenu("Transpose##ctxTranspose", hasSelNotes))
        {
            if (ImGui.MenuItem("+12##ctxTransposeUp12"))
                TransposeSelectedNotes(12);
            if (ImGui.MenuItem("-12##ctxTransposeDown12"))
                TransposeSelectedNotes(-12);
            ImGui.EndMenu();
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
}
