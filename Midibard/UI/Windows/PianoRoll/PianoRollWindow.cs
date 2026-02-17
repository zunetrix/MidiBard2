using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;
using Dalamud.Interface.Utility.Raii;
using System.Collections.Generic;

namespace MidiBard;

public partial class PianoRollWindow : Window
{
    private Plugin Plugin { get; }

    // State object - contains all mutable state
    public readonly PianoRollState State = new();

    // Static constants (colors, etc)
    private static readonly Vector4 BlackKeyColor = new Vector4(0.15f, 0.2f, 0.25f, 1f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.7f, 0.8f, 0.9f, 1f);

    private Vector4 gridLight = new Vector4(0.26f, 0.33f, 0.37f, 1f); // #42545f
    private Vector4 gridDark = new Vector4(0.25f, 0.32f, 0.36f, 1f); // #41535e
    private Vector4 gridLine = new Vector4(0.12f, 0.19f, 0.23f, 1f); // #1f313c
    private static readonly int[] BlackKeys = { 1, 3, 6, 8, 10 };

    public PianoRollWindow(Plugin plugin) : base($"Piano Roll###PianoRollVisualizerWindow")
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
                State.TimelinePos = Plugin.CurrentBardPlayback.GetCurrentTime<MetricTimeSpan>().GetTotalSeconds();
            }

            State.SongName = Plugin.PlaylistManager.FilePathList[Plugin.PlaylistManager.CurrentSongIndex].FileName;
        }
        catch
        {
            // ignored
        }

        var contentRegion = ImGui.GetContentRegionAvail();

        float trackPanelWidth = State.ShowLeftPanel ? 280f : 0f;
        float pianoRollWidth = contentRegion.X - trackPanelWidth - PianoRollState.PianoKeyWidth;
        float pianoRollHeight = contentRegion.Y;

        if (State.ShowLeftPanel)
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
}
