using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;
using Dalamud.Interface.Utility.Raii;
using MidiBard.Playlist;

namespace MidiBard;

public partial class PianoRollWindow : Window
{
    private Plugin Plugin { get; }

    public readonly PianoRollState State = new();

    private static readonly Vector4 BlackKeyColor = new Vector4(0.15f, 0.2f, 0.25f, 1f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.7f, 0.8f, 0.9f, 1f);

    private static readonly int[] BlackKeys = { 1, 3, 6, 8, 10 };

    private float _trackListContentHeight = 0f;
    private float _leftPanelWidth = 290f;

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
        ImGui.SetCursorScreenPos(ctx.CanvasMin);
        ImGui.InvisibleButton("##pianoroll_canvas",
            new Vector2(ctx.Width, ctx.Height),
            ImGuiButtonFlags.MouseButtonLeft);

        HandlePianoInput(ctx);

        ctx.DrawList.AddRectFilled(ctx.CanvasMin, ctx.CanvasMax, ImGui.ColorConvertFloat4ToU32(State.GridDarkColor));
        ctx.DrawList.PushClipRect(ctx.CanvasMin, ctx.CanvasMax, true);

        DrawNoteGrid(ctx);
        DrawTimeGrid(ctx);
        DrawRangeMarkers(ctx);
        DrawNotes(ctx);
        DrawPlaybackCursor(ctx, timelinePos);

        ctx.DrawList.PopClipRect();
    }
}
