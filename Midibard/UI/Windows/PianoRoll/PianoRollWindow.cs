using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;
using MidiBard.Control.MidiControl.Editing;
using Dalamud.Interface.Utility.Raii;
using MidiBard.Playlist;

namespace MidiBard;

public partial class PianoRollWindow : Window
{
    private Plugin Plugin { get; }

    public readonly PianoRollState State = new();

    // Direct bool array for O(1) black-key lookup - faster than HashSet hashing per row
    internal static readonly bool[] IsBlackKey = { false, true, false, true, false, false, true, false, true, false, true, false };

    // Pre-computed RGBA uint constants - avoids repeated ColorConvertFloat4ToU32 of fixed values inside loops
    private static readonly uint BlackKeyColorU32 = ToU32(0.15f, 0.20f, 0.25f, 1f);
    private static readonly uint WhiteKeyColorU32 = ToU32(0.70f, 0.80f, 0.90f, 1f);
    private static readonly uint PianoKeyBorderU32 = ToU32(0f, 0f, 0f, 0.4f);
    private static readonly uint BlackKeyTextU32 = ToU32(1f, 1f, 1f, 1f);
    private static readonly uint WhiteKeyTextU32 = ToU32(0f, 0f, 0f, 1f);
    private static readonly uint BarLineU32 = ToU32(1f, 1f, 1f, 0.25f);
    private static readonly uint SecondLineU32 = ToU32(1f, 1f, 1f, 0.10f);
    private static readonly uint VoiceLimitMarkU32 = ToU32(1f, 0f, 0f, 0.15f);

    // Pre-computed note label strings for all 128 MIDI notes - avoids string allocation per rendered note
    private static readonly string[] NoteLabels = BuildNoteLabels();
    // Text sizes are filled on first render frame (requires active ImGui context)
    private static readonly Vector2[] NoteLabelSizes = new Vector2[128];
    private static bool _noteLabelSizesReady;

    private float _trackListContentHeight = 0f;
    private float _leftPanelWidth = 290f;

    // Cached per loaded MIDI file - invalidated in RefreshPlotData when file changes
    private TempoMap? _cachedTempoMap;
    private int _voiceLimitCacheKey = -1;

    // Reusable list for batching visible note rects per track in DrawNotes
    private readonly List<(Vector2 min, Vector2 max, int displayNote)> _batchNoteRects = new();

    public PianoRollWindow(Plugin plugin) : base($"Piano Roll###PianoRollWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(1000, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        UpdateWindowConfig();
    }

    public override void PreDraw()
    {
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
        using (ImRaii.PushColor(ImGuiCol.TitleBg, Style.Components.FrameBg).Push(ImGuiCol.TitleBgActive, Style.Components.FrameBg))
        {
            DrawMenuBar();
            DrawToolsBar();
            DrawPianoRoll();
        }
    }

    private void DrawPianoRoll()
    {
        if (IsOpen)
        {
            RefreshPlotData();
        }

        if (Plugin.CurrentBardPlayback.IsLoaded)
        {
            State.TimelinePos = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>().GetTotalSeconds();

            State.SongName = Plugin.PlaylistManager.CurrentPlayingSong?.GetFileName() ?? string.Empty;
            WindowName = $"{State.SongName}###PianoRollVisualizerWindow";
        }

        var contentRegion = ImGui.GetContentRegionAvail();

        const float splitterWidth = 5f;
        float minPanelPx = 120f * ImGuiHelpers.GlobalScale;
        float maxPanelPx = MathF.Max(minPanelPx, contentRegion.X - minPanelPx - PianoRollState.PianoKeyWidth);
        _leftPanelWidth = MathF.Max(minPanelPx, MathF.Min(_leftPanelWidth, maxPanelPx));

        float trackPanelWidth = State.ShowLeftPanel ? _leftPanelWidth : 0f;
        float effectiveSplitter = State.ShowLeftPanel ? splitterWidth : 0f;
        float pianoRollWidth = contentRegion.X - trackPanelWidth - effectiveSplitter - PianoRollState.PianoKeyWidth;
        float pianoRollHeight = contentRegion.Y;

        if (State.ShowLeftPanel)
        {
            ImGui.BeginChild("##LeftPanelArea", new Vector2(_leftPanelWidth, contentRegion.Y), true, ImGuiWindowFlags.NoScrollbar);

            float maxListHeight = contentRegion.Y * 0.5f;
            float trackChildHeight = Math.Clamp(_trackListContentHeight, ImGui.GetFrameHeightWithSpacing(), maxListHeight);
            ImGui.BeginChild("##TrackListArea", new Vector2(0, trackChildHeight), false);
            DrawTrackList();
            _trackListContentHeight = ImGui.GetCursorPosY();
            ImGui.EndChild();

            ImGui.BeginChild("##VoiceLimitListArea", new Vector2(0, maxListHeight), false);
            DrawVoiceLimitList(pianoRollWidth);
            ImGui.EndChild();

            ImGui.EndChild();
            ImGui.SameLine();
            DrawSplitter("##PianoRollSplitter", ref _leftPanelWidth, minPanelPx, maxPanelPx);
            ImGui.SameLine();
        }

        // piano roll area
        ImGui.BeginChild("##PianorollArea", new Vector2(contentRegion.X - trackPanelWidth - effectiveSplitter, contentRegion.Y), false);
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();

        float pianoRollX = cursor.X + PianoRollState.PianoKeyWidth;
        float pianoRollY = cursor.Y;

        if (!State.InitialCenterCameraPositionDone)
        {
            CenterViewOnNote(60, pianoRollHeight);
            State.InitialCenterCameraPositionDone = true;
        }

        FollowPlaybackCursor(pianoRollWidth, State.TimePixelsPerSecond, State.TimelinePos);

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
            PianoKeyWidth = PianoRollState.PianoKeyWidth,
            PianoKeysX = cursor.X
        };

