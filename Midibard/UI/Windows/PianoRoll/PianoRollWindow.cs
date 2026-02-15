using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Resources;
using Dalamud.Interface;
using MidiBard.Extensions.Time;

namespace MidiBard;

/// <summary>
/// Piano Roll style MIDI visualizer, showing notes as horizontal bars with time on X-axis and note pitch on Y-axis
/// </summary>
public class PianoRollWindow : Window
{
    private Plugin Plugin { get; }
    private bool setNextLimit;
    private (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] _plotData;

    private static readonly Vector4 BlackKeyColor = new Vector4(0.2f, 0.2f, 0.2f, 0.5f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.8f, 0.8f, 0.8f, 0.3f);

    private static readonly int[] BlackKeys = { 1, 2, 4, 5, 6, 8, 9, 10, 11 }; // C#, D#, F#, G#, A#

    private float _pianoKeyWidth = 30f;
    private float _timePixelsPerSecond = 50f;
    private double _scrollTime = 0;

    public PianoRollWindow(Plugin plugin) : base($"{Language.window_title_visualizor} - Piano Roll###PianoRollVisualizerWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(1000, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;

        UpdateWindowConfig();
    }

    public override void OnOpen()
    {
        base.OnOpen();
    }

    public override void Draw()
    {
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Style.Components.FrameBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Style.Components.FrameBg);

        DrawPianoRoll();

