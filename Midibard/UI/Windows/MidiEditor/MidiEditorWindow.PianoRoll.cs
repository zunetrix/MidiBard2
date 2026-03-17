using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

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
            _previewFile = _file;
            _previewFileVersion = _file?.Version ?? -1;
            if (_file != null)
            {
                _previewTracks = BuildPreviewTracks(_file, out _previewMaxTime);
                _previewTempoMap = _file.TempoMap;
                _previewState.CheckAllTracks = true;
                _previewState.SelectedVoiceLimitItem = 0;
                RefreshPreviewVoiceLimits();
            }
            else
            {
                _previewTracks = null;
                _previewTempoMap = null;
                _previewState.VoiceLimitRegions = new List<(double, double, int)>();
            }
            _previewState.CameraTime = 0;
            if (_previewTracks != null)
                CenterPreviewCamera();
        }

        if (_previewTracks == null)
        {
            ImGui.TextDisabled("No file loaded.");
            return;
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

            float maxListHeight = contentRegion.Y * 0.5f;
            float trackChildHeight = Math.Clamp(_previewTrackListContentHeight, ImGui.GetFrameHeightWithSpacing(), maxListHeight);
            ImGui.BeginChild("##PreviewTrackListArea", new Vector2(0, trackChildHeight), false);
            DrawPreviewTrackList(pianoRollWidth);
            _previewTrackListContentHeight = ImGui.GetCursorPosY();
            ImGui.EndChild();

            ImGui.BeginChild("##PreviewVoiceLimitArea", new Vector2(0, maxListHeight), false);
            DrawPreviewVoiceLimitList(pianoRollWidth);
            ImGui.EndChild();

            ImGui.EndChild();
            ImGui.SameLine();
            pianoRoll.DrawSplitter("##PreviewSplitter", ref _previewLeftPanelWidth, minPanelPx, maxPanelPx);
            ImGui.SameLine();
        }

        if (pianoRollWidth <= 0 || pianoRollHeight <= 0) return;

        ImGui.BeginChild("##PreviewRollArea", new Vector2(contentRegion.X - trackPanelWidth - effectiveSplitter, contentRegion.Y), false);
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();

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

        // InvisibleButton must come before HandlePreviewInput so IsItemActive/IsItemHovered work
        ImGui.SetCursorScreenPos(ctx.CanvasMin);
        ImGui.InvisibleButton("##preview_roll", new Vector2(pianoRollWidth, pianoRollHeight), ImGuiButtonFlags.MouseButtonLeft);
        HandlePreviewInput(ctx);
        ImGui.SetCursorScreenPos(cursor);

        drawList.AddRectFilled(ctx.CanvasMin, ctx.CanvasMax, ImGui.ColorConvertFloat4ToU32(_previewState.GridDarkColor));
        drawList.PushClipRect(ctx.CanvasMin, ctx.CanvasMax, true);
        pianoRoll.DrawNoteGrid(ctx, _previewState);
        pianoRoll.DrawTimeGrid(ctx, _previewTempoMap, _previewState);
        pianoRoll.DrawRangeMarkers(ctx, _previewState);
        pianoRoll.DrawNotes(ctx, _previewTracks, _previewState);
        pianoRoll.DrawVoiceLimitRegions(ctx, _previewState.VoiceLimitRegions);
        drawList.PopClipRect();

        pianoRoll.DrawPianoKeys(ctx);
        ImGui.EndChild();
    }

    private float _previewTrackListContentHeight;

    private void DrawPreviewToolbar()
    {
        // BPM
        if (_file != null)
        {
            var bpm = _file.TempoMap.GetTempoAtTime(new MidiTimeSpan(0)).BeatsPerMinute;
            ImGui.Button($"BPM {bpm:F1}");
            ImGui.SameLine();
        }

        // Beat division
        ImGui.SetNextItemWidth(130 * ImGuiHelpers.GlobalScale);
        var beatDivision = _previewState.BeatDivision;
        ImGuiUtil.EnumCombo("##PreviewBeatDivision", ref beatDivision);
        _previewState.BeatDivision = beatDivision;

        ImGui.SameLine();

        // Time scale
        ImGuiUtil.IconButton(FontAwesomeIcon.ArrowsLeftRight, "##PreviewTimescaleIcon");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80 * ImGuiHelpers.GlobalScale);
        float timePixels = _previewState.TimePixelsPerSecond;
        if (ImGui.DragFloat("##PreviewTimeScale", ref timePixels, 0.5f, 5f, 500f, "%.0f px/s"))
            _previewState.TimePixelsPerSecond = timePixels;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _previewState.TimePixelsPerSecond = 25f;

        ImGui.SameLine();

        // Note scale
        ImGuiUtil.IconButton(FontAwesomeIcon.ArrowsUpDown, "##PreviewNotescaleIcon");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70 * ImGuiHelpers.GlobalScale);
        float noteHeight = _previewState.NoteMinHeight;
        if (ImGui.DragFloat("##PreviewNoteScale", ref noteHeight, 0.2f, 4f, 40f, "%.0f px"))
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
        if (ImGui.Button("Reset##PreviewReset"))
        {
            _previewState.CameraTime = 0;
            _previewState.TimePixelsPerSecond = 25f;
            _previewState.NoteMinHeight = 10f;
            CenterPreviewCamera();
        }

        ImGuiHelpers.ScaledDummy(0, 2);
    }

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

    private static TrackDisplayState[] BuildPreviewTracks(EditableMidiFile file, out double maxTime)
    {
        var tmap = file.TempoMap;
        var result = new List<TrackDisplayState>(file.Tracks.Count);
        maxTime = 0;

        for (int i = 0; i < file.Tracks.Count; i++)
        {
            var track = file.Tracks[i];
            var notes = track.Chunk.GetNotes()
                .Select(n => (
                    n.TimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(),
                    n.EndTimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(),
                    (int)n.NoteNumber))
                .ToArray();

            foreach (var (_, end, _) in notes)
                if (end > maxTime) maxTime = end;

            result.Add(new TrackDisplayState
            {
                TrackInfo = new TrackInfo { Index = i, TrackName = track.Name ?? string.Empty },
                Notes = notes,
                Visible = !track.IsConductorTrack,
            });
        }

        if (maxTime <= 0) maxTime = 10;
        return result.ToArray();
    }

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

    // Must be called immediately after the InvisibleButton so IsItemActive/IsItemHovered refer to it
    private void HandlePreviewInput(PianoRenderContext ctx)
    {
        var io = ImGui.GetIO();

        // Left drag: pan time + note range
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            Vector2 delta = io.MouseDelta;

            _previewState.CameraTime -= delta.X / ctx.View.PixelsPerSecond;
            _previewState.CameraTopNote -= delta.Y / ctx.View.NoteHeight;

            float visibleNotes = ctx.Height / ctx.View.NoteHeight;
            float clampMin = Math.Min(visibleNotes, 127f);
            _previewState.CameraTopNote = Math.Clamp(_previewState.CameraTopNote, clampMin, 127f);
            _previewState.CameraTime = Math.Clamp(_previewState.CameraTime, 0, _previewMaxTime);
        }

        // Scroll: zoom both axes simultaneously
        if (ImGui.IsItemHovered() && io.MouseWheel != 0)
        {
            float zoomFactor = MathF.Pow(1.1f, io.MouseWheel);
            _previewState.NoteMinHeight = Math.Clamp(_previewState.NoteMinHeight * zoomFactor, 4f, 40f);
            _previewState.TimePixelsPerSecond = Math.Clamp(_previewState.TimePixelsPerSecond * zoomFactor, 5f, 500f);
        }
    }
}
