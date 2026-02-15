using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Resources;
using MidiBard.Extensions.Time;

namespace MidiBard;

public class PianoRollWindow : Window
{
    private Plugin Plugin { get; }
    private bool setNextLimit;
    private (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] _plotData;

    private static readonly Vector4 BlackKeyColor = new Vector4(0.2f, 0.2f, 0.2f, 0.5f);
    private static readonly Vector4 WhiteKeyColor = new Vector4(0.8f, 0.8f, 0.8f, 0.3f);

    private static readonly int[] BlackKeys = { 1, 2, 4, 5, 6, 8, 9, 10, 11 }; // C#, D#, F#, G#, A#

    private readonly float _pianoKeyWidth = 60f;
    private float _timePixelsPerSecond = 50f;
    private double _scrollTime = 0;
    private float _noteMinHeight = 2f; // allow adjusting piano key / note visual height

    private bool[] _trackVisible;
    private bool _showTrackPanel = true;

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
        ImGui.Text($"Song: {songName} | Time: {_scrollTime:F2}s");
        ImGui.SliderFloat("Time Scale", ref _timePixelsPerSecond, 10f, 200f);
        ImGui.SliderFloat("Note Scale", ref _noteMinHeight, 1f, 24f);
        // ImGui.SliderFloat("Piano Key Width", ref _pianoKeyWidth, 10f, 60f);

        if (ImGui.Button(_showTrackPanel ? "Hide Tracks" : "Show Tracks"))
            _showTrackPanel = !_showTrackPanel;
    }

    private void DrawTrackMenu()
    {
        ImGui.Text("Tracks");
        if (_plotData == null) return;

        // ensure visibility array length
        var maxIndex = Math.Max(_plotData.Length, Plugin.CurrentBardPlayback?.TrackInfos?.Length ?? 0);
        if (_trackVisible == null || _trackVisible.Length < maxIndex)
        {
            _trackVisible = new bool[maxIndex];
            for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = false;

            // initialize based on MidiFileConfig when available
            try
            {
                var cfgTracks = Plugin.CurrentBardPlayback?.MidiFileConfig?.Tracks;
                if (cfgTracks != null && cfgTracks.Count > 0)
                {
                    // default to false, only enable tracks explicitly enabled in config
                    foreach (var cfgTrack in cfgTracks)
                    {
                        if (cfgTrack == null) continue;
                        var idx = cfgTrack.Index;
                        if (idx >= 0 && idx < _trackVisible.Length)
                            _trackVisible[idx] = cfgTrack.Enabled && cfgTrack.AssignedCids.Count >= 1;
                    }
                }
                else
                {
                    // no config: show all tracks
                    for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = true;
                }
            }
            catch
            {
                // ignore and fallback to show all
                for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = true;
            }
        }

        for (int i = 0; i < _plotData.Length; i++)
        {
            var tinfo = _plotData[i].trackInfo;
            bool visible = (tinfo.Index < _trackVisible.Length) ? _trackVisible[tinfo.Index] : true;
            var color = GetTrackColor(tinfo.Index);
            ImGui.ColorButton($"##col{tinfo.Index}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16));
            ImGui.SameLine();
            if (ImGui.Checkbox($"[{tinfo.Index + 1:00}] {tinfo.TrackName}", ref visible))
            {
                if (tinfo.Index < _trackVisible.Length) _trackVisible[tinfo.Index] = visible;
            }
        }
    }

    private void DrawPianoRoll()
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

        // Handle mouse wheel for scrolling inside roll area
        var io = ImGui.GetIO();
        if (ImGui.IsMouseHoveringRect(new Vector2(pianoRollX, pianoRollY), new Vector2(pianoRollX + pianoRollWidth, pianoRollY + pianoRollHeight)))
        {
            if (io.MouseWheel > 0)
                _scrollTime -= 0.5;
            else if (io.MouseWheel < 0)
                _scrollTime += 0.5;

            if (io.KeyShift && io.MouseWheel != 0)
            {
                _timePixelsPerSecond += io.MouseWheel * 5;
                _timePixelsPerSecond = Math.Max(10, Math.Min(200, _timePixelsPerSecond));
            }
        }

        _scrollTime = Math.Max(0, _scrollTime);

        // Draw piano keys and roll area
        DrawPianoKeys(drawList, cursor.X, cursor.Y, _pianoKeyWidth, pianoRollHeight);
        DrawPianoRollArea(drawList, pianoRollX, pianoRollY, pianoRollWidth, pianoRollHeight, timelinePos);

        ImGui.EndChild();

        ImGui.Dummy(new Vector2(contentRegion.X, 0));
    }

    private void DrawPianoKeys(ImDrawListPtr drawList, float x, float y, float width, float height)
    {
        const int totalNotes = 128;
        float noteHeight = Math.Max(height / totalNotes, _noteMinHeight);

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
        float noteHeight = Math.Max(height / totalNotes, _noteMinHeight);

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
                // skip invisible tracks
                if (_trackVisible != null && trackInfo.Index < _trackVisible.Length && !_trackVisible[trackInfo.Index])
                    continue;

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
        try
        {
            var denom = Plugin.CurrentBardPlayback?.TrackInfos?.Length ?? 1;
            var safeDenom = Math.Max(1, denom);
            float hue = index / (float)safeDenom;
            ImGui.ColorConvertHSVtoRGB(hue, 0.8f, 1, &c.X, &c.Y, &c.Z);
        }
        catch
        {
            // fallback to white
            c = Vector4.One;
        }

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
