using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;
using Dalamud.Interface;
using MidiBard.Extensions.General;
using Dalamud.Interface.Utility.Raii;
using System.Collections.Generic;

namespace MidiBard;

public partial class PianoRollWindow : Window
{
    private Plugin Plugin { get; }
    private (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] _plotData;

    private static readonly Vector4 BlackKeyColor = new Vector4(0.15f, 0.2f, 0.25f, 1f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.7f, 0.8f, 0.9f, 1f);

    private Vector4 gridLight = new Vector4(0.26f, 0.33f, 0.37f, 1f); // #42545f
    private Vector4 gridDark = new Vector4(0.25f, 0.32f, 0.36f, 1f); // #41535e
    private Vector4 gridLine = new Vector4(0.12f, 0.19f, 0.23f, 1f); // #1f313c
    private static readonly int[] BlackKeys = { 1, 3, 6, 8, 10 };
    private readonly float _pianoKeyWidth = 80f;

    private double _cameraTime = 0;   // visible time on the left side
    private float _timePixelsPerSecond = 25f;
    private float _cameraTopNote = 127;
    private float _noteMinHeight = 10f; // adjusting piano key / note visual height
    private bool _autoFollowPlayback = true;
    private bool _panMode = true;
    private bool[] _trackVisible;
    private bool _initialCenterCameraPositionDone = false;
    private double _timelinePos = 0;
    private bool _showLeftPanel = true;

    private int _selectedVoiceLimitItem = 0;
    private List<(double start, double end, int noteCount)> _voiceLimitRegions = new List<(double start, double end, int noteCount)>();
    private bool _checlAllTracks = true;

    private string _lastLoadedFilePath;
    private string songName = string.Empty;

    private bool _showNoteLabel = false;
    private bool _showNoteBorder = true;
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

    public override void PreDraw()
    {
        // Flags = ImGuiWindowFlags.None;
        Flags = ImGuiWindowFlags.MenuBar;
        if (!Plugin.Config.AllowMovement)
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }

        if (!Plugin.Config.AllowResize)
        {
            Flags |= ImGuiWindowFlags.NoResize;
        }

        base.PreDraw();
    }

    public override void Draw()
    {
        using (ImRaii.PushColor(ImGuiCol.TitleBg, Style.Components.FrameBg))
        {
            using (ImRaii.PushColor(ImGuiCol.TitleBgActive, Style.Components.FrameBg))
            {
                DrawMenuBar();
                DrawToolsArea();
                DrawPianoRoll();
            }
        }
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
                        ImGuiUtil.IconButtonToggle("##HandToolBtn", ref _panMode, FontAwesomeIcon.HandPaper, FontAwesomeIcon.MousePointer, "Hand Tool");
                        ImGui.EndMenu();
                    }

                    DrawViewMenu();

                    // if (ImGui.MenuItem("Menu Item"))
                    // {
                    //     //
                    // }
                }
            }
        }
    }

    private void DrawViewMenu()
    {
        using (var menu = ImRaii.Menu("View"))
        {
            if (!menu) return;
            ImGui.Checkbox($"Left Panel", ref _showLeftPanel);

            ImGui.Checkbox($"Note Label", ref _showNoteLabel);

            ImGui.Checkbox($"Note Border", ref _showNoteBorder);

            ImGui.Checkbox($"Time Markers", ref _showSeconds);

            ImGui.Checkbox($"C3-C6 Markers", ref _showC3C6Range);

            ImGui.Checkbox($"Follow Playback", ref _autoFollowPlayback);

            ImGui.Checkbox($"Voice Limit Markers", ref _showVoiceLimit);

            ImGui.Text("Voice Limit");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##InputMaxVoiceLimit", ref _maxVoiceLimit, 1, 1, flags: ImGuiInputTextFlags.AutoSelectAll))
            {
                _maxVoiceLimit = _maxVoiceLimit.Clamp(1, 30);
            }
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetVoiceLimit", "Reset"))
            {
                _maxVoiceLimit = 16;
            }

            ImGui.Text("Grid");
            ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
            ImGuiUtil.EnumCombo("##BeatDivision", ref _beatDivision);
        }
    }

    private void DrawToolsArea()
    {
        ImGui.Text($"Song: {songName}");

        // ImGui.Text("Icon Size:");
        // ImGui.SetNextItemWidth(100);
        // ImGui.SameLine();
        // ImGui.SliderFloat("Time Scale", ref _timePixelsPerSecond, 25f, 200f);
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.DragFloat("Time Scale##InputTimeScale", ref _timePixelsPerSecond, 0.1f, 25f, 500f);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetTimeScale", "Reset"))
        {
            _timePixelsPerSecond = 25f;
        }

        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(10, 0);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.DragFloat("Note Scale##InputNoteScale", ref _noteMinHeight, 0.1f, 10f, 40f);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetNoteScale", "Reset"))
        {
            _noteMinHeight = 10f;
        }

        ImGuiHelpers.ScaledDummy(0, 5);
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
                _timelinePos = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>().GetTotalSeconds();
            }

            songName = Plugin.PlaylistManager.FilePathList[Plugin.PlaylistManager.CurrentSongIndex].FileName;
        }
        catch
        {
            // ignored
        }

        var contentRegion = ImGui.GetContentRegionAvail();

        // left panel
        float trackPanelWidth = _showLeftPanel ? 280f : 0f;

        // piano roll area dimensions (calculate before left panel to pass to voice limit list)
        float pianoRollWidth = contentRegion.X - trackPanelWidth - _pianoKeyWidth;
        float pianoRollHeight = contentRegion.Y;

        if (_showLeftPanel)
        {
            ImGui.BeginChild("##LeftPanelRegion", new Vector2(trackPanelWidth, contentRegion.Y), true, ImGuiWindowFlags.HorizontalScrollbar);
            DrawTrackList();

            DrawVoiceLimitList(pianoRollWidth);

            ImGui.EndChild();
            ImGui.SameLine();
        }

        // piano roll area
        ImGui.BeginChild("##pianoroll_area", new Vector2(contentRegion.X - trackPanelWidth, contentRegion.Y), false);
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();

        float pianoRollX = cursor.X + _pianoKeyWidth;
        float pianoRollY = cursor.Y;
        // float pianoRollWidth = ImGui.GetContentRegionAvail().X - _pianoKeyWidth;
        // float pianoRollHeight = ImGui.GetContentRegionAvail().Y;

        if (!_initialCenterCameraPositionDone)
        {
            CenterViewOnNote(60, pianoRollHeight); // C4
            _initialCenterCameraPositionDone = true;
        }

        // must be before viewport build
        FollowPlaybackCursor(pianoRollWidth, _timePixelsPerSecond, _timelinePos);

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

        DrawPianoRollArea(pianoRollContext, _timelinePos);
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

            uint noteColorU32 = ImGui.ColorConvertFloat4ToU32(GetTrackColor(trackInfo.Index));

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

                if (_showNoteBorder)
                {
                    ctx.DrawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(Style.Colors.Black), rounding: 2f, thickness: 1f);
                }

                // note label
                if (ctx.View.NoteHeight > 15f && _showNoteLabel) // zoom size
                {
                    uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 1f));
                    ctx.DrawList.AddText(new Vector2(min.X, min.Y), textColor, GetPianoKeyLabel(note));
                }
            }
        }
    }
}
