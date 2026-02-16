using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Resources;
using MidiBard.Extensions.Time;
using Dalamud.Interface;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

public partial class PianoRollWindow : Window
{
    private Plugin Plugin { get; }
    private bool setNextLimit;
    private (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] _plotData;

    private static readonly Vector4 BlackKeyColor = new Vector4(0.15f, 0.2f, 0.25f, 1f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.7f, 0.8f, 0.9f, 1f);

    private Vector4 gridLight = new Vector4(0x42 / 255f, 0x54 / 255f, 0x5f / 255f, 1f); // #42545f
    private Vector4 gridDark = new Vector4(0x41 / 255f, 0x53 / 255f, 0x5e / 255f, 1f); // #41535e
    private Vector4 gridLine = new Vector4(0x1f / 255f, 0x31 / 255f, 0x3c / 255f, 1f); // #1f313c

    private static readonly int[] BlackKeys = { 1, 3, 6, 8, 10 };

    private readonly float _pianoKeyWidth = 80f;
    private float _timePixelsPerSecond = 25f;
    private double _cameraTime = 0;   // visible time on the left side
    private float _cameraTopNote = 127;
    private float _noteMinHeight = 10f; // allow adjusting piano key / note visual height

    private bool _autoFollowPlayback = true;
    private bool _panMode = false;