        ImGui.PopStyleColor(2);
    }

    private unsafe void DrawPianoRoll()
    {
        if (IsOpen)
        {
            RefreshPlotData();
        }

        double timelinePos = 0;
        string songName = string.Empty;

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

        ImGui.Text($"Song: {songName} | Time: {timelinePos:F2}s | Scroll: {_scrollTime:F2}s");
        ImGui.SliderFloat("Time/Pixel Scale", ref _timePixelsPerSecond, 10f, 200f);
        ImGui.SliderFloat("Piano Key Width", ref _pianoKeyWidth, 10f, 60f);

        var contentRegion = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();

        float pianoRollX = ImGui.GetCursorScreenPos().X + _pianoKeyWidth;
        float pianoRollY = ImGui.GetCursorScreenPos().Y;
        float pianoRollWidth = contentRegion.X - _pianoKeyWidth - 30;
        float pianoRollHeight = contentRegion.Y;

        // Handle mouse wheel for scrolling
        var io = ImGui.GetIO();
        if (ImGui.IsMouseHoveringRect(new Vector2(pianoRollX, pianoRollY), new Vector2(pianoRollX + pianoRollWidth, pianoRollY + pianoRollHeight)))
        {
            if (io.MouseWheel > 0)
            {
                _scrollTime -= 0.5;
            }
            else if (io.MouseWheel < 0)
            {
                _scrollTime += 0.5;
            }

            if (io.KeyShift && io.MouseWheel != 0)
            {
                _timePixelsPerSecond += io.MouseWheel * 5;
                _timePixelsPerSecond = Math.Max(10, Math.Min(200, _timePixelsPerSecond));
            }
        }

        _scrollTime = Math.Max(0, _scrollTime);

        // Draw piano keys on the left
        DrawPianoKeys(drawList, pianoRollX, pianoRollY, _pianoKeyWidth, pianoRollHeight);

        // Draw grid and notes
        DrawPianoRollArea(drawList, pianoRollX, pianoRollY, pianoRollWidth, pianoRollHeight, timelinePos);

        ImGui.Dummy(contentRegion);
    }

    private void DrawPianoKeys(ImDrawListPtr drawList, float x, float y, float width, float height)
    {
        const int totalNotes = 128;
        float noteHeight = height / totalNotes;

        for (int note = 0; note < totalNotes; note++)
        {
            int octave = note / 12;
            int noteInOctave = note % 12;
            bool isBlackKey = BlackKeys.Contains(noteInOctave);

            float noteY = y + (totalNotes - note - 1) * noteHeight;

            Vector4 keyColor = isBlackKey ? BlackKeyColor : WhiteKeyColor;
            uint keyColorU32 = ImGui.ColorConvertFloat4ToU32(keyColor);

            drawList.AddRectFilled(
                new Vector2(x, noteY),
                new Vector2(x + width, noteY + noteHeight),
                keyColorU32);

            drawList.AddRect(
                new Vector2(x, noteY),
                new Vector2(x + width, noteY + noteHeight),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.8f)));

            // Draw note labels for C notes
            if (noteInOctave == 0)
            {
                drawList.AddText(new Vector2(x + 2, noteY), ImGui.ColorConvertFloat4ToU32(Vector4.One), $"C{octave}");
            }
        }
    }

    private void DrawPianoRollArea(ImDrawListPtr drawList, float x, float y, float width, float height, double timelinePos)
    {
        const int totalNotes = 128;
        float noteHeight = height / totalNotes;

        // Draw background
        drawList.AddRectFilled(
            new Vector2(x, y),
            new Vector2(x + width, y + height),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.8f)));

        // Draw time grid
        float pixelsPerSecond = _timePixelsPerSecond;
        float timeStep = 1f;

        // Adjust time step based on zoom
        if (pixelsPerSecond < 50) timeStep = 5f;
        else if (pixelsPerSecond < 100) timeStep = 2f;

        for (double t = Math.Floor(_scrollTime / timeStep) * timeStep; t < _scrollTime + (width / pixelsPerSecond); t += timeStep)
        {
            float lineX = x + (float)((t - _scrollTime) * pixelsPerSecond);
            drawList.AddLine(
                new Vector2(lineX, y),
                new Vector2(lineX, y + height),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f)));

            // Draw time labels
            drawList.AddText(new Vector2(lineX + 2, y + 2), ImGui.ColorConvertFloat4ToU32(Vector4.One), $"{t:F0}s");
        }

        // Draw note lines
        for (int note = 0; note < totalNotes; note++)
        {
            int noteInOctave = note % 12;
            if (noteInOctave == 0)
            {
                float noteY = y + (totalNotes - note - 1) * noteHeight;
                drawList.AddLine(
                    new Vector2(x, noteY),
                    new Vector2(x + width, noteY),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.3f)), 2f);
            }
        }

        // Draw notes from all tracks
        if (_plotData?.Any() == true && Plugin.CurrentBardPlayback.IsLoaded)
        {
            foreach (var (trackInfo, notes) in _plotData)
            {
                var noteColor = GetTrackColor(trackInfo.Index);
                uint noteColorU32 = ImGui.ColorConvertFloat4ToU32(noteColor);

                foreach (var (start, end, noteNumber) in notes)
                {
                    if (end < _scrollTime || start > _scrollTime + (width / pixelsPerSecond))
                        continue;

                    float noteX = x + (float)((start - _scrollTime) * pixelsPerSecond);
                    float noteWidth = (float)((end - start) * pixelsPerSecond);
                    float noteY = y + (totalNotes - noteNumber - 1) * noteHeight + 1;
                    float noteHeightAdjusted = noteHeight - 2;

                    // Ensure minimum width for visibility
                    if (noteWidth < 2) noteWidth = 2;

                    drawList.AddRectFilled(
                        new Vector2(noteX, noteY),
                        new Vector2(noteX + noteWidth, noteY + noteHeightAdjusted),
                        noteColorU32, 2f);

                    drawList.AddRect(
                        new Vector2(noteX, noteY),
                        new Vector2(noteX + noteWidth, noteY + noteHeightAdjusted),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.5f)), 1f);
                }
            }
        }

        // Draw playback position cursor
        float cursorX = x + (float)((timelinePos - _scrollTime) * pixelsPerSecond);
        if (cursorX >= x && cursorX <= x + width)
        {
            drawList.AddLine(
                new Vector2(cursorX, y),
                new Vector2(cursorX, y + height),
                ImGui.ColorConvertFloat4ToU32(ImGuiColors.DalamudRed), 2f);
        }
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

    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;
    }
}
