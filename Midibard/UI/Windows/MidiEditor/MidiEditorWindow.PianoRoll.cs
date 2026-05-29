using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Preview;
using MidiBard.Extensions.Time;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private double _previewMaxTime = 10.0;

    private void DrawPianoRollPanel()
    {
        using var child = ImRaii.Child("##PianoRollChild", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar);
        if (!child) return;

        // Rebuild preview tracks when the file reference or its content changes
        if (_file != _previewFile || _file?.Version != _previewFileVersion)
        {
            bool isNewFile = _file != _previewFile;
            var oldTracks = isNewFile ? null : _previewTracks;
            _previewFile = _file;
            _previewFileVersion = _file?.Version ?? -1;
            if (_file != null)
            {
                var previewSnapshot = _previewQueryExecutor.Execute(
                    new BuildPreviewSnapshotQuery(),
                    CreatePreviewQueryContext(),
                    new EditorOperationEmptyOptions());
                if (previewSnapshot.Succeeded)
                {
                    _previewTracks = CreatePreviewTrackDisplayStates(previewSnapshot.Result!.Value);
                    _previewMaxTime = previewSnapshot.Result.Value.MaxTimeSeconds;
                }
                else
                {
                    _previewTracks = [];
                    _previewMaxTime = 10;
                }

                _previewTempoMap = _file.TempoMap;
                if (oldTracks != null && _previewTrackOrder != null)
                {
                    // Match by EditableTrack reference using the PREVIOUS frame's track order,
                    // so display settings survive DnD reorders (list is mutated in-place).
                    var oldStateByTrack = new Dictionary<EditableTrack, TrackDisplayState>(_previewTrackOrder.Length);
                    for (int i = 0; i < oldTracks.Length && i < _previewTrackOrder.Length; i++)
                        oldStateByTrack[_previewTrackOrder[i]] = oldTracks[i];

                    for (int i = 0; i < _previewTracks.Length && i < _file.Tracks.Count; i++)
                    {
                        if (oldStateByTrack.TryGetValue(_file.Tracks[i], out var old))
                        {
                            _previewTracks[i].ShowAdaptedNotes = old.ShowAdaptedNotes;
                            _previewTracks[i].Color = old.Color;
                            _previewTracks[i].Visible = old.Visible;
                            _previewTracks[i].IsLocked = old.IsLocked;
                        }
                    }
                }
                else
                {
                    _previewState.CheckAllTracks = true;
                    _previewState.SelectedVoiceLimitItem = 0;
                }
                _previewTrackOrder = _file.Tracks.ToArray(); // snapshot current order for next rebuild
                RefreshPreviewVoiceLimits();
                _playbackPreview.Load(_file, preservePosition: !isNewFile);
            }
            else
            {
                _previewTracks = null;
                _previewTempoMap = null;
                _previewState.VoiceLimitRegions = new List<(double, double, int)>();
                _playbackPreview.Load(null, preservePosition: false);
            }
            // Only reset camera when switching to a different file
            if (isNewFile)
            {
                _previewState.CameraTime = 0;
                if (_previewTracks != null)
                    CenterPreviewCamera();
            }
        }

        if (_previewTracks == null)
        {
            ImGui.TextDisabled("No file loaded.");
            return;
        }

        _playbackPreview.Update();

        // Sync selected track's notes from live events so dragged positions render immediately
        if (_selectedTrackIndex >= 0 && _selectedTrackIndex < _previewTracks.Length &&
            _file != null && _previewTempoMap != null)
        {
            var liveTrack = _file.Tracks[_selectedTrackIndex];
            if (liveTrack.Events != null)
            {
                var tmap = _previewTempoMap;
                _previewTracks[_selectedTrackIndex].Notes = liveTrack.Events
                    .Where(ev => ev.NoteOffSource != null && ev.Source.Event is NoteOnEvent)
                    .Select(ev => (
                        TimeConverter.ConvertTo<MetricTimeSpan>(ev.Tick, tmap).TotalMicroseconds / 1_000_000.0,
                        TimeConverter.ConvertTo<MetricTimeSpan>(ev.Tick + ev.DurationTicks, tmap).TotalMicroseconds / 1_000_000.0,
                        (int)(byte)((NoteOnEvent)ev.Source.Event).NoteNumber
                    ))
                    .ToArray();
            }
        }

        DrawPreviewToolbar();

        var contentRegion = ImGui.GetContentRegionAvail();
        const float splitterWidth = 5f;
        float minPanelPx = 100f * ImGuiHelpers.GlobalScale;
        float pianoKeyWidth = PianoRollState.PianoKeyWidth;
        float maxPanelPx = MathF.Max(minPanelPx, contentRegion.X - minPanelPx - pianoKeyWidth);
        _previewLeftPanelWidth = MathF.Max(minPanelPx, MathF.Min(_previewLeftPanelWidth, maxPanelPx));

        float trackPanelWidth = _previewState.ShowLeftPanel ? _previewLeftPanelWidth : 0f;
        float effectiveSplitter = _previewState.ShowLeftPanel ? splitterWidth : 0f;
        float pianoRollWidth = contentRegion.X - trackPanelWidth - effectiveSplitter - pianoKeyWidth;
        float pianoRollHeight = contentRegion.Y;

        var pianoRoll = _plugin.Ui.PianoRollWindow;

        if (_previewState.ShowLeftPanel)
        {
            ImGui.BeginChild("##PreviewLeftPanel", new Vector2(_previewLeftPanelWidth, contentRegion.Y), true, ImGuiWindowFlags.NoScrollbar);

            // float maxListHeight = contentRegion.Y * 0.5f;
            // float trackChildHeight = Math.Clamp(_previewTrackListContentHeight, ImGui.GetFrameHeightWithSpacing(), maxListHeight);
            // ImGui.BeginChild("##PreviewTrackListArea", new Vector2(0, trackChildHeight), false);
            // DrawPreviewTrackList(pianoRollWidth);
            // _previewTrackListContentHeight = ImGui.GetCursorPosY();
            // ImGui.EndChild();

            ImGui.BeginChild("##PreviewVoiceLimitArea", new Vector2(0, -1), false);
            DrawPreviewVoiceLimitList(pianoRollWidth);
            ImGui.EndChild();

            ImGui.EndChild();
            ImGui.SameLine();
            pianoRoll.DrawSplitter("##PreviewSplitter", ref _previewLeftPanelWidth, minPanelPx, maxPanelPx);
            ImGui.SameLine();
        }

        if (pianoRollWidth <= 0 || pianoRollHeight <= 0) return;
        _pianoRollWidthCache = pianoRollWidth;

        ImGui.BeginChild("##PreviewRollArea", new Vector2(contentRegion.X - trackPanelWidth - effectiveSplitter, contentRegion.Y), false);
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();

        FollowPlaybackPreview(pianoRollWidth);
        var view = BuildPreviewViewport(pianoRollWidth, pianoRollHeight);
        var ctx = new PianoRenderContext
        {
            DrawList = drawList,
            X = cursor.X + pianoKeyWidth,
            Y = cursor.Y,
            Width = pianoRollWidth,
            Height = pianoRollHeight,
            CanvasMin = new Vector2(cursor.X + pianoKeyWidth, cursor.Y),
            CanvasMax = new Vector2(cursor.X + pianoKeyWidth + pianoRollWidth, cursor.Y + pianoRollHeight),
            View = view,
            PianoKeyWidth = pianoKeyWidth,
            PianoKeysX = cursor.X,
        };

        // InvisibleButton must come before HandleEditorInteraction so IsItemActive/IsItemHovered refer to it
        ImGui.SetCursorScreenPos(ctx.CanvasMin);
        ImGui.InvisibleButton("##preview_roll", new Vector2(pianoRollWidth, pianoRollHeight),
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
        BuildNoteHitList(ctx);
        HandleEditorInteraction(ctx);
        ImGui.SetCursorScreenPos(cursor);

        drawList.AddRectFilled(ctx.CanvasMin, ctx.CanvasMax, ImGui.ColorConvertFloat4ToU32(_previewState.GridDarkColor));
        drawList.PushClipRect(ctx.CanvasMin, ctx.CanvasMax, true);
        pianoRoll.DrawNoteGrid(ctx, _previewState);
        pianoRoll.DrawTimeGrid(ctx, _previewTempoMap, _previewState);
        pianoRoll.DrawRangeMarkers(ctx, _previewState);
        pianoRoll.DrawNotes(ctx, _previewTracks, _previewState);
        DrawEditorOverlay(ctx);
        if (_previewState.ShowProgramChangeMarkers) DrawProgramChangeMarkers(ctx);
        pianoRoll.DrawVoiceLimitRegions(ctx, _previewState.VoiceLimitRegions);
        drawList.PopClipRect();

        pianoRoll.DrawPianoKeys(ctx);

        // Piano key hover + click-to-play
        ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y));
        ImGui.InvisibleButton("##piano_key_click", new Vector2(pianoKeyWidth, pianoRollHeight),
            ImGuiButtonFlags.MouseButtonLeft);

        if (_previewState.ShowNotePreview)
        {
            if (ImGui.IsItemHovered())
            {
                var mousePos = ImGui.GetMousePos();
                var note = ctx.ScreenYToNote(mousePos.Y);
                if (note >= 0 && note < 128)
                {
                    var isBlack = PianoRollWindow.IsBlackKey[note % 12];
                    var overlayColor = isBlack ? 0x60FFFFFFu : 0x304296F9u;
                    var top = ctx.GetNoteTopY(note);
                    var bottom = top + ctx.View.NoteHeight;
                    drawList.AddRectFilled(
                        new Vector2(cursor.X, top),
                        new Vector2(cursor.X + pianoKeyWidth, bottom),
                        overlayColor);
                }
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var mousePos = ImGui.GetMousePos();
                var note = ctx.ScreenYToNote(mousePos.Y);
                if (note >= 0 && note < 128)
                {
                    var instrumentId = _selectedTrackIndex >= 0
                        ? _playbackPreview.GetTrackInstrumentId(_selectedTrackIndex)
                        : 2u;
                    _playbackPreview.AuditionNote(note, 0, instrumentId, 700);
                }
            }
        }

        HandleEditorKeyboard();
        ImGui.EndChild();
    }

    // private float _previewTrackListContentHeight;
    private void DrawPreviewToolbar()
    {
        DrawPlaybackPreviewControls();

        ImGui.SameLine();

        // Beat division
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        var beatDivision = _previewState.BeatDivision;
        ImGuiUtil.EnumCombo("##PreviewBeatDivision", ref beatDivision,
            labelsOverride: ["Bar", "1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/128"]);
        _previewState.BeatDivision = beatDivision;

        ImGuiUtil.HelpMarker(MidiEditorOperationHelp.PianoRollKeyboardShortcuts);

        ImGui.SameLine();

        // Program change markers toggle
        // ImGui.SameLine();
        // bool pcMarkers = _previewState.ShowProgramChangeMarkers;
        // using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal, pcMarkers))
        // {
        //     if (ImGuiUtil.IconButton(FontAwesomeIcon.Guitar, "##previewPCMarkers",
        //         pcMarkers ? "Program change markers: ON" : "Program change markers: OFF",
        //         size: Style.Dimensions.ButtonLarge))
        //         _previewState.ShowProgramChangeMarkers = !_previewState.ShowProgramChangeMarkers;
        // }


        // Clear note selection
        using (ImRaii.Disabled(_selectedEventIndices.Count == 0))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Eraser, "##previewClearNoteSel",
                MidiEditorOperationHelp.ClearNoteSelection(_selectedEventIndices.Count),
                size: Style.Dimensions.ButtonLarge))
                _selectedEventIndices.Clear();
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(GetSelectedNoteKeys().Count == 0))
        {
            if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Trash, "##previewDeleteSelectedNotes",
                MidiEditorOperationHelp.DeleteSelectedNotes,
                size: Style.Dimensions.ButtonLarge))
                DeleteSelectedNotes();
        }

        ImGui.SameLine();

        // Pencil mode toggle
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal, _pencilModeActive)
                .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueNormal, _pencilModeActive))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Pen, "##previewPencilMode",
                _pencilModeActive ? MidiEditorOperationHelp.PencilModeOn : MidiEditorOperationHelp.PencilModeOff,
                size: Style.Dimensions.ButtonLarge))
                _pencilModeActive = !_pencilModeActive;
        }
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.PencilMode);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(65 * ImGuiHelpers.GlobalScale);
        ImGui.Combo("##pencilNoteSize", ref _pencilNoteDivisionIndex, PencilDivisionLabels, PencilDivisionLabels.Length);
        ImGuiUtil.ToolTip(MidiEditorOperationHelp.PencilNoteSize);

        ImGui.SameLine();

        // Snap to grid toggle
        bool snapActive = _previewState.SnapToGrid;
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal, snapActive))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Magnet, "##previewSnapGrid",
                snapActive ? MidiEditorOperationHelp.SnapToGridOn : MidiEditorOperationHelp.SnapToGridOff,
                size: Style.Dimensions.ButtonLarge))
                _previewState.SnapToGrid = !_previewState.SnapToGrid;
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal, _pencilAutoTrim)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueNormal, _pencilAutoTrim))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Cut, "##pencilAutoTrim",
                _pencilAutoTrim
                    ? MidiEditorOperationHelp.PencilAutoTrimOn
                    : MidiEditorOperationHelp.PencilAutoTrimOff,
                size: Style.Dimensions.ButtonLarge))
                _pencilAutoTrim = !_pencilAutoTrim;
        }

        ImGui.SameLine();

        // Time scale
        ImGuiUtil.IconButton(FontAwesomeIcon.ArrowsLeftRight, "##PreviewTimescaleIcon");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80 * ImGuiHelpers.GlobalScale);
        float timePixels = _previewState.TimePixelsPerSecond;
        if (ImGui.DragFloat("##PreviewTimeScale", ref timePixels, 0.5f, 5f, 700f, "%.0f px/s"))
            _previewState.TimePixelsPerSecond = timePixels;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _previewState.TimePixelsPerSecond = 25f;

        ImGui.SameLine();

        // Note scale
        ImGuiUtil.IconButton(FontAwesomeIcon.ArrowsUpDown, "##PreviewNotescaleIcon");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70 * ImGuiHelpers.GlobalScale);
        float noteHeight = _previewState.NoteMinHeight;
        if (ImGui.DragFloat("##PreviewNoteScale", ref noteHeight, 0.2f, 4f, 200f, "%.0f px"))
            _previewState.NoteMinHeight = noteHeight;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _previewState.NoteMinHeight = 10f;

        ImGui.SameLine();

        // Camera timeline slider
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        float cameraProgress = _previewMaxTime > 0 ? (float)(_previewState.CameraTime / _previewMaxTime) : 0f;
        if (ImGui.SliderFloat("##PreviewCameraSlider", ref cameraProgress, 0f, 1f,
                _previewState.CameraTime.FormatSecondsToTime()))
            _previewState.CameraTime = cameraProgress * _previewMaxTime;

        ImGui.SameLine();

        // Reset
        if (ImGui.Button("Reset View##PreviewReset"))
        {
            _previewState.CameraTime = 0;
            _previewState.TimePixelsPerSecond = 25f;
            _previewState.NoteMinHeight = 10f;
            CenterPreviewCamera();
        }

        ImGuiHelpers.ScaledDummy(0, 2);
    }

    private void DrawPlaybackPreviewControls()
    {
        using (ImRaii.Disabled(!_playbackPreview.HasEvents))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Backward, "##previewRestartPlayback", MidiEditorPreviewControlTooltips.RestartPreview,
                    size: Style.Dimensions.ButtonLarge))
            {
                ExecutePreviewTransportCommand(new RestartPreviewCommand(), new EditorOperationEmptyOptions());
                EnsurePlaybackPreviewVisible(_pianoRollWidthCache);
            }

            ImGui.SameLine();

            var playPauseIcon = _playbackPreview.IsPlaying ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play;
            var playPauseTooltip = _playbackPreview.IsPlaying
                ? MidiEditorPreviewControlTooltips.PausePreview
                : MidiEditorPreviewControlTooltips.ResumePreview;
            if (ImGuiUtil.IconButton(playPauseIcon, "##previewPlayPause", playPauseTooltip,
                    size: Style.Dimensions.ButtonLarge))
            {
                if (_playbackPreview.IsPlaying)
                {
                    ExecutePreviewTransportCommand(new PausePreviewCommand(), new EditorOperationEmptyOptions());
                }
                else
                {
                    ExecutePreviewTransportCommand(new ResumePreviewCommand(), new EditorOperationEmptyOptions());
                    EnsurePlaybackPreviewVisible(_pianoRollWidthCache);
                }
            }

            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##previewStopPlayback", MidiEditorPreviewControlTooltips.StopPreview,
                    size: Style.Dimensions.ButtonLarge))
            {
                ExecutePreviewTransportCommand(new StopPreviewCommand(), new EditorOperationEmptyOptions());
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(145 * ImGuiHelpers.GlobalScale);
            var position = (float)_playbackPreview.PositionSeconds;
            var duration = Math.Max(0.0, _playbackPreview.DurationSeconds);
            if (ImGui.SliderFloat("##previewPlaybackPosition", ref position, 0f, (float)Math.Max(duration, 0.001),
                    $"{_playbackPreview.PositionSeconds.FormatSecondsToTime()} / {duration.FormatSecondsToTime()}"))
            {
                ExecutePreviewTransportCommand(new SeekPreviewCommand(), new SeekPreviewOptions(position));
                EnsurePlaybackPreviewVisible(_pianoRollWidthCache);
            }
        }

    }

    private PreviewCommandExecutionResult<PreviewTransportResult> ExecutePreviewTransportCommand<TOptions>(
        IPreviewCommand<TOptions, PreviewTransportResult> command,
        TOptions options)
        => _previewCommandExecutor.Execute(command, CreatePreviewCommandContext(), options);

    private void StopPlaybackPreview()
        => ExecutePreviewTransportCommand(new StopPreviewCommand(), new EditorOperationEmptyOptions());

    private void DrawPreviewTrackList(float pianoRollWidth)
    {
        if (_previewTracks == null) return;

        if (ImGui.CollapsingHeader($"Tracks##PreviewTracksHeader", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool checkAll = _previewState.CheckAllTracks;
            if (ImGui.Checkbox("##PreviewCheckAll", ref checkAll))
            {
                _previewState.CheckAllTracks = checkAll;
                foreach (var t in _previewTracks) t.Visible = checkAll;
                RefreshPreviewVoiceLimits();
            }
            ImGui.SameLine();
            ImGui.Text("Tracks");

            foreach (var track in _previewTracks)
            {
                var tinfo = track.TrackInfo;
                bool visible = track.Visible;
                var color = track.Color ?? PianoRollWindow.GetTrackColor(tinfo.Index, _previewTracks.Length);

                if (ImGui.ColorButton($"##prevcol{tinfo.Index}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16)))
                    ImGui.OpenPopup($"##prevColorPicker{tinfo.Index}");
                if (ImGui.IsItemHovered())
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                ImGui.SameLine();
                if (ImGui.Checkbox($"[{tinfo.Index + 1:00}] {tinfo.TrackName}##prevTrack{tinfo.Index}", ref visible))
                {
                    track.Visible = visible;
                    RefreshPreviewVoiceLimits();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    ImGui.OpenPopup($"##prevTrackOpts{tinfo.Index}");
                ImGuiUtil.ToolTip("Right-click for options");

                if (ImGui.BeginPopup($"##prevColorPicker{tinfo.Index}"))
                {
                    var pickerColor = track.Color ?? PianoRollWindow.GetTrackColor(tinfo.Index, _previewTracks.Length);
                    if (ImGui.ColorPicker4($"##prevpicker{tinfo.Index}", ref pickerColor, ImGuiColorEditFlags.AlphaBar))
                        track.Color = pickerColor;
                    if (track.Color.HasValue && ImGui.Button("Reset##prevColorReset"))
                        track.Color = null;
                    ImGui.EndPopup();
                }

                if (ImGui.BeginPopup($"##prevTrackOpts{tinfo.Index}"))
                {
                    bool adapted = track.ShowAdaptedNotes;
                    if (ImGui.Checkbox($"Show Adapted Notes##prevAdapted{tinfo.Index}", ref adapted))
                        track.ShowAdaptedNotes = adapted;
                    ImGui.EndPopup();
                }
            }
        }
    }

    private void DrawPreviewVoiceLimitList(float pianoRollWidth)
    {
        if (_previewTracks == null) return;

        var regions = _previewState.VoiceLimitRegions;

        if (ImGui.CollapsingHeader($"Voice Limit ({regions.Count})##PreviewVoiceLimitHeader"))
        {
            ImGui.SetNextItemWidth(80 * ImGuiHelpers.GlobalScale);
            int maxVoices = _previewState.MaxVoiceLimit;
            if (ImGui.InputInt("Max##PreviewMaxVoice", ref maxVoices, 1, 1))
            {
                _previewState.MaxVoiceLimit = Math.Clamp(maxVoices, 1, 30);
                RefreshPreviewVoiceLimits();
            }

            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                bool selected = _previewState.SelectedVoiceLimitItem == i;
                using (ImRaii.PushColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered, selected)
                    .Push(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered, selected)
                    .Push(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered, selected))
                {
                    string label = $"{i + 1:000} - {r.start.GetDurationString()} ({r.noteCount})##prevVL_{i}";
                    if (ImGui.Selectable(label, selected))
                    {
                        _previewState.SelectedVoiceLimitItem = i;
                        CenterPreviewViewOnTime(r.start, pianoRollWidth);
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void RefreshPreviewVoiceLimits()
    {
        if (_previewTracks == null)
        {
            _previewState.VoiceLimitRegions = new List<(double, double, int)>();
            return;
        }
        _previewState.VoiceLimitRegions = PianoRollWindow.ComputeVoiceLimitRegions(
            _previewTracks, _previewState.MaxVoiceLimit, _previewState.GroupVoiceLimitRegions);
    }

    private void CenterPreviewViewOnTime(double time, float pianoRollWidth)
    {
        float visibleTime = pianoRollWidth / _previewState.TimePixelsPerSecond;
        _previewState.CameraTime = time - visibleTime * 0.3;
        _previewState.CameraTime = Math.Max(0, Math.Min(_previewState.CameraTime, _previewMaxTime));
    }

    private void FollowPlaybackPreview(float pianoRollWidth)
    {
        if (!_playbackPreview.IsPlaying)
            return;

        var visibleTime = GetPreviewVisibleTime(pianoRollWidth);
        _previewState.CameraTime = MidiEditorPreviewCamera.FollowPlayback(
            _previewState.CameraTime,
            _playbackPreview.PositionSeconds,
            visibleTime,
            _previewMaxTime);
    }

    private void EnsurePlaybackPreviewVisible(float pianoRollWidth)
    {
        var visibleTime = GetPreviewVisibleTime(pianoRollWidth);
        _previewState.CameraTime = MidiEditorPreviewCamera.EnsureVisible(
            _previewState.CameraTime,
            _playbackPreview.PositionSeconds,
            visibleTime,
            _previewMaxTime);
    }

    private double GetPreviewVisibleTime(float pianoRollWidth)
        => MidiEditorPreviewCamera.GetVisibleTime(pianoRollWidth, _previewState.TimePixelsPerSecond);

    private void SetPreviewCameraTime(double cameraTime)
        => _previewState.CameraTime = MidiEditorPreviewCamera.Clamp(cameraTime, _previewMaxTime);

    private static TrackDisplayState[] CreatePreviewTrackDisplayStates(PreviewSnapshot snapshot)
        => snapshot.Tracks
            .Select(track => new TrackDisplayState
            {
                // Use empty TrackName so TransposeFromTrackName = 0 - the editor always shows raw MIDI positions.
                TrackInfo = new TrackInfo { Index = track.TrackIndex, TrackName = string.Empty },
                Notes = track.Notes
                    .Select(note => (note.StartSeconds, note.EndSeconds, note.NoteNumber))
                    .ToArray(),
                Visible = !track.IsConductorTrack,
                ShowAdaptedNotes = false,
            })
            .ToArray();

    private void CenterPreviewCamera()
    {
        if (_previewTracks == null || _previewTracks.Length == 0) return;

        int minNote = 127, maxNote = 0;
        foreach (var t in _previewTracks)
        {
            if (!t.Visible || t.Notes.Length == 0) continue;
            foreach (var (_, _, n) in t.Notes)
            {
                if (n < minNote) minNote = n;
                if (n > maxNote) maxNote = n;
            }
        }

        if (minNote > maxNote) return;

        float midNote = (minNote + maxNote) / 2f;
        _previewState.CameraTopNote = Math.Clamp(midNote + 20f, 20f, 127f);
    }

    private PianoViewport BuildPreviewViewport(float width, float height)
    {
        float noteHeight = Math.Max(_previewState.NoteMinHeight, 4f);
        float pps = _previewState.TimePixelsPerSecond;
        float visibleNotes = height / noteHeight;

        return new PianoViewport
        {
            NoteHeight = noteHeight,
            PixelsPerSecond = pps,
            VisibleNotes = visibleNotes,
            TopNote = _previewState.CameraTopNote,
            StartNote = (int)Math.Floor(_previewState.CameraTopNote - visibleNotes),
            EndNote = (int)Math.Ceiling(_previewState.CameraTopNote),
            StartTime = _previewState.CameraTime,
            EndTime = _previewState.CameraTime + (width / pps),
        };
    }

}
