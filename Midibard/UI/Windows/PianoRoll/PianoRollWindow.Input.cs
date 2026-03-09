using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MidiBard;

/// <summary>
/// Handles input operations for the Piano Roll (mouse, keyboard).
/// </summary>
public partial class PianoRollWindow
{
    private void HandlePianoInput(PianoRenderContext ctx)
    {
        var io = ImGui.GetIO();

        if (State.PanMode && ImGui.IsItemActive() &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            State.AutoFollowPlayback = false;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            Vector2 delta = io.MouseDelta;

            State.CameraTime -= delta.X / ctx.View.PixelsPerSecond;
            State.CameraTopNote -= delta.Y / ctx.View.NoteHeight;

            ClampCamera(ctx.Height, ctx.View.NoteHeight);

            if (State.CameraTime < 0)
                State.CameraTime = 0;

            var midiMaxTime = GetMaxScrollTime();
            if (State.CameraTime > midiMaxTime)
                State.CameraTime = midiMaxTime;
        }

        if (ImGui.IsItemHovered() && io.MouseWheel != 0)
        {
            float zoomFactor = MathF.Pow(1.1f, io.MouseWheel);
            State.NoteMinHeight = Math.Clamp(State.NoteMinHeight * zoomFactor, 10f, 40f);
            State.TimePixelsPerSecond = Math.Clamp(State.TimePixelsPerSecond * zoomFactor, 25f, 500f);
        }
    }
}