        DrawPianoRollArea(pianoRollContext, State.TimelinePos);
        DrawPianoKeys(pianoRollContext);

        if (State.ShowVoiceLimit)
        {
            DrawVoiceLimitRegions(pianoRollContext);
        }

        ImGui.EndChild();
        // ImGuiHelpers.ScaledDummy(contentRegion.X, 0);
    }

    private void DrawPianoRollArea(PianoRenderContext ctx, double timelinePos)
    {
        State.RefreshColorCaches();

        ImGui.SetCursorScreenPos(ctx.CanvasMin);
        ImGui.InvisibleButton("##pianoroll_canvas",
            new Vector2(ctx.Width, ctx.Height),
            ImGuiButtonFlags.MouseButtonLeft);

        HandlePianoInput(ctx);

        ctx.DrawList.AddRectFilled(ctx.CanvasMin, ctx.CanvasMax, State.GridDarkColorU32);
        ctx.DrawList.PushClipRect(ctx.CanvasMin, ctx.CanvasMax, true);

        DrawNoteGrid(ctx, State);

        // Populate tempo map cache lazily (invalidated by RefreshPlotData on file change)
        if (_cachedTempoMap == null && Plugin.CurrentBardPlayback.IsLoaded)
        {
            var midi = Plugin.CurrentBardPlayback.MidiFile;
            if (midi != null) _cachedTempoMap = midi.GetTempoMap();
        }
        DrawTimeGrid(ctx, _cachedTempoMap, State);

        DrawRangeMarkers(ctx, State);
        DrawNotes(ctx, State.Tracks, State);
        DrawPlaybackCursor(ctx, timelinePos);

        ctx.DrawList.PopClipRect();
    }

    // Pure-math equivalent of ImGui.ColorConvertFloat4ToU32 - safe for static field initializers
    private static uint ToU32(float r, float g, float b, float a) =>
        ((uint)(r * 255f + 0.5f)) |
        ((uint)(g * 255f + 0.5f) << 8) |
        ((uint)(b * 255f + 0.5f) << 16) |
        ((uint)(a * 255f + 0.5f) << 24);

    private static string[] BuildNoteLabels()
    {
        var labels = new string[128];
        for (int i = 0; i < 128; i++)
            labels[i] = MidiForgeNotePrimitives.GetMidiNoteName(i);
        return labels;
    }

    // Fill CalcTextSize cache on first render frame - requires active ImGui context
    private void EnsureNoteLabelSizes()
    {
        if (_noteLabelSizesReady) return;
        for (int i = 0; i < 128; i++)
            NoteLabelSizes[i] = ImGui.CalcTextSize(NoteLabels[i]);
        _noteLabelSizesReady = true;
    }

    // Binary search: index of first note with start > maxTime (notes array sorted by start)
    private static int BinarySearchNoteUpper((double start, double end, int note)[] arr, double maxTime)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid].start <= maxTime) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // Binary search: index of first note with start >= minTime (notes array sorted by start)
    // Returns a conservative lower bound that may be slightly before the true first visible note,
    // to account for notes that start before the viewport but extend into it.
    private static int BinarySearchNoteLower((double start, double end, int note)[] arr, double minTime)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid].start < minTime) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
