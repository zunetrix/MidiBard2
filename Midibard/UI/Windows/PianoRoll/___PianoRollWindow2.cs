using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;

namespace MidiBard;

public class PianoRollWindow2 : Window
{
    private Plugin Plugin { get; }

    private float zoomX = 100f;     // pixels per second
    private float zoomY = 12f;      // pixels per note

    public PianoRollWindow2(Plugin plugin)
        : base("Piano Roll###PianoRollWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(800, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw()
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded)
            return;

        DrawPianoRoll();
    }

    private void DrawPianoRoll()
    {
        ImGui.BeginChild("PianoRollCanvas",
    ImGui.GetContentRegionAvail(),
    false,
    ImGuiWindowFlags.HorizontalScrollbar);

        float scrollX = ImGui.GetScrollX();
        float scrollY = ImGui.GetScrollY();

        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos() - new Vector2(scrollX, scrollY);
        var canvasSize = ImGui.GetContentRegionAvail();

        var tempoMap = Plugin.CurrentBardPlayback.TempoMap;
        double currentTime = Plugin.CurrentBardPlayback
            .GetCurrentTime<MetricTimeSpan>()
            .GetTotalSeconds();

        float songLength = 60f; // duration

        // vertical grid tempo
        for (float t = 0; t < songLength; t += 1f)
        {
            float x = canvasPos.X + t * zoomX;

            drawList.AddLine(
                new Vector2(x, canvasPos.Y),
                new Vector2(x, canvasPos.Y + canvasSize.Y),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.05f)));
        }

        for (int note = 0; note < 128; note++)
        {
            float y = canvasPos.Y + (127 - note) * zoomY;

            var color = note % 12 == 0
                ? new Vector4(1, 1, 1, 0.08f) // octave
                : new Vector4(1, 1, 1, 0.03f);

            drawList.AddLine(
                new Vector2(canvasPos.X, y),
                new Vector2(canvasPos.X + canvasSize.X, y),
                ImGui.ColorConvertFloat4ToU32(color));
        }

        foreach (var (trackChunk, index) in Plugin.CurrentBardPlayback.TrackChunks.Select((t, i) => (t, i)))
        {
            var notes = trackChunk.GetNotes();

            foreach (var note in notes)
            {
                double start = note.TimeAs<MetricTimeSpan>(tempoMap).GetTotalSeconds();
                double end = note.EndTimeAs<MetricTimeSpan>(tempoMap).GetTotalSeconds();

                float x1 = canvasPos.X + (float)(start * zoomX);
                float x2 = canvasPos.X + (float)(end * zoomX);
                float y = canvasPos.Y + (127 - note.NoteNumber) * zoomY;

                drawList.AddRectFilled(
                    new Vector2(x1, y),
                    new Vector2(x2, y + zoomY),
                    ImGui.ColorConvertFloat4ToU32(GetTrackColor(index)),
                    2f);
            }
        }

        float timelineX = canvasPos.X + (float)(currentTime * zoomX);

        drawList.AddLine(
            new Vector2(timelineX, canvasPos.Y),
            new Vector2(timelineX, canvasPos.Y + canvasSize.Y),
            ImGui.ColorConvertFloat4ToU32(Style.Colors.Red),
            2f);

        ImGui.EndChild();
    }

    private unsafe Vector4 GetTrackColor(int index)
    {
        Vector4 c = Vector4.One;
        ImGui.ColorConvertHSVtoRGB(
            index / (float)Plugin.CurrentBardPlayback.TrackInfos.Length,
            0.8f, 1,
            &c.X, &c.Y, &c.Z);
        return c;
    }
}