    private bool[] _trackVisible;
    private bool _showTrackPanel = true;
    private bool _showC3C6Range = false;
    private string songName = string.Empty;

    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E",
        "F", "F#", "G", "G#", "A", "A#", "B"
    };

    public PianoRollWindow(Plugin plugin) : base($"{Language.window_title_visualizor} - Piano Roll###PianoRollVisualizerWindow")
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
        ImGui.Text($"Song: {songName}");
        ImGui.SliderFloat("Time Scale", ref _timePixelsPerSecond, 25f, 200f);
        ImGui.SliderFloat("Note Scale", ref _noteMinHeight, 10f, 24f);

        if (ImGui.Button(_showTrackPanel ? "Hide Tracks" : "Show Tracks"))
            _showTrackPanel = !_showTrackPanel;

        ImGui.SameLine();
        ImGui.Checkbox($"Follow Playback", ref _autoFollowPlayback);

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

        double timelinePos = 0;
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

        // Top menu bar (title, sliders, toggle)
        DrawMenuBar(songName);

        var contentRegion = ImGui.GetContentRegionAvail();

        // left track panel width
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

        ImGui.EndChild();

        ImGui.Dummy(new Vector2(contentRegion.X, 0));
    }
    private void FollowPlaybackCursor(float width, float pixelsPerSecond, double timelinePos)
    {
        if (_autoFollowPlayback)
        {
            double visibleTime = width / pixelsPerSecond;
            _cameraTime = timelinePos - visibleTime * 0.3; // offset cursor left

            if (_cameraTime < 0)
                _cameraTime = 0;
        }
    }

    private void DrawPlaybackCursor(PianoRenderContext ctx, double timelinePos)
    {
        float cursorX = ctx.X + (float)((timelinePos - _cameraTime) * ctx.View.PixelsPerSecond);

        if (cursorX >= ctx.X && cursorX <= ctx.X + ctx.Width)
        {
            ctx.DrawList.AddLine(
                new Vector2(cursorX, ctx.Y),
                new Vector2(cursorX, ctx.Y + ctx.Height),
                ImGui.ColorConvertFloat4ToU32(Style.Colors.Red), 2f);
        }
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

    public void RefreshPlotData()
    {
        Task.Run(() =>
        {
            try
            {
                if (Plugin.CurrentBardPlayback?.TrackInfos == null)
                {
                    DalamudApi.PluginLog.Debug("try RefreshPlotData but CurrentTracks is null");
                    return;
                }

                var tmap = Plugin.CurrentBardPlayback.TempoMap;

                _plotData = Plugin.CurrentBardPlayback.TrackChunks.Select((trackChunk, index) =>
                    {
                        var trackNotes = trackChunk.GetNotes()
                            .Select(j => (j.TimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(),
                                j.EndTimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(), (int)j.NoteNumber))
                            .ToArray();

                        return (Plugin.CurrentBardPlayback.TrackInfos[index], notes: trackNotes);
                    })
                    .ToArray();

                setNextLimit = true;
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error when refreshing piano roll plot data");
            }
        });
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

    private void DrawTimeGrid_IN_SECONDS(PianoRenderContext ctx)
    {
        float timeStep = ctx.View.PixelsPerSecond < 60 ? 5f :
                         ctx.View.PixelsPerSecond < 120 ? 2f : 1f;

        for (double t = Math.Floor(ctx.View.StartTime / timeStep) * timeStep;
             t < ctx.View.EndTime;
             t += timeStep)
        {
            float lineX = ctx.X + (float)((t - ctx.View.StartTime) * ctx.View.PixelsPerSecond);

            bool isMajor = ((int)t % 4) == 0;

            uint color = ImGui.ColorConvertFloat4ToU32(
                isMajor ? gridLine * 1.2f : gridLine);

            ctx.DrawList.AddLine(
                new Vector2(lineX, ctx.Y),
                new Vector2(lineX, ctx.Y + ctx.Height),
                color,
                isMajor ? 2f : 1f);

            // time label
            if (isMajor)
            {
                ctx.DrawList.AddText(
                    new Vector2(lineX + 4, ctx.Y + 4),
                    ImGui.ColorConvertFloat4ToU32(Vector4.One),
                    $"{t:F0}s");
            }
        }
    }

    private void DrawTimeGrid(PianoRenderContext ctx)
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded)
            return;

        var tempoMap = Plugin.CurrentBardPlayback.TempoMap;

        long startTicks = TimeConverter.ConvertFrom(
            ctx.View.StartTime.ToMetricTimeSpan(),
            tempoMap);

        long endTicks = TimeConverter.ConvertFrom(
            ctx.View.EndTime.ToMetricTimeSpan(),
            tempoMap);

        var startBarBeat = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(startTicks, tempoMap);
        var endBarBeat = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(endTicks, tempoMap);

        int startBar = (int)startBarBeat.Bars;
        int endBar = (int)endBarBeat.Bars + 1;

        for (int bar = startBar; bar <= endBar; bar++)
        {
            // measure start
            var barTime = new BarBeatTicksTimeSpan(bar, 0);
            long barTicks = TimeConverter.ConvertFrom(barTime, tempoMap);

            var barMetric = TimeConverter.ConvertTo<MetricTimeSpan>(barTicks, tempoMap);

            double seconds = barMetric.TotalMicroseconds / 1_000_000.0;

            if (seconds < ctx.View.StartTime || seconds > ctx.View.EndTime)
                continue;

            float x = ctx.X + (float)((seconds - ctx.View.StartTime) * ctx.View.PixelsPerSecond);

            // measure line
            ctx.DrawList.AddLine(
                new Vector2(x, ctx.Y),
                new Vector2(x, ctx.Y + ctx.Height),
                ImGui.ColorConvertFloat4ToU32(Vector4.One),
                2f);

            // beats grid
            var barTimeSpan = new MidiTimeSpan(barTicks);
            var timeSignature = tempoMap.GetTimeSignatureAtTime(barTimeSpan);

            for (int beat = 1; beat < timeSignature.Numerator; beat++)
            {
                var beatTime = new BarBeatTicksTimeSpan(bar, beat);
                long beatTicks = TimeConverter.ConvertFrom(beatTime, tempoMap);

                var beatMetric = TimeConverter.ConvertTo<MetricTimeSpan>(beatTicks, tempoMap);
                double beatSeconds = beatMetric.TotalMicroseconds / 1_000_000.0;

                if (beatSeconds < ctx.View.StartTime || beatSeconds > ctx.View.EndTime)
                    continue;

                float beatX = ctx.X + (float)((beatSeconds - ctx.View.StartTime) * ctx.View.PixelsPerSecond);

                ctx.DrawList.AddLine(
                    new Vector2(beatX, ctx.Y),
                    new Vector2(beatX, ctx.Y + ctx.Height),
                    ImGui.ColorConvertFloat4ToU32(gridLine),
                    1f);
            }

            // Label
            ctx.DrawList.AddText(
                new Vector2(x + 4, ctx.Y + 4),
                ImGui.ColorConvertFloat4ToU32(Vector4.One),
                $"Bar {bar + 1}");
        }
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

    private void ClampCamera(float height, float noteHeight)
    {
        float visibleNotes = height / noteHeight;

        float minTop = visibleNotes;
        float maxTop = 127;

        _cameraTopNote = Math.Clamp(_cameraTopNote, minTop, maxTop);
    }

    private void DrawNotes(PianoRenderContext ctx)
    {
        if (_plotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        foreach (var (trackInfo, notes) in _plotData)
        {
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

    private void HandlePianoInput(PianoRenderContext ctx)
    {
        var io = ImGui.GetIO();

        // pan move
        if (_panMode && ImGui.IsItemActive() &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _autoFollowPlayback = false;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            Vector2 delta = io.MouseDelta;

            _cameraTime -= delta.X / ctx.View.PixelsPerSecond;
            _cameraTopNote -= delta.Y / ctx.View.NoteHeight;

            ClampCamera(ctx.Height, ctx.View.NoteHeight);

            if (_cameraTime < 0)
                _cameraTime = 0;
        }

        // zoom
        if (ImGui.IsItemHovered() && io.MouseWheel != 0)
        {
            if (io.KeyCtrl)
            {
                _noteMinHeight = Math.Clamp(
                    _noteMinHeight + io.MouseWheel * 2f,
                    4f, 40f);
            }
            else
            {
                _timePixelsPerSecond = Math.Clamp(
                    _timePixelsPerSecond + io.MouseWheel * 10f,
                    20f, 400f);
            }
        }
    }

    private void DrawRangeMarkers(PianoRenderContext ctx)
    {
        if (!_showC3C6Range)
            return;

        const int C3 = 48;
        const int C6 = 84;

        DrawHorizontalMarker(ctx, C3, alignBottom: true);

        DrawHorizontalMarker(ctx, C6, alignBottom: false);
    }

    private void DrawHorizontalMarker(PianoRenderContext ctx, int note, bool alignBottom)
    {
        float noteY = alignBottom
            ? ctx.GetNoteBottomY(note)
            : ctx.GetNoteTopY(note);

        if (noteY < ctx.Y || noteY > ctx.Y + ctx.Height)
            return;

        ctx.DrawList.AddLine(
            new Vector2(ctx.X, noteY),
            new Vector2(ctx.X + ctx.Width, noteY),
            ImGui.ColorConvertFloat4ToU32(Style.Colors.Yellow),
            2f);
    }
}
