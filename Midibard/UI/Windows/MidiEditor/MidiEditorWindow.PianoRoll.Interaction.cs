using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private const float ResizeHandlePx = 8f;

    //  Hit list

    /// <summary>
    /// Rebuilds the per-frame note hit list from the currently loaded track's events.
    /// Must be called after BuildPreviewViewport so ctx.View is up to date.
    /// </summary>
    private void BuildNoteHitList(PianoRenderContext ctx)
    {
        _noteHitList.Clear();

        var events = CurrentEvents;
        if (events == null || _file == null) return;

        // No hit-testing for locked tracks
        var trackDisplayState = (_previewTracks != null && _selectedTrackIndex < _previewTracks.Length)
            ? _previewTracks[_selectedTrackIndex] : null;
        if (trackDisplayState?.IsLocked == true) return;

        var tmap = _file.TempoMap;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (ev.NoteOffSource == null) continue;
            if (ev.Source.Event is not NoteOnEvent noteOn) continue;

            int note = (byte)noteOn.NoteNumber;
            double startSec = TimeConverter.ConvertTo<MetricTimeSpan>(ev.Tick, tmap).TotalMicroseconds / 1_000_000.0;
            double endSec = TimeConverter.ConvertTo<MetricTimeSpan>(ev.Tick + ev.DurationTicks, tmap).TotalMicroseconds / 1_000_000.0;

            if (!ctx.IsNoteVisible(startSec, endSec, note)) continue;

            _noteHitList.Add(new NoteHitEntry(ctx.NoteRectMin(startSec, note), ctx.NoteRectMax(endSec, note), i));
        }
    }

    //  Hit testing

    private (int hitIndex, bool isResizeHandle) HitTestNote(Vector2 mousePos)
    {
        // Iterate in reverse so topmost-rendered note wins
        for (int i = _noteHitList.Count - 1; i >= 0; i--)
        {
            var h = _noteHitList[i];
            if (mousePos.X < h.RectMin.X || mousePos.X > h.RectMax.X ||
                mousePos.Y < h.RectMin.Y || mousePos.Y > h.RectMax.Y)
                continue;

            bool isResize = mousePos.X >= h.RectMax.X - ResizeHandlePx;
            return (h.EventIndex, isResize);
        }
        return (-1, false);
    }

    //  Pre-drag snapshot

    private void SnapshotPreDragState()
    {
        _preDragSnapshot.Clear();
        var events = CurrentEvents;
        if (events == null) return;

        foreach (var idx in _selectedEventIndices)
        {
            if ((uint)idx >= (uint)events.Count) continue;
            var ev = events[idx];
            if (ev.NoteOffSource == null) continue;
            if (ev.Source.Event is not NoteOnEvent noteOn) continue;

            _preDragSnapshot[idx] = (
                tick: (int)ev.Tick,
                val1: (byte)noteOn.NoteNumber,
                val2: (byte)noteOn.Velocity,
                dur: (int)ev.DurationTicks
            );
        }
    }

    //  Main interaction handler

    /// <summary>Must be called immediately after the piano roll InvisibleButton.</summary>
    private void HandleEditorInteraction(PianoRenderContext ctx)
    {
        if (_file == null) return;

        var io = ImGui.GetIO();
        var mousePos = io.MousePos;
        bool isHovered = ImGui.IsItemHovered();
        bool isActive = ImGui.IsItemActive();
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        //  Middle-drag pan (always available)
        if (isActive && ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            _previewState.CameraTime -= io.MouseDelta.X / ctx.View.PixelsPerSecond;
            _previewState.CameraTopNote -= io.MouseDelta.Y / ctx.View.NoteHeight;
            ClampPreviewCamera(ctx);
        }

        switch (_editorDragMode)
        {
            //  Idle — decide what to start on click
            case EditorDragMode.None:
                {
                    if (isHovered)
                    {
                        if (_pencilModeActive)
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Arrow);
                        else
                        {
                            var (hoverIdx, hoverResize) = HitTestNote(mousePos);
                            ImGui.SetMouseCursor(hoverIdx >= 0
                                ? (hoverResize ? ImGuiMouseCursor.ResizeEw : ImGuiMouseCursor.Arrow)
                                : ImGuiMouseCursor.Hand);
                        }
                    }

                    if (isHovered && leftClicked)
                    {
                        if (_pencilModeActive)
                        {
                            // Pencil tool: insert a note at click position
                            if (_file != null && _selectedTrackIndex >= 0 && _selectedTrackIndex < _file.Tracks.Count)
                            {
                                var track = _file.Tracks[_selectedTrackIndex];
                                if (track.Events != null && !track.IsConductorTrack)
                                {
                                    var tmap = _file.TempoMap;
                                    int noteNum = Math.Clamp((int)(ctx.View.TopNote - (mousePos.Y - ctx.Y) / ctx.View.NoteHeight), 0, 127);
                                    double sec = ctx.ScreenXToTime(mousePos.X);
                                    long tick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(Math.Max(0.0, sec) * 1_000_000.0)), tmap);
                                    tick = SnapTickToGrid(tick, tmap);
                                    // Push start past any overlapping same-pitch note
                                    tick = FindOverlapEndTick(track.Events, noteNum, tick);
                                    int ppqn = _file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision td ? td.TicksPerQuarterNote : 480;
                                    long duration = 4L * ppqn / PencilDivisions[_pencilNoteDivisionIndex];
                                    _pencilNoteStartTick = tick;
                                    _pencilDragOriginSec = sec;
                                    _pencilDragEvent = track.InsertNote(tick, noteNum, 80, duration);
                                    if (_pencilDragEvent != null)
                                    {
                                        _file.IsDirty = true;
                                        _editorDragMode = EditorDragMode.PencilDraw;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var (hitIdx, isResize) = HitTestNote(mousePos);

                            if (io.KeyCtrl)
                            {
                                // Ctrl+click note: toggle selection
                                if (hitIdx >= 0)
                                {
                                    if (!_selectedEventIndices.Remove(hitIdx))
                                        _selectedEventIndices.Add(hitIdx);
                                }
                                else
                                {
                                    // Ctrl+click empty: additive box select
                                    _boxSelectInitialSelection = new HashSet<int>(_selectedEventIndices);
                                    _boxSelectA = _boxSelectB = mousePos;
                                    _editorDragMode = EditorDragMode.BoxSelect;
                                }
                            }
                            else if (hitIdx >= 0)
                            {
                                // Click on note: select + begin move or resize
                                if (isResize)
                                {
                                    if (!_selectedEventIndices.Contains(hitIdx))
                                    { _selectedEventIndices.Clear(); _selectedEventIndices.Add(hitIdx); }
                                    SnapshotPreDragState();
                                    _dragOriginSeconds = ctx.ScreenXToTime(mousePos.X);
                                    _editorDragMode = EditorDragMode.Resize;
                                }
                                else
                                {
                                    if (!_selectedEventIndices.Contains(hitIdx))
                                    {
                                        _selectedEventIndices.Clear();
                                        _selectedEventIndices.Add(hitIdx);
                                        TriggerScrollToEvent(hitIdx);
                                    }
                                    SnapshotPreDragState();
                                    _dragOriginSeconds = ctx.ScreenXToTime(mousePos.X);
                                    _dragOriginNoteOffset = ctx.View.TopNote - (mousePos.Y - ctx.Y) / ctx.View.NoteHeight;
                                    _editorDragMode = EditorDragMode.Move;
                                }
                            }
                            else
                            {
                                // Click on empty space: begin pan drag (selection is preserved)
                                _editorDragMode = EditorDragMode.Pan;
                            }
                        } // end else (!_pencilModeActive)
                    }
                    break;
                }

            //  Pan drag (started by clicking empty space without Ctrl)
            case EditorDragMode.Pan:
                if (leftDown && isActive)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    _previewState.CameraTime -= io.MouseDelta.X / ctx.View.PixelsPerSecond;
                    _previewState.CameraTopNote -= io.MouseDelta.Y / ctx.View.NoteHeight;
                    ClampPreviewCamera(ctx);
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                }
                break;

            case EditorDragMode.Move:
                if (leftDown && isActive)
                {
                    ApplyNoteMoveFromDrag(ctx, mousePos);
                    _file!.IsDirty = true;
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                    _preDragSnapshot.Clear();
                }
                break;

            case EditorDragMode.Resize:
                if (leftDown && isActive)
                {
                    ApplyNoteResizeFromDrag(ctx, mousePos);
                    _file!.IsDirty = true;
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                    _preDragSnapshot.Clear();
                }
                break;

            case EditorDragMode.BoxSelect:
                if (leftDown)
                {
                    _boxSelectB = mousePos;
                    UpdateBoxSelection();
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                }
                break;

            case EditorDragMode.PencilDraw:
                if (leftDown && isActive)
                {
                    if (_pencilDragEvent != null && _file != null)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
                        var tmap = _file.TempoMap;
                        double curSec = ctx.ScreenXToTime(mousePos.X);
                        double startSec = TimeConverter.ConvertTo<MetricTimeSpan>(_pencilNoteStartTick, tmap).TotalMicroseconds / 1_000_000.0;
                        double endSec = Math.Max(startSec + 0.001, curSec);
                        long endTick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(endSec * 1_000_000.0)), tmap);
                        if (_previewState.SnapToGrid) endTick = SnapTickToGrid(endTick, tmap);
                        int ppqn = _file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision td ? td.TicksPerQuarterNote : 480;
                        long minDur = 4L * ppqn / PencilDivisions[_pencilNoteDivisionIndex];
                        // Cap at the next same-pitch note to prevent overlap
                        if (_pencilDragEvent.Source.Event is NoteOnEvent pencilNoteOn)
                        {
                            int pencilNoteNum = (byte)pencilNoteOn.NoteNumber;
                            var track = _file.Tracks[_selectedTrackIndex];
                            long nextStart = FindNextNoteStartAfter(track.Events, _pencilDragEvent, pencilNoteNum, _pencilNoteStartTick);
                            if (nextStart != long.MaxValue)
                                endTick = Math.Min(endTick, nextStart);
                        }
                        long newDur = Math.Max(minDur, endTick - _pencilNoteStartTick);
                        _pencilDragEvent.RefreshEditValues();
                        _pencilDragEvent.EditDuration = (int)newDur;
                        _pencilDragEvent.ApplyEditValues();
                        _file.IsDirty = true;
                    }
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                    _pencilDragEvent = null;
                }
                break;
        }

        if (isHovered && io.MouseWheel != 0) ApplyPreviewZoom(io.MouseWheel);
    }

    //  Camera helpers

    private void ClampPreviewCamera(PianoRenderContext ctx)
    {
        float visibleNotes = ctx.Height / ctx.View.NoteHeight;
        _previewState.CameraTopNote = Math.Clamp(_previewState.CameraTopNote, Math.Min(visibleNotes, 127f), 127f);
        _previewState.CameraTime = Math.Clamp(_previewState.CameraTime, 0, _previewMaxTime);
    }

    private void ApplyPreviewZoom(float wheel)
    {
        float z = MathF.Pow(1.1f, wheel);
        _previewState.NoteMinHeight = Math.Clamp(_previewState.NoteMinHeight * z, 4f, 80f);
        _previewState.TimePixelsPerSecond = Math.Clamp(_previewState.TimePixelsPerSecond * z, 5f, 700f);
    }

    //  Note mutations

    private void ApplyNoteMoveFromDrag(PianoRenderContext ctx, Vector2 mousePos)
    {
        var events = CurrentEvents;
        if (events == null || _file == null) return;

        var tmap = _file.TempoMap;
        double curSec = ctx.ScreenXToTime(mousePos.X);
        double deltaSeconds = curSec - _dragOriginSeconds;
        float curNoteOffset = ctx.View.TopNote - (mousePos.Y - ctx.Y) / ctx.View.NoteHeight;
        int noteDelta = (int)MathF.Round(curNoteOffset - _dragOriginNoteOffset);

        foreach (var (idx, snap) in _preDragSnapshot)
        {
            if ((uint)idx >= (uint)events.Count) continue;
            var ev = events[idx];

            double origSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)snap.tick, tmap).TotalMicroseconds / 1_000_000.0;
            double newSec = Math.Max(0.0, origSec + deltaSeconds);
            long newTick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(newSec * 1_000_000.0)), tmap);
            newTick = SnapTickToGrid(newTick, tmap);

            ev.EditTick = (int)Math.Max(0, newTick);
            ev.EditValue1 = Math.Clamp(snap.val1 + noteDelta, 0, 127);
            ev.EditValue2 = snap.val2;
            ev.EditDuration = snap.dur;
            ev.ApplyEditValues();
        }
    }

    private void ApplyNoteResizeFromDrag(PianoRenderContext ctx, Vector2 mousePos)
    {
        var events = CurrentEvents;
        if (events == null || _file == null) return;

        var tmap = _file.TempoMap;
        double curSec = ctx.ScreenXToTime(mousePos.X);
        double deltaSeconds = curSec - _dragOriginSeconds;

        foreach (var (idx, snap) in _preDragSnapshot)
        {
            if ((uint)idx >= (uint)events.Count) continue;
            var ev = events[idx];

            double origStartSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)snap.tick, tmap).TotalMicroseconds / 1_000_000.0;
            double origEndSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)(snap.tick + snap.dur), tmap).TotalMicroseconds / 1_000_000.0;
            double newEndSec = Math.Max(origStartSec + 0.01, origEndSec + deltaSeconds);
            long newEndTick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(newEndSec * 1_000_000.0)), tmap);
            newEndTick = SnapTickToGrid(newEndTick, tmap);
            int newDur = (int)Math.Max(1, newEndTick - snap.tick);

            ev.EditTick = snap.tick;
            ev.EditValue1 = snap.val1;
            ev.EditValue2 = snap.val2;
            ev.EditDuration = newDur;
            ev.ApplyEditValues();
        }
    }

    /// <summary>
    /// Snaps a tick position to the nearest grid division defined by <see cref="PianoRollState.BeatDivision"/>.
    /// Returns the original tick unchanged when snap is disabled or the grid step cannot be determined.
    /// </summary>
    private long SnapTickToGrid(long tick, TempoMap tmap)
    {
        if (!_previewState.SnapToGrid) return tick;

        int subdivFactor = (int)_previewState.BeatDivision;

        var pos = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(tick, tmap);
        long bar = pos.Bars;
        long beat = pos.Beats;

        long barTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, 0), tmap);

        // Snap to bar boundary
        if (subdivFactor == 0)
        {
            long nextBarTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar + 1, 0), tmap);
            return (tick - barTick <= nextBarTick - tick) ? barTick : nextBarTick;
        }

        long beatTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(bar, beat), tmap);

        // Determine beats-per-bar from the time signature at this position
        var beatMetric = TimeConverter.ConvertTo<MetricTimeSpan>(beatTick, tmap);
        var timeSig = tmap.GetTimeSignatureAtTime(beatMetric);
        int beatsPerBar = timeSig.Numerator;

        long nextBeat = beat < beatsPerBar - 1 ? beat + 1 : 0;
        long nextBarForBeat = beat < beatsPerBar - 1 ? bar : bar + 1;
        long nextBeatTick = TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(nextBarForBeat, nextBeat), tmap);

        long beatDuration = nextBeatTick - beatTick;
        if (beatDuration <= 0) return tick;

        long step = beatDuration / subdivFactor;
        if (step <= 0) return tick;

        // Round to nearest step within this beat
        long offset = tick - beatTick;
        long snappedOffset = (long)Math.Round((double)offset / step) * step;
        return Math.Max(0, beatTick + snappedOffset);
    }

    //  Box selection

    private void UpdateBoxSelection()
    {
        float minX = MathF.Min(_boxSelectA.X, _boxSelectB.X);
        float maxX = MathF.Max(_boxSelectA.X, _boxSelectB.X);
        float minY = MathF.Min(_boxSelectA.Y, _boxSelectB.Y);
        float maxY = MathF.Max(_boxSelectA.Y, _boxSelectB.Y);

        // Start from the initial selection snapshot
        _selectedEventIndices.Clear();
        _selectedEventIndices.UnionWith(_boxSelectInitialSelection);

        // Toggle notes inside the box: already-selected → deselect; not selected → select
        foreach (var h in _noteHitList)
        {
            if (h.RectMax.X >= minX && h.RectMin.X <= maxX &&
                h.RectMax.Y >= minY && h.RectMin.Y <= maxY)
            {
                if (_boxSelectInitialSelection.Contains(h.EventIndex))
                    _selectedEventIndices.Remove(h.EventIndex);
                else
                    _selectedEventIndices.Add(h.EventIndex);
            }
        }
    }

    //  Overlay rendering

    /// <summary>
    /// Draws selection highlights and box-select rect.
    /// Must be called inside an active clip rect (between PushClipRect / PopClipRect).
    /// </summary>
    private void DrawEditorOverlay(PianoRenderContext ctx)
    {
        var dl = ctx.DrawList;

        // White border + resize-handle tint on selected notes
        foreach (var h in _noteHitList)
        {
            if (!_selectedEventIndices.Contains(h.EventIndex)) continue;

            dl.AddRect(h.RectMin, h.RectMax, 0xFFFFFFFF, 0f, ImDrawFlags.None, 2f);

            // Resize handle indicator fill
            var handleMin = new Vector2(Math.Max(h.RectMin.X, h.RectMax.X - ResizeHandlePx), h.RectMin.Y);
            dl.AddRectFilled(handleMin, h.RectMax, 0x60FFFFFF);
        }

        // Box-select rectangle
        if (_editorDragMode == EditorDragMode.BoxSelect)
        {
            var bMin = new Vector2(MathF.Min(_boxSelectA.X, _boxSelectB.X), MathF.Min(_boxSelectA.Y, _boxSelectB.Y));
            var bMax = new Vector2(MathF.Max(_boxSelectA.X, _boxSelectB.X), MathF.Max(_boxSelectA.Y, _boxSelectB.Y));
            dl.AddRectFilled(bMin, bMax, 0x334296F9); // semi-transparent blue fill
            dl.AddRect(bMin, bMax, 0xFF4296F9);       // blue border
        }
    }

    //  Keyboard shortcuts

    /// <summary>Handles keyboard shortcuts for the editor piano roll. Call inside the roll child window.</summary>
    private void HandleEditorKeyboard()
    {
        var io = ImGui.GetIO();
        if (!ImGui.GetIO().WantCaptureKeyboard) return;
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;

        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) TransposeSelectedNotes(12);
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) TransposeSelectedNotes(-12);
            if (ImGui.IsKeyPressed(ImGuiKey.A)) SelectAllNotesInTrack();
        }
        else
        {
            if (ImGui.IsKeyDown(ImGuiKey.Delete) && _selectedEventIndices.Count > 0)
                DeleteSelectedEvents();
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                _selectedEventIndices.Clear();
        }
    }

    //  Helpers

    private void TransposeSelectedNotes(int semitones)
    {
        var events = CurrentEvents;
        if (events == null || _file == null || _selectedEventIndices.Count == 0) return;

        foreach (var idx in _selectedEventIndices)
        {
            if ((uint)idx >= (uint)events.Count) continue;
            var ev = events[idx];
            if (ev.NoteOffSource == null) continue;
            ev.RefreshEditValues();
            ev.EditValue1 = Math.Clamp(ev.EditValue1 + semitones, 0, 127);
            ev.ApplyEditValues();
        }

        _file.IsDirty = true;
    }

    private void SelectAllNotesInTrack()
    {
        var events = CurrentEvents;
        if (events == null) return;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].NoteOffSource != null)
                _selectedEventIndices.Add(i);
        }
    }

    private void TriggerScrollToEvent(int eventIndex)
    {
        _pianoRollScrollToSelected = true;
        _pianoRollScrollTarget = eventIndex;
    }

    /// <summary>
    /// Returns the latest end-tick of any note with <paramref name="noteNum"/> that contains <paramref name="tick"/>.
    /// If nothing overlaps, returns <paramref name="tick"/> unchanged.
    /// </summary>
    private static long FindOverlapEndTick(List<EditableEvent>? events, int noteNum, long tick)
    {
        if (events == null) return tick;
        long end = tick;
        foreach (var ev in events)
        {
            if (ev.NoteOffSource == null) continue;
            if (ev.Source.Event is not NoteOnEvent noteOn) continue;
            if ((byte)noteOn.NoteNumber != noteNum) continue;
            long evStart = ev.Tick;
            long evEnd = ev.Tick + ev.DurationTicks;
            if (evStart <= tick && tick < evEnd)
                end = Math.Max(end, evEnd);
        }
        return end;
    }

    /// <summary>
    /// Finds the start tick of the earliest note with <paramref name="noteNum"/> that begins after <paramref name="afterTick"/>,
    /// excluding <paramref name="exclude"/>. Returns <see cref="long.MaxValue"/> if none found.
    /// </summary>
    private static long FindNextNoteStartAfter(List<EditableEvent>? events, EditableEvent exclude, int noteNum, long afterTick)
    {
        if (events == null) return long.MaxValue;
        long min = long.MaxValue;
        foreach (var ev in events)
        {
            if (ReferenceEquals(ev, exclude)) continue;
            if (ev.NoteOffSource == null) continue;
            if (ev.Source.Event is not NoteOnEvent noteOn) continue;
            if ((byte)noteOn.NoteNumber != noteNum) continue;
            if (ev.Tick > afterTick)
                min = Math.Min(min, ev.Tick);
        }
        return min;
    }
}
