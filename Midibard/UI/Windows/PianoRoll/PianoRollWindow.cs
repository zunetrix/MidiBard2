using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;
using Dalamud.Interface;

namespace MidiBard;

public partial class PianoRollWindow : Window
{
    private Plugin Plugin { get; }
    private bool setNextLimit;
    private (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] _plotData;

    private static readonly Vector4 BlackKeyColor = new Vector4(0.15f, 0.2f, 0.25f, 1f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.7f, 0.8f, 0.9f, 1f);

    private Vector4 gridLight = new Vector4(0.26f, 0.33f, 0.37f, 1f); // #42545f
    private Vector4 gridDark = new Vector4(0.25f, 0.32f, 0.36f, 1f); // #41535e
    private Vector4 gridLine = new Vector4(0.12f, 0.19f, 0.23f, 1f); // #1f313c
    private static readonly int[] BlackKeys = { 1, 3, 6, 8, 10 };
    private readonly float _pianoKeyWidth = 80f;
    private float _timePixelsPerSecond = 25f;
    private double _cameraTime = 0;   // visible time on the left side
    private float _cameraTopNote = 127;
    private float _noteMinHeight = 10f; // adjusting piano key / note visual height
    private bool _autoFollowPlayback = true;
    private bool _panMode = true;
    private bool[] _trackVisible;
    private bool _initialCenterDone = false;
    private double timelinePos = 0;
    private bool _showTrackPanel = true;
    private string songName = string.Empty;
    private bool _showC3C6Range = true;
    private bool _showVoiceLimit = true;
    private int _maxVoiceLimit = 16;
    private BeatSubdivision _beatDivision;
    private bool _showSeconds = true;

    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E",
        "F", "F#", "G", "G#", "A", "A#", "B"
    };

    public PianoRollWindow(Plugin plugin) : base($"Piano Roll###PianoRollVisualizerWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(1000, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;

        UpdateWindowConfig();
    }

    public override void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Style.Components.FrameBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Style.Components.FrameBg);
        DrawPianoRoll();
        ImGui.PopStyleColor(2);
    }

    private void DrawMenuBar(string songName)
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Bars, "##ShowHideTrackMenuBtn", "Show/Hide Tracks"))
        {
            _showTrackPanel = !_showTrackPanel;
        }

        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(10, 0);

        ImGui.SameLine();
        ImGui.Text($"Song: {songName}");

        // ImGui.Text("Icon Size:");
        // ImGui.SetNextItemWidth(100);
        // ImGui.SameLine();
        // ImGui.SliderFloat("Time Scale", ref _timePixelsPerSecond, 25f, 200f);
        ImGui.SetNextItemWidth(600);
        ImGui.DragFloat("Time Scale##InputTimeScale", ref _timePixelsPerSecond, 0.1f, 25f, 500f);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetTimeScale", "Reset"))
        {
            _timePixelsPerSecond = 25f;
        }

        // ImGui.SliderFloat("Note Scale", ref _noteMinHeight, 10f, 24f);
        ImGui.SetNextItemWidth(600);
        ImGui.DragFloat("Note Scale##InputNoteScale", ref _noteMinHeight, 0.1f, 10f, 128f);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetNoteScale", "Reset"))
        {
            _noteMinHeight = 10f;
        }

        ImGui.SetNextItemWidth(600);
        ImGui.DragInt("Max Voice Limit##InputMaxVoiceLimit", ref _maxVoiceLimit, 1, 1, 24);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetVoiceLimit", "Reset"))
        {
            _maxVoiceLimit = 16;
        }
        ImGui.SameLine();
        ImGui.Checkbox($"Show Voice Limit Markers", ref _showVoiceLimit);

        ImGui.Text("Beat Division");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        ImGuiUtil.EnumCombo("##BeatDivision", ref _beatDivision);

        ImGui.SameLine();
        ImGui.Checkbox($"Follow Playback", ref _autoFollowPlayback);

        ImGui.SameLine();
        ImGui.Checkbox($"Show Seconds", ref _showSeconds);

        ImGui.SameLine();
        ImGui.Checkbox($"C3-C6 Range", ref _showC3C6Range);

        ImGui.SameLine();
        ImGuiUtil.IconButtonToggle("##HandToolBtn", ref _panMode, FontAwesomeIcon.HandPaper, FontAwesomeIcon.MousePointer, "Hand Tool");
    }

    private void DrawPianoRoll()
    {
        if (IsOpen)
        {
            RefreshPlotData();
        }

        try
        {
            if (Plugin.CurrentBardPlayback.IsLoaded)
            {
                timelinePos = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>().GetTotalSeconds();
            }

            songName = Plugin.PlaylistManager.FilePathList[Plugin.PlaylistManager.CurrentSongIndex].FileName;
        }
        catch
        {
            // ignored
        }

        // top menu
        DrawMenuBar(songName);

        var contentRegion = ImGui.GetContentRegionAvail();

        // left panel
        float trackPanelWidth = _showTrackPanel ? 280f : 0f;

        if (_showTrackPanel)
        {
            ImGui.BeginChild("##pianoroll_tracks", new Vector2(trackPanelWidth, contentRegion.Y), true);
            DrawTrackMenu();
            ImGui.EndChild();
            ImGui.SameLine();
        }

        // piano roll area
        ImGui.BeginChild("##pianoroll_area", new Vector2(contentRegion.X - trackPanelWidth, contentRegion.Y), false);
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();

        float pianoRollX = cursor.X + _pianoKeyWidth;
        float pianoRollY = cursor.Y;
        float pianoRollWidth = ImGui.GetContentRegionAvail().X - _pianoKeyWidth;
        float pianoRollHeight = ImGui.GetContentRegionAvail().Y;

        if (!_initialCenterDone)
        {
            CenterOnNote(60, pianoRollHeight); // C4
            _initialCenterDone = true;
        }

        // must be before viewport build
        FollowPlaybackCursor(pianoRollWidth, _timePixelsPerSecond, timelinePos);

        var view = BuildViewport(pianoRollWidth, pianoRollHeight);
        var pianoRollContext = new PianoRenderContext
        {
            DrawList = drawList,
            X = pianoRollX,
            Y = pianoRollY,
            Width = pianoRollWidth,
            Height = pianoRollHeight,
            CanvasMin = new Vector2(pianoRollX, pianoRollY),
            CanvasMax = new Vector2(pianoRollX + pianoRollWidth, pianoRollY + pianoRollHeight),
            View = view,
            PianoKeyWidth = _pianoKeyWidth,
            PianoKeysX = cursor.X
        };

        DrawPianoRollArea(pianoRollContext, timelinePos);
        DrawPianoKeys(pianoRollContext);

        if (_showVoiceLimit)
        {
            DrawVoiceLimitRegions(pianoRollContext);
        }

        ImGui.EndChild();

        ImGuiHelpers.ScaledDummy(contentRegion.X, 0);
    }

    private void DrawPianoRollArea(PianoRenderContext ctx, double timelinePos)
    {
        ImGui.SetCursorScreenPos(ctx.CanvasMin);
        ImGui.InvisibleButton("##pianoroll_canvas",
            new Vector2(ctx.Width, ctx.Height),
            ImGuiButtonFlags.MouseButtonLeft);

        HandlePianoInput(ctx);

        ctx.DrawList.AddRectFilled(ctx.CanvasMin, ctx.CanvasMax, ImGui.ColorConvertFloat4ToU32(gridDark));
        ctx.DrawList.PushClipRect(ctx.CanvasMin, ctx.CanvasMax, true);

        DrawNoteGrid(ctx);
        DrawTimeGrid(ctx);
        DrawRangeMarkers(ctx);
        DrawNotes(ctx);
        DrawPlaybackCursor(ctx, timelinePos);

        ctx.DrawList.PopClipRect();
    }

    private PianoViewport BuildViewport(float width, float height)
    {
        float noteHeight = Math.Max(_noteMinHeight, 4f);
        float pixelsPerSecond = _timePixelsPerSecond;

        float visibleNotes = height / noteHeight;

        return new PianoViewport
        {
            NoteHeight = noteHeight,
            PixelsPerSecond = pixelsPerSecond,
            VisibleNotes = visibleNotes,

            TopNote = _cameraTopNote,

            StartNote = (int)Math.Floor(_cameraTopNote - visibleNotes),
            EndNote = (int)Math.Ceiling(_cameraTopNote),

            StartTime = _cameraTime,
            EndTime = _cameraTime + (width / pixelsPerSecond)
        };
    }

    private void DrawNoteGrid(PianoRenderContext ctx)
    {
        for (int note = ctx.View.StartNote; note <= ctx.View.EndNote; note++)
        {
            if (note < 0 || note >= 128)
                continue;

            float noteY = ctx.GetNoteTopY(note);

            bool isBlack = BlackKeys.Contains(note % 12);
            Vector4 rowColor = isBlack ? gridDark : gridLight;

            ctx.DrawList.AddRectFilled(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY + ctx.View.NoteHeight),
                ImGui.ColorConvertFloat4ToU32(rowColor));

            ctx.DrawList.AddLine(
                new Vector2(ctx.X, noteY),
                new Vector2(ctx.X + ctx.Width, noteY),
                ImGui.ColorConvertFloat4ToU32(gridLine));
        }
    }

    private void DrawNotes(PianoRenderContext ctx)
    {
        if (_plotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        foreach (var (trackInfo, notes) in _plotData)
        {
            // draw only enabled tracks
            if (_trackVisible != null &&
                trackInfo.Index < _trackVisible.Length &&
                !_trackVisible[trackInfo.Index])
                continue;

            uint noteColorU32 = ImGui.ColorConvertFloat4ToU32(
                GetTrackColor(trackInfo.Index));

            foreach (var (start, end, note) in notes)
            {
                if (!ctx.IsNoteVisible(start, end, note))
                    continue;

                Vector2 min = ctx.NoteRectMin(start, note);
                Vector2 max = ctx.NoteRectMax(end, note);

                if (max.X - min.X < 2f)
                    max.X = min.X + 2f;

                max.Y -= 2f;

                ctx.DrawList.AddRectFilled(min, max, noteColorU32, 2f);

                ctx.DrawList.AddRect(
                    min,
                    max,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.5f)));
            }
        }
    }
}
