using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.General;
using MidiBard.Util.ImGuiExt;
using MidiBard.Extensions.Time;
using System.Linq;

namespace MidiBard;

/// <summary>
/// Handles rendering operations for the Piano Roll.
/// </summary>
public partial class PianoRollWindow
{
    private PianoViewport BuildViewport(float width, float height)
    {
        float noteHeight = Math.Max(State.NoteMinHeight, 4f);
        float pixelsPerSecond = State.TimePixelsPerSecond;
        float visibleNotes = height / noteHeight;

        return new PianoViewport
        {
            NoteHeight = noteHeight,
            PixelsPerSecond = pixelsPerSecond,
            VisibleNotes = visibleNotes,
            TopNote = State.CameraTopNote,
            StartNote = (int)Math.Floor(State.CameraTopNote - visibleNotes),
            EndNote = (int)Math.Ceiling(State.CameraTopNote),
            StartTime = State.CameraTime,
            EndTime = State.CameraTime + (width / pixelsPerSecond)
        };
    }

    private void DrawMenuBar()
    {
        using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
            {
                using (var menu = ImRaii.MenuBar())
                {
                    if (ImGui.BeginMenu("Menu"))
                    {
                        bool panMode = State.PanMode;
                        if (ImGuiUtil.IconButtonToggle("##HandToolBtn", ref panMode, FontAwesomeIcon.HandPaper, FontAwesomeIcon.MousePointer, "Hand Tool"))
                            State.PanMode = panMode;
                        ImGui.EndMenu();
                    }

                    DrawViewMenu();
                    DrawOptionsMenu();
                }
            }
        }
    }

    private void DrawViewMenu()
    {
        using (var menu = ImRaii.Menu("View"))
        {
            if (!menu) return;

            bool showLeftPanel = State.ShowLeftPanel;
            if (ImGui.Checkbox($"Left Panel", ref showLeftPanel))
                State.ShowLeftPanel = showLeftPanel;

            bool showNoteLabel = State.ShowNoteLabel;
            if (ImGui.Checkbox($"Note Label", ref showNoteLabel))
                State.ShowNoteLabel = showNoteLabel;

            bool showNoteBorder = State.ShowNoteBorder;
            if (ImGui.Checkbox($"Note Border", ref showNoteBorder))
                State.ShowNoteBorder = showNoteBorder;

            bool showSeconds = State.ShowSeconds;
            if (ImGui.Checkbox($"Time Markers", ref showSeconds))
                State.ShowSeconds = showSeconds;

            bool showC3C6Range = State.ShowC3C6Range;
            if (ImGui.Checkbox($"C3-C6 Markers", ref showC3C6Range))
                State.ShowC3C6Range = showC3C6Range;

            bool showVoiceLimit = State.ShowVoiceLimit;
            if (ImGui.Checkbox($"Voice Limit Markers", ref showVoiceLimit))
                State.ShowVoiceLimit = showVoiceLimit;
        }
    }

    private void DrawOptionsMenu()
    {
        using (var menu = ImRaii.Menu("Options"))
        {
            if (!menu) return;

            bool autoFollow = State.AutoFollowPlayback;
            if (ImGui.Checkbox($"Follow Playback", ref autoFollow))
                State.AutoFollowPlayback = autoFollow;


            bool showAdaptedNotes = State.Tracks?.All(t => t.ShowAdaptedNotes) == true;
            if (ImGui.Checkbox($"Show Adapted Notes", ref showAdaptedNotes))
            {
                if (State.Tracks != null)
                    foreach (var t in State.Tracks) t.ShowAdaptedNotes = showAdaptedNotes;
            }

            ImGui.Separator();

            bool groupRegions = State.GroupVoiceLimitRegions;
            if (ImGui.Checkbox($"Group Voice Limit Regions", ref groupRegions))
                State.GroupVoiceLimitRegions = groupRegions;
            ImGuiUtil.ToolTip("Group voice limit markers to max 1 per second");

            ImGui.Text("Max Voice Limit:");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            int maxVoiceLimit = State.MaxVoiceLimit;
            if (ImGui.InputInt("##InputMaxVoiceLimit", ref maxVoiceLimit, 1, 1, flags: ImGuiInputTextFlags.AutoSelectAll))
            {
                State.MaxVoiceLimit = maxVoiceLimit.Clamp(1, 30);
            }
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetVoiceLimit", "Reset"))
            {
                State.MaxVoiceLimit = 16;
            }

        }
    }

    private void DrawCameraTimelineSlider()
    {
        double maxScrollTime = GetMaxScrollTime();
        float cameraProgress = 0f;

        if (maxScrollTime > 0)
        {
            cameraProgress = (float)(State.CameraTime / maxScrollTime);
        }

        string timeLabel = State.CameraTime.FormatSecondsToTime();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Timeline##CameraTimelineSlider", ref cameraProgress, 0f, 1f, timeLabel))
        {
            State.CameraTime = cameraProgress * maxScrollTime;
            State.AutoFollowPlayback = false;
        }
    }

    private void DrawTimelineSlider()
    {
        double maxScrollTime = GetMaxScrollTime();
        float timelineProgress = 0f;

        if (maxScrollTime > 0)
        {
            timelineProgress = (float)(State.TimelinePos / maxScrollTime);
        }

        string timeLabel = State.TimelinePos.FormatSecondsToTime();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Playback##PlaybackTimelineSlider", ref timelineProgress, 0f, 1f, timeLabel))
        {
            var newTime = timelineProgress * maxScrollTime;
            // Move camera to follow playback
            State.CameraTime = newTime;
            State.AutoFollowPlayback = false;
        }
    }

    private void DrawNoteScaleSlider()
    {
        // Note scale slider
        ImGuiUtil.IconButton(FontAwesomeIcon.ArrowsUpDown, "##TimescaleIconBtn");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        float noteHeight = State.NoteMinHeight;
        ImGui.DragFloat("Note Scale##InputNoteScale", ref noteHeight, 0.1f, 10f, 40f);
        State.NoteMinHeight = noteHeight;
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetNoteScale", "Reset"))
        {
            State.NoteMinHeight = 10f;
        }
    }

    private void DrawTimeScaleSlider()
    {
        // Time scale slider
        ImGuiUtil.IconButton(FontAwesomeIcon.ArrowsLeftRight, "##TimescaleIconBtn");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        float timePixels = State.TimePixelsPerSecond;
        ImGui.DragFloat("Time Scale##InputTimeScale", ref timePixels, 0.1f, 25f, 500f);
        State.TimePixelsPerSecond = timePixels;
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetTimeScale", "Reset"))
        {
            State.TimePixelsPerSecond = 25f;
        }
    }

    private void DrawBPM()
    {
        var bpm = Plugin.CurrentBardPlayback?.GetBpm();
        ImGui.Button($"BPM {bpm:F1}");
    }

    private void DrawToolsBar()
    {
        DrawBPM();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        var beatDivision = State.BeatDivision;
        ImGuiUtil.EnumCombo("##BeatDivision", ref beatDivision);
        State.BeatDivision = beatDivision;

        ImGui.SameLine();
        DrawTimeScaleSlider();

        ImGui.SameLine();
        DrawNoteScaleSlider();

        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(10, 0);

        ImGui.SameLine();
        DrawTimelineSlider();

        ImGui.SameLine();
        DrawCameraTimelineSlider();

        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawTrackList()
    {
        if (State.Tracks == null) return;

        if (ImGui.CollapsingHeader($"Tracks##TrackListCollapsing"))
        {
            bool checkAll = State.CheckAllTracks;
            if (ImGui.Checkbox($"##CheckAllTracks", ref checkAll))
            {
                State.CheckAllTracks = checkAll;
                foreach (var t in State.Tracks) t.Visible = checkAll;
                UpdateVoiceLimitRegions();
            }

            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
            ImGui.Text("Tracks");

            foreach (var track in State.Tracks)
            {
                var tinfo = track.TrackInfo;
                bool visible = track.Visible;
                var color = track.Color ?? GetTrackColor(tinfo.Index, State.Tracks.Length);

                if (ImGuiUtil.IconButton(FontAwesomeIcon.SlidersH, $"##trackOpts{tinfo.Index}", tooltip: null))
                    ImGui.OpenPopup($"##trackOptions{tinfo.Index}");

                if (ImGui.BeginPopup($"##trackOptions{tinfo.Index}"))
                {
                    bool adapted = track.ShowAdaptedNotes;
                    if (ImGui.Checkbox($"Show Adapted Notes##AdaptedNoteTrack_{tinfo.Index}", ref adapted))
                        track.ShowAdaptedNotes = adapted;
                    ImGui.EndPopup();
                }

                ImGui.SameLine();
                if (ImGui.ColorButton($"##col{tinfo.Index}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16)))
                    ImGui.OpenPopup($"##trackColorPicker{tinfo.Index}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                if (ImGui.BeginPopup($"##trackColorPicker{tinfo.Index}"))
                {
                    var pickerColor = track.Color ?? GetTrackColor(tinfo.Index, State.Tracks.Length);
                    if (ImGui.ColorPicker4($"##picker{tinfo.Index}", ref pickerColor, ImGuiColorEditFlags.AlphaBar))
                        track.Color = pickerColor;
                    if (track.Color.HasValue && ImGui.Button("Reset"))
                        track.Color = null;
                    ImGui.EndPopup();
                }

                ImGui.SameLine();
                if (ImGui.Checkbox($"[{tinfo.Index + 1:00}] {tinfo.TrackName}", ref visible))
                {
                    track.Visible = visible;
                    UpdateVoiceLimitRegions();
                }
            }
        }
    }

    private void DrawVoiceLimitList(float pianoRollWidth)
    {
        if (State.Tracks?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        var voiceLimitRegions = State.VoiceLimitRegions;

        if (ImGui.CollapsingHeader($"Voice Limit List ({voiceLimitRegions.Count})##VoiceLimitList"))
        {
            for (int i = 0; i < voiceLimitRegions.Count; i++)
            {
                var voiceLimitRegion = voiceLimitRegions[i];
                bool isSelected = State.SelectedVoiceLimitItem == i;

                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered);
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered);
                    ImGui.PushStyleColor(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered);
                }

                string label = $"{i + 1:000} - {voiceLimitRegion.start.GetDurationString()} ({voiceLimitRegion.noteCount})##voiceLimit_{i}";
                if (ImGui.Selectable(label, isSelected))
                {
                    State.SelectedVoiceLimitItem = i;
                    CenterViewOnTime(voiceLimitRegion.start, pianoRollWidth);
                }

                if (isSelected)
                    ImGui.PopStyleColor(3);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }
}
