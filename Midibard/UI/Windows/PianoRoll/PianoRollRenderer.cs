using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.General;
using MidiBard.Util.ImGuiExt;
using MidiBard.Extensions.Time;

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

            // bool showAdaptedNotes = State.ShowAdaptedNotes;
            // if (ImGui.Checkbox($"Use Autoadapted Notes", ref showAdaptedNotes))
            //     State.ShowAdaptedNotes = showAdaptedNotes;

            ImGuiGroupPanel.BeginGroupPanel("Voice Limit");
            {
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
            ImGuiGroupPanel.EndGroupPanel();
        }
    }

    private void DrawTimelineSlider()
    {
        double maxScrollTime = GetMaxScrollTime();
        float timelineProgress = 0f;

        if (maxScrollTime > 0)
        {
            timelineProgress = (float)(State.CameraTime / maxScrollTime);
        }

        string timeLabel = FormatTime(State.CameraTime);
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Timeline##TimelineSlider", ref timelineProgress, 0f, 1f, timeLabel))
        {
            State.CameraTime = timelineProgress * maxScrollTime;
            State.AutoFollowPlayback = false;
        }
    }

    private static string FormatTime(double seconds)
    {
        int totalSeconds = (int)seconds;
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        return $"{minutes}:{secs:00}";
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

    private void DrawToolsArea()
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

        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawTrackList()
    {
        if (State.PlotData == null) return;

        InitTrackList();

        if (ImGui.CollapsingHeader($"Tracks##TrackListCollapsing"))
        {
            bool checkAll = State.CheckAllTracks;
            if (ImGui.Checkbox($"##CheckAllTracks", ref checkAll))
            {
                State.CheckAllTracks = checkAll;
                if (State.TrackVisible == null || State.TrackVisible.Length == 0) return;
                for (int i = 0; i < State.TrackVisible.Length; i++) State.TrackVisible[i] = checkAll;
                UpdateVoiceLimitRegions();
            }

            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
            ImGui.Text("Tracks");

            for (int i = 0; i < State.PlotData.Length; i++)
            {
                var tinfo = State.PlotData[i].trackInfo;
                bool visible = (tinfo.Index < State.TrackVisible.Length) ? State.TrackVisible[tinfo.Index] : true;
                var color = GetTrackColor(tinfo.Index);
                ImGui.ColorButton($"##col{tinfo.Index}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16));
                ImGui.SameLine();
                if (ImGui.Checkbox($"[{tinfo.Index + 1:00}] {tinfo.TrackName}", ref visible))
                {
                    if (tinfo.Index < State.TrackVisible.Length) State.TrackVisible[tinfo.Index] = visible;
                    UpdateVoiceLimitRegions();
                }
            }
        }
    }

    private void DrawVoiceLimitList(float pianoRollWidth)
    {
        if (State.PlotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
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
