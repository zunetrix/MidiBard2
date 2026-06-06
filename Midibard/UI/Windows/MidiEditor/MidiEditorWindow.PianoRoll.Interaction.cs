using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands.Note;

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

        // Use the same display-note formula as DrawNotes so hit rects match rendered positions.
        int transposeFromName = trackDisplayState?.TrackInfo.TransposeFromTrackName ?? 0;
        bool showAdapted = trackDisplayState?.ShowAdaptedNotes ?? false;

        double viewStart = ctx.View.StartTime;
        double viewEnd = ctx.View.EndTime;

        // Binary search: find first event whose StartSeconds could be in the viewport.
        // Step back 100 indices to catch long notes that started before the viewport.
        int firstIdx = FindFirstEventIndexByStartSeconds(events, viewStart);
        firstIdx = Math.Max(0, firstIdx - 100);

        for (int i = firstIdx; i < events.Count; i++)
        {
            var ev = events[i];
            if (ev.NoteOffSource == null) continue;
            if (ev.Source.Event is not NoteOnEvent noteOn) continue;

            // Early exit: events are sorted by StartSeconds, so once we pass the viewport end we're done.
            if (ev.StartSeconds > viewEnd) break;

            int displayNote = TrackInfo.TranslateNoteNumber((byte)noteOn.NoteNumber, transposeFromName, showAdapted) + 48;

            if (!ctx.IsNoteVisible(ev.StartSeconds, ev.EndSeconds, displayNote)) continue;

            _noteHitList.Add(new NoteHitEntry(ctx.NoteRectMin(ev.StartSeconds, displayNote), ctx.NoteRectMax(ev.EndSeconds, displayNote), i));
        }
    }

    // Binary search on EditableEvent.StartSeconds (monotonic with Tick, so preserves sort order).
    private static int FindFirstEventIndexByStartSeconds(List<EditableEvent> events, double minStartSeconds)
    {
        int lo = 0, hi = events.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (events[mid].StartSeconds < minStartSeconds) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    //  Hit testing

    private (int hitIndex, NoteHitZone zone) HitTestNote(Vector2 mousePos)
    {
        // Iterate in reverse so topmost-rendered note wins
        for (int i = _noteHitList.Count - 1; i >= 0; i--)
        {
            var h = _noteHitList[i];
            if (mousePos.X < h.RectMin.X || mousePos.X > h.RectMax.X ||
                mousePos.Y < h.RectMin.Y || mousePos.Y > h.RectMax.Y)
                continue;

            if (mousePos.X <= h.RectMin.X + ResizeHandlePx)
                return (h.EventIndex, NoteHitZone.StartResize);

            if (mousePos.X >= h.RectMax.X - ResizeHandlePx)
                return (h.EventIndex, NoteHitZone.EndResize);

            return (h.EventIndex, NoteHitZone.Body);
        }
        return (-1, NoteHitZone.None);
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
        bool rightClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        // Alt held → temporarily enable pencil mode regardless of the toolbar button
        bool pencilEffective = _pencilModeActive || io.KeyAlt;

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
            //  Idle - decide what to start on click
            case EditorDragMode.None:
                {
                    if (isHovered)
                    {
                        if (pencilEffective)
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Arrow);
                        else
                        {
                            var (hoverIdx, hoverZone) = HitTestNote(mousePos);
                            ImGui.SetMouseCursor(hoverIdx >= 0
                                ? (hoverZone is NoteHitZone.StartResize or NoteHitZone.EndResize ? ImGuiMouseCursor.ResizeEw : ImGuiMouseCursor.Arrow)
                                : ImGuiMouseCursor.Hand);
                        }
                    }

                    if (isHovered && leftClicked)
                    {
                        if (pencilEffective)
                        {
                            // Pencil tool: insert a note at click position
                            if (_file != null && _selectedTrackIndex >= 0 && _selectedTrackIndex < _file.Tracks.Count)
                            {
                                var track = _file.Tracks[_selectedTrackIndex];
                                if (track.Events != null && !track.IsConductorTrack)
                                {
                                    var tmap = _file.TempoMap;
                                    int noteNum = Math.Clamp((int)Math.Ceiling(ctx.View.TopNote - (mousePos.Y - ctx.Y) / ctx.View.NoteHeight), 0, 127);
                                    double sec = ctx.ScreenXToTime(mousePos.X);
                                    long tick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(Math.Max(0.0, sec) * 1_000_000.0)), tmap);
                                    tick = SnapTickToGrid(tick, tmap);
                                    // Block insertion if the click lands inside an existing same-pitch note.
                                    if (InsertNoteCommand.FindOverlapEndTick(track.Events, noteNum, tick) != tick) break;
                                    int ppqn = _file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision td ? td.TicksPerQuarterNote : 480;
                                    long duration = MidiEditorPencilNoteSizing.GetDurationTicks(ppqn, _pencilNoteDivisionIndex);

                                    // Check if the initial duration would overlap the next same-pitch note
                                    long nextStart = InsertNoteCommand.FindNextNoteStartAfter(track.Events, null, noteNum, tick);
                                    // Store the max allowed duration so PencilDraw drag cannot re-introduce overlap
                                    _pencilNoteMaxDur = nextStart == long.MaxValue ? long.MaxValue : nextStart - tick;
                                    if (nextStart < tick + duration)
                                    {
                                        if (_pencilAutoTrim)
                                            duration = nextStart - tick;  // trim to fit
                                        else
                                            duration = 0;                  // block: refuse to insert
                                    }

                                    if (duration > 0)
                                    {
                                        BeginEditorCommandGesture();
                                        _pencilNoteStartTick = tick;
                                        _pencilDragOriginSec = sec;
                                        var insertResult = _editorCommandExecutor.Execute(
                                            new InsertNoteCommand(),
                                            CreateEditorCommandContext(),
                                            new InsertNoteOptions(
                                                _selectedTrackIndex,
                                                tick,
                                                noteNum,
                                                80,
                                                duration,
                                                PreventOverlap: true,
                                                TrimToFit: _pencilAutoTrim));
                                        var insertedIdx = insertResult.Result?.Value.InsertedEventIndex ?? -1;
                                        _pencilDragEvent = insertedIdx >= 0 && insertedIdx < track.Events.Count
                                            ? track.Events[insertedIdx]
                                            : null;

                                        if (insertResult.Succeeded && insertResult.Changed && _pencilDragEvent != null)
                                        {
                                            track.RefreshEventMetricTimes(_file.TempoMap);
                                            // Shift any selected indices that were pushed back by the insertion
                                            if (insertedIdx >= 0 && _selectedEventIndices.Count > 0)
                                            {
                                                var shifted = new HashSet<int>();
                                                foreach (var selIdx in _selectedEventIndices)
                                                    shifted.Add(selIdx >= insertedIdx ? selIdx + 1 : selIdx);
                                                _selectedEventIndices.Clear();
                                                _selectedEventIndices.UnionWith(shifted);
                                            }
                                            _editorDragMode = EditorDragMode.PencilDraw;
                                        }
                                        else
                                        {
                                            _pencilDragEvent = null;
                                            CancelEditorCommandGesture();
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            var (hitIdx, hitZone) = HitTestNote(mousePos);

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
                                if (hitZone is NoteHitZone.StartResize or NoteHitZone.EndResize)
                                {
                                    if (!_selectedEventIndices.Contains(hitIdx))
                                    { _selectedEventIndices.Clear(); _selectedEventIndices.Add(hitIdx); }
                                    SnapshotPreDragState();
                                    BeginEditorCommandGesture();
                                    _dragOriginSeconds = ctx.ScreenXToTime(mousePos.X);
                                    _resizeFromStart = hitZone == NoteHitZone.StartResize;
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
                                    BeginEditorCommandGesture();
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

                    // Pencil right-click: delete note under cursor
                    if (isHovered && rightClicked && pencilEffective)
                    {
                        var (hitIdx, _) = HitTestNote(mousePos);
                        var noteKey = TryCreateNoteSelectionKey(hitIdx);
                        if (noteKey.HasValue && _file != null && _selectedTrackIndex >= 0 && _selectedTrackIndex < _file.Tracks.Count)
                        {
                            var track = _file.Tracks[_selectedTrackIndex];
                            if (track.Events != null && hitIdx < track.Events.Count)
                            {
                                var result = _editorCommandExecutor.Execute(
                                    new DeleteNoteCommand(),
                                    CreateEditorCommandContext(),
                                    new DeleteNoteOptions(_selectedTrackIndex, noteKey.Value));
                                if (result.Succeeded && result.Changed)
                                {
                                    _selectedEventIndices.Remove(hitIdx);
                                    // Shift selection indices down for all notes after the deleted one
                                    var shiftedDown = new HashSet<int>();
                                    foreach (var selIdx in _selectedEventIndices)
                                        shiftedDown.Add(selIdx > hitIdx ? selIdx - 1 : selIdx);
                                    _selectedEventIndices.Clear();
                                    _selectedEventIndices.UnionWith(shiftedDown);
                                    ApplyEditorCommandRefreshHints();
                                }
                            }
                        }
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
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                    CommitEditorCommandGesture();
                    _preDragSnapshot.Clear();
                }
                break;

            case EditorDragMode.Resize:
                if (leftDown && isActive)
                {
                    ApplyNoteResizeFromDrag(ctx, mousePos, _resizeFromStart);
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                    _resizeFromStart = false;
                    CommitEditorCommandGesture();
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
                        long minDur = MidiEditorPencilNoteSizing.GetDurationTicks(ppqn, _pencilNoteDivisionIndex);
                        // Cap at the next same-pitch note to prevent overlap; track whether capped
                        bool cappedByNext = false;
                        if (_pencilDragEvent.Source.Event is NoteOnEvent pencilNoteOn)
                        {
                            int pencilNoteNum = (byte)pencilNoteOn.NoteNumber;
                            var track = _file.Tracks[_selectedTrackIndex];
                            long nextStart = InsertNoteCommand.FindNextNoteStartAfter(track.Events, _pencilDragEvent, pencilNoteNum, _pencilNoteStartTick);
                            if (nextStart != long.MaxValue && endTick > nextStart)
                            {
                                endTick = nextStart;
                                cappedByNext = true;
                            }
                        }
                        // Don't let minDur override the nextStart cap - that would re-introduce overlap
                        long newDur = cappedByNext
                            ? Math.Max(1, endTick - _pencilNoteStartTick)
                            : Math.Max(minDur, endTick - _pencilNoteStartTick);
                        // Also enforce the max duration captured at note insertion (trim-to-fit case)
                        if (_pencilNoteMaxDur != long.MaxValue && newDur > _pencilNoteMaxDur)
                            newDur = Math.Max(1, _pencilNoteMaxDur);
                        if (newDur != _pencilDragEvent.DurationTicks)
                        {
                            ResizePencilDragNote(newDur);
                        }
                    }
                }
                else
                {
                    _editorDragMode = EditorDragMode.None;
                    CommitEditorCommandGesture();
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
        _previewState.NoteMinHeight = Math.Clamp(_previewState.NoteMinHeight * z, 4f, 200f);
        _previewState.TimePixelsPerSecond = Math.Clamp(_previewState.TimePixelsPerSecond * z, 5f, 700f);
    }

    //  Note mutations

    private bool ApplyNoteMoveFromDrag(PianoRenderContext ctx, Vector2 mousePos)
    {
        var events = CurrentEvents;
        if (events == null || _file == null) return false;

        var tmap = _file.TempoMap;
        double curSec = ctx.ScreenXToTime(mousePos.X);
        double deltaSeconds = curSec - _dragOriginSeconds;
        float curNoteOffset = ctx.View.TopNote - (mousePos.Y - ctx.Y) / ctx.View.NoteHeight;
        int noteDelta = (int)MathF.Round(curNoteOffset - _dragOriginNoteOffset);
        var io = ImGui.GetIO();
        if (io.KeyShift && !io.KeyAlt)
            deltaSeconds = 0;
        else if (io.KeyAlt && !io.KeyShift)
            noteDelta = 0;
        var edits = new List<NoteEditOperation>();

        foreach (var (idx, snap) in _preDragSnapshot)
        {
            if ((uint)idx >= (uint)events.Count) continue;
            var noteKey = TryCreateNoteSelectionKey(events, idx);
            if (!noteKey.HasValue) continue;

            double origSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)snap.tick, tmap).TotalMicroseconds / 1_000_000.0;
            double newSec = Math.Max(0.0, origSec + deltaSeconds);
            long newTick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(newSec * 1_000_000.0)), tmap);
            newTick = SnapTickToGrid(newTick, tmap);

            int newPitch = Math.Clamp(snap.val1 + noteDelta, 0, 127);

            // Clamp newTick so it doesn't produce overlap with non-moving notes at the target pitch
            long prevEnd = 0;
            long nextStartForNote = long.MaxValue;
            for (int j = 0; j < events.Count; j++)
            {
                if (_preDragSnapshot.ContainsKey(j)) continue;  // skip notes being moved together
                var ev2 = events[j];
                if (ev2.NoteOffSource == null) continue;
                if (ev2.Source.Event is not NoteOnEvent noteOn2) continue;
                if ((byte)noteOn2.NoteNumber != newPitch) continue;
                long ev2End = ev2.Tick + ev2.DurationTicks;
                if (ev2End <= newTick)
                    prevEnd = Math.Max(prevEnd, ev2End);
                else
                    nextStartForNote = Math.Min(nextStartForNote, ev2.Tick);
            }
            newTick = Math.Max(newTick, prevEnd);
            if (nextStartForNote != long.MaxValue && newTick + snap.dur > nextStartForNote)
                newTick = Math.Max(prevEnd, nextStartForNote - snap.dur);

            if (newTick == snap.tick && newPitch == snap.val1)
                continue;

            edits.Add(new NoteEditOperation(
                noteKey.Value,
                new NoteEditValues(
                    Math.Max(0, newTick),
                    newPitch,
                    snap.val2,
                    snap.dur)));
        }

        if (edits.Count == 0)
            return false;

        var result = _editorCommandExecutor.Execute(
            new MoveSelectedNotesCommand(),
            CreateEditorCommandContext(),
            new MoveSelectedNotesOptions(_selectedTrackIndex, edits));
        if (result.Succeeded && result.Changed && _file != null)
            _file.Tracks[_selectedTrackIndex].RefreshEventMetricTimes(_file.TempoMap);
        return result.Succeeded && result.Changed;
    }

    private bool ApplyNoteResizeFromDrag(PianoRenderContext ctx, Vector2 mousePos, bool fromStart)
    {
        var events = CurrentEvents;
        if (events == null || _file == null) return false;

        var tmap = _file.TempoMap;
        double curSec = ctx.ScreenXToTime(mousePos.X);
        double deltaSeconds = curSec - _dragOriginSeconds;
        var edits = new List<NoteEditOperation>();

        foreach (var (idx, snap) in _preDragSnapshot)
        {
            if ((uint)idx >= (uint)events.Count) continue;
            var noteKey = TryCreateNoteSelectionKey(events, idx);
            if (!noteKey.HasValue) continue;

            long newTick;
            long newDur;
            if (fromStart)
            {
                double origStartSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)snap.tick, tmap).TotalMicroseconds / 1_000_000.0;
                double newStartSec = Math.Max(0.0, origStartSec + deltaSeconds);
                long newStartTick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(newStartSec * 1_000_000.0)), tmap);
                newStartTick = SnapTickToGrid(newStartTick, tmap);
                long endTick = snap.tick + snap.dur;
                newTick = Math.Clamp(newStartTick, 0, endTick - 1);
                newDur = endTick - newTick;
            }
            else
            {
                double origStartSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)snap.tick, tmap).TotalMicroseconds / 1_000_000.0;
                double origEndSec = TimeConverter.ConvertTo<MetricTimeSpan>((long)(snap.tick + snap.dur), tmap).TotalMicroseconds / 1_000_000.0;
                double newEndSec = Math.Max(origStartSec + 0.01, origEndSec + deltaSeconds);
                long newEndTick = TimeConverter.ConvertFrom(new MetricTimeSpan((long)(newEndSec * 1_000_000.0)), tmap);
                newEndTick = SnapTickToGrid(newEndTick, tmap);
                newTick = snap.tick;
                newDur = Math.Max(1, newEndTick - snap.tick);
            }

            if (newTick == snap.tick && newDur == snap.dur)
                continue;

            edits.Add(new NoteEditOperation(
                noteKey.Value,
                new NoteEditValues(
                    newTick,
                    snap.val1,
                    snap.val2,
                    newDur)));
        }

        if (edits.Count == 0)
            return false;

        var result = _editorCommandExecutor.Execute(
            new ResizeSelectedNotesCommand(),
            CreateEditorCommandContext(),
            new ResizeSelectedNotesOptions(_selectedTrackIndex, edits));
        if (result.Succeeded && result.Changed && _file != null)
            _file.Tracks[_selectedTrackIndex].RefreshEventMetricTimes(_file.TempoMap);
        return result.Succeeded && result.Changed;
    }

    private void ResizePencilDragNote(long durationTicks)
    {
        if (_pencilDragEvent == null || _file == null)
            return;

        var track = _selectedTrackIndex >= 0 && _selectedTrackIndex < _file.Tracks.Count
            ? _file.Tracks[_selectedTrackIndex]
            : null;
        if (track?.Events == null)
            return;

        var eventIndex = track.Events.IndexOf(_pencilDragEvent);
        var noteKey = TryCreateNoteSelectionKey(track.Events, eventIndex);
        if (!noteKey.HasValue || _pencilDragEvent.Source.Event is not NoteOnEvent noteOn)
            return;

        var result = _editorCommandExecutor.Execute(
            new ResizeSelectedNotesCommand(),
            CreateEditorCommandContext(),
            new ResizeSelectedNotesOptions(
                _selectedTrackIndex,
                new[]
                {
                    new NoteEditOperation(
                        noteKey.Value,
                        new NoteEditValues(
                            _pencilDragEvent.Tick,
                            (byte)noteOn.NoteNumber,
                            (byte)noteOn.Velocity,
                            durationTicks))
                }));
        if (result.Succeeded && result.Changed && _file != null)
            _file.Tracks[_selectedTrackIndex].RefreshEventMetricTimes(_file.TempoMap);
    }

    private NoteSelectionKey? TryCreateNoteSelectionKey(int eventIndex)
        => TryCreateNoteSelectionKey(CurrentEvents, eventIndex);

    private static NoteSelectionKey? TryCreateNoteSelectionKey(
        IReadOnlyList<EditableEvent>? events,
        int eventIndex)
    {
        if (events == null || (uint)eventIndex >= (uint)events.Count)
            return null;

        var editableEvent = events[eventIndex];
        return editableEvent.NoteOffSource != null && editableEvent.Source.Event is NoteOnEvent
            ? NoteSelectionKey.FromEvent(eventIndex, editableEvent)
            : null;
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

        long step;
        if (_previewState.SnapUseTuplet
            && _previewState.SnapTupletNotes > 0
            && _previewState.SnapTupletSpaceOf > 0)
        {
            // Tuplet: N notes in the space of M regular subdivisions
            // step = (beatDuration / subdivFactor) * spaceOf / notes
            step = beatDuration * _previewState.SnapTupletSpaceOf
                / (subdivFactor * _previewState.SnapTupletNotes);
        }
        else
        {
            step = beatDuration / subdivFactor;
        }

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
        if (_selectedEventIndices.Count > 0)
        {
            foreach (var h in _noteHitList)
            {
                if (!_selectedEventIndices.Contains(h.EventIndex)) continue;

                dl.AddRect(h.RectMin, h.RectMax, 0xFFFFFFFF, 0f, ImDrawFlags.None, 2f);

                // Resize handle indicator fill
                var handleMin = new Vector2(Math.Max(h.RectMin.X, h.RectMax.X - ResizeHandlePx), h.RectMin.Y);
                dl.AddRectFilled(handleMin, h.RectMax, 0x60FFFFFF);
            }
        }

        // Box-select rectangle
        if (_editorDragMode == EditorDragMode.BoxSelect)
        {
            var bMin = new Vector2(MathF.Min(_boxSelectA.X, _boxSelectB.X), MathF.Min(_boxSelectA.Y, _boxSelectB.Y));
            var bMax = new Vector2(MathF.Max(_boxSelectA.X, _boxSelectB.X), MathF.Max(_boxSelectA.Y, _boxSelectB.Y));
            dl.AddRectFilled(bMin, bMax, 0x334296F9); // semi-transparent blue fill
            dl.AddRect(bMin, bMax, 0xFF4296F9);       // blue border
        }

        DrawPlaybackPreviewLine(ctx);
    }

    private void DrawPlaybackPreviewLine(PianoRenderContext ctx)
    {
        if (_playbackPreview.DurationSeconds <= 0)
            return;

        var position = _playbackPreview.PositionSeconds;
        if (position < ctx.View.StartTime || position > ctx.View.EndTime)
            return;

        var x = ctx.X + (float)((position - ctx.View.StartTime) * ctx.View.PixelsPerSecond);
        const uint lineColor = 0xFFFFD34D;
        const uint fillColor = 0x99FFD34D;

        ctx.DrawList.AddLine(new Vector2(x, ctx.CanvasMin.Y), new Vector2(x, ctx.CanvasMax.Y), lineColor, 2f);
        ctx.DrawList.AddTriangleFilled(
            new Vector2(x, ctx.CanvasMin.Y + 1f),
            new Vector2(x - 5f, ctx.CanvasMin.Y + 10f),
            new Vector2(x + 5f, ctx.CanvasMin.Y + 10f),
            fillColor);
    }

    //  Keyboard shortcuts

    /// <summary>Handles keyboard shortcuts for the editor piano roll. Call inside the roll child window.</summary>
    private void HandleEditorKeyboard()
    {
        var io = ImGui.GetIO();
        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        var textInputActive = io.WantTextInput || _editingEvent != null || _editingTrack != null;
        MidiEditorKeyboardAction action;
        unsafe
        {
            var input = UIInputData.Instance();
            action = MidiEditorKeyboardShortcuts.Resolve(new MidiEditorKeyboardShortcutState(
                PianoRollFocused: focused,
                TextInputActive: textInputActive,
                CtrlDown: input->IsKeyDown(SeVirtualKey.CONTROL),
                UpPressed: input->IsKeyPressed(SeVirtualKey.UP),
                DownPressed: input->IsKeyPressed(SeVirtualKey.DOWN),
                LeftPressed: input->IsKeyPressed(SeVirtualKey.LEFT),
                RightPressed: input->IsKeyPressed(SeVirtualKey.RIGHT),
                APressed: input->IsKeyPressed(SeVirtualKey.A),
                CPressed: input->IsKeyPressed(SeVirtualKey.C),
                VPressed: input->IsKeyPressed(SeVirtualKey.V),
                DeletePressed: input->IsKeyPressed(SeVirtualKey.DELETE),
                EscapePressed: input->IsKeyPressed(SeVirtualKey.ESCAPE)));
        }

        switch (action)
        {
            case MidiEditorKeyboardAction.TransposeOctaveUp:
                TransposeSelectedNotes(12);
                break;
            case MidiEditorKeyboardAction.TransposeOctaveDown:
                TransposeSelectedNotes(-12);
                break;
            case MidiEditorKeyboardAction.MoveNotesLeft:
                NudgeSelectedNotesByGrid(-1);
                break;
            case MidiEditorKeyboardAction.MoveNotesRight:
                NudgeSelectedNotesByGrid(1);
                break;
            case MidiEditorKeyboardAction.SelectAllNotes:
                SelectAllNotesInTrack();
                break;
            case MidiEditorKeyboardAction.CopySelectedNotes:
                CopySelectedNotes();
                break;
            case MidiEditorKeyboardAction.PasteCopiedNotes:
                PasteCopiedNotes();
                break;
            case MidiEditorKeyboardAction.DeleteSelection:
                DeleteEditorKeyboardSelection();
                break;
            case MidiEditorKeyboardAction.ClearSelection:
                ClearEditorKeyboardSelection();
                break;
        }
    }

    //  Helpers
    private void TransposeSelectedNotes(int semitones)
    {
        if (_file == null || semitones == 0) return;

        var selectedNoteKeys = GetSelectedNoteKeys();
        if (selectedNoteKeys.Count == 0) return;

        var result = _editorCommandExecutor.Execute(
            new TransposeSelectedNotesCommand(),
            CreateEditorCommandContext(),
            new TransposeSelectedNotesOptions(
                _selectedTrackIndex,
                selectedNoteKeys,
                semitones));
        if (result.Succeeded)
            ApplyEditorCommandRefreshHints();
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

    private void DeleteEditorKeyboardSelection()
    {
        if (_selectedEventIndices.Count == 0)
            return;

        if (GetSelectedNoteKeys().Count > 0)
            DeleteSelectedNotes();
        else
            DeleteSelectedEvents();
    }

    private void ClearEditorKeyboardSelection()
    {
        if (_selectedEventIndices.Count > 0)
            _selectedEventIndices.Clear();
    }

    private void TriggerScrollToEvent(int eventIndex)
    {
        _pianoRollScrollToSelected = true;
        _pianoRollScrollTarget = eventIndex;
    }
}
