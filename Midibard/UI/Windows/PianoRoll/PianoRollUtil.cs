using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;

namespace MidiBard;

public partial class PianoRollWindow
{
    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;
    }

    private PianoViewport BuildViewport(float width, float height)
    {
        float noteHeight = Math.Max(State.NoteMinHeight, 4f);
        float pixelsPerSecond = State.TimePixelsPerSecond;
        float visibleNotes = height / noteHeight;

        return new PianoViewport
        {
            NoteHeight = noteHeight,
            PixelsPerSecond = pixelsPerSecond,
            VisibleNotes = visibleNotes,
            TopNote = State.CameraTopNote,
            StartNote = (int)Math.Floor(State.CameraTopNote - visibleNotes),
            EndNote = (int)Math.Ceiling(State.CameraTopNote),
            StartTime = State.CameraTime,
            EndTime = State.CameraTime + (width / pixelsPerSecond)
        };
    }

    public void RefreshPlotData()
    {
        var currentFilePath = Plugin.CurrentBardPlayback?.FilePath;
        bool fileChanged = currentFilePath != State.LastLoadedFilePath;
        if (fileChanged)
        {
            State.LastLoadedFilePath = currentFilePath;
            State.TrackVisible = null;
        }

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

                State.PlotData = Plugin.CurrentBardPlayback.TrackChunks.Select((trackChunk, index) =>
                    {
                        var trackNotes = trackChunk.GetNotes()
                            .Select(j => (j.TimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(),
                                j.EndTimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(), (int)j.NoteNumber))
                            .ToArray();

                        return (Plugin.CurrentBardPlayback.TrackInfos[index], notes: trackNotes);
                    })
                    .ToArray();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error when refreshing piano roll plot data");
            }
        });

        EnsureTrackVisibilityInitialized();
        UpdateVoiceLimitRegions();
    }

    private double GetMaxScrollTime()
    {
        try
        {
            if (Plugin.CurrentBardPlayback?.IsLoaded == true)
            {
                var duration = Plugin.CurrentBardPlayback.GetDuration<MetricTimeSpan>();
                return duration.GetTotalSeconds();
            }
        }
        catch { }
        return 10;
    }

    private void ClampCamera(float height, float noteHeight)
    {
        float visibleNotes = height / State.NoteMinHeight;
        float minTop = visibleNotes;
        float maxTop = 127;
        State.CameraTopNote = Math.Clamp(State.CameraTopNote, minTop, maxTop);
    }

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

    private void CenterViewOnNote(int note, float viewportHeight)
    {
        float visibleNotes = viewportHeight / State.NoteMinHeight;
        State.CameraTopNote = note + (visibleNotes / 2f);
        ClampCamera(viewportHeight, State.NoteMinHeight);
    }

    private void CenterViewOnTime(double time, float viewportWidth)
    {
        float visibleTime = viewportWidth / State.TimePixelsPerSecond;
        State.CameraTime = time - (visibleTime * 0.3);

        if (State.CameraTime < 0)
            State.CameraTime = 0;

        var maxTime = GetMaxScrollTime();
        if (State.CameraTime > maxTime)
            State.CameraTime = maxTime;
    }

    private void EnsureTrackVisibilityInitialized()
    {
        if (State.PlotData == null) return;

        var maxIndex = Math.Max(State.PlotData.Length, Plugin.CurrentBardPlayback?.TrackInfos?.Length ?? 0);
        if (State.TrackVisible == null || State.TrackVisible.Length < maxIndex)
        {
            State.TrackVisible = new bool[maxIndex];
            for (int i = 0; i < State.TrackVisible.Length; i++) State.TrackVisible[i] = false;

            try
            {
                var cfgTracks = Plugin.CurrentBardPlayback?.MidiFileConfig?.Tracks;
                if (cfgTracks != null && cfgTracks.Count > 0)
                {
                    foreach (var cfgTrack in cfgTracks)
                    {
                        if (cfgTrack == null) continue;
                        var idx = cfgTrack.Index;
                        if (idx >= 0 && idx < State.TrackVisible.Length)
                            State.TrackVisible[idx] = cfgTrack.Enabled && cfgTrack.AssignedCids.Count >= 1;
                    }
                }
                else
                {
                    for (int i = 0; i < State.TrackVisible.Length; i++) State.TrackVisible[i] = true;
                }
            }
            catch
            {
                for (int i = 0; i < State.TrackVisible.Length; i++) State.TrackVisible[i] = true;
            }
        }
    }

    private void DrawTrackList()
    {
        if (State.PlotData == null) return;

        EnsureTrackVisibilityInitialized();

        if (ImGui.CollapsingHeader($"Tracks##TrackListCollapsing"))
        {
            bool checkAll = State.CheckAllTracks;
            if (ImGui.Checkbox($"##CheckAllTracks", ref checkAll))
            {
                State.CheckAllTracks = checkAll;
                if (State.TrackVisible == null || State.TrackVisible.Length == 0) return;
                for (int i = 0; i < State.TrackVisible.Length; i++) State.TrackVisible[i] = checkAll;
                UpdateVoiceLimitRegions();
            }

            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
            ImGui.Text("Tracks");

            for (int i = 0; i < State.PlotData.Length; i++)
            {
                var tinfo = State.PlotData[i].trackInfo;
                bool visible = (tinfo.Index < State.TrackVisible.Length) ? State.TrackVisible[tinfo.Index] : true;
                var color = GetTrackColor(tinfo.Index);
                ImGui.ColorButton($"##col{tinfo.Index}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(16, 16));
                ImGui.SameLine();
                if (ImGui.Checkbox($"[{tinfo.Index + 1:00}] {tinfo.TrackName}", ref visible))
                {
                    if (tinfo.Index < State.TrackVisible.Length) State.TrackVisible[tinfo.Index] = visible;
                    UpdateVoiceLimitRegions();
                }
            }
        }
    }

    private void DrawVoiceLimitList(float pianoRollWidth)
    {
        if (State.PlotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        var voiceLimitRegions = State.VoiceLimitRegions;

        if (ImGui.CollapsingHeader($"Voice Limit List ({voiceLimitRegions.Count})##VoiceLimitList"))
        {
            for (int i = 0; i < voiceLimitRegions.Count; i++)
            {
                var voiceLimitRegion = voiceLimitRegions[i];
                bool isSelected = State.SelectedVoiceLimitItem == i;

                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered);
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered);
                    ImGui.PushStyleColor(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered);
                }

                string label = $"{i + 1:000} - {voiceLimitRegion.start.GetDurationString()} ({voiceLimitRegion.noteCount})##voiceLimit_{i}";
                if (ImGui.Selectable(label, isSelected))
                {
                    State.SelectedVoiceLimitItem = i;
                    CenterViewOnTime(voiceLimitRegion.start, pianoRollWidth);
                }

                if (isSelected)
                    ImGui.PopStyleColor(3);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void UpdateVoiceLimitRegions()
    {
        State.VoiceLimitRegions = ComputeSimultaneousNoteRegions(State.MaxVoiceLimit, State.GroupVoiceLimitRegions);
    }

    private List<(double start, double end, int noteCount)> ComputeSimultaneousNoteRegions(int maxSimultaneousNotes, bool groupRegions = false)
    {
        var result = new List<(double start, double end, int noteCount)>();

        if (State.PlotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return result;

        var events = new List<(double time, int delta)>();

        foreach (var (track, notes) in State.PlotData)
        {
            if (State.TrackVisible != null &&
                track.Index < State.TrackVisible.Length &&
                !State.TrackVisible[track.Index])
                continue;

            foreach (var note in notes)
            {
                events.Add((note.Item1, +1));
                events.Add((note.Item2, -1));
            }
        }

        if (events.Count == 0)
            return result;

        events = events
            .OrderBy(e => e.time)
            .ThenBy(e => e.delta)
            .ToList();

        int activeNotes = 0;
        double? regionStart = null;
        int maxNoteCountInRegion = 0;

        foreach (var ev in events)
        {
            int previous = activeNotes;
            activeNotes += ev.delta;

            if (previous < maxSimultaneousNotes && activeNotes >= maxSimultaneousNotes)
            {
                regionStart = ev.time;
                maxNoteCountInRegion = activeNotes;
            }

            if (regionStart.HasValue && activeNotes > maxNoteCountInRegion)
            {
                maxNoteCountInRegion = activeNotes;
            }

            if (previous >= maxSimultaneousNotes && activeNotes < maxSimultaneousNotes && regionStart.HasValue)
            {
                result.Add((regionStart.Value, ev.time, maxNoteCountInRegion));
                regionStart = null;
                maxNoteCountInRegion = 0;
            }
        }

        if (regionStart.HasValue)
        {
            result.Add((regionStart.Value, events.Last().time, maxNoteCountInRegion));
        }

        if (groupRegions && result.Count > 0)
        {
            var groupedResult = new List<(double start, double end, int noteCount)>();
            var currentGroup = result[0];

            for (int i = 1; i < result.Count; i++)
            {
                var region = result[i];
                if (region.start - currentGroup.end < 1.0)
                {
                    if (region.noteCount > currentGroup.noteCount)
                    {
                        currentGroup = region;
                    }
                }
                else
                {
                    groupedResult.Add(currentGroup);
                    currentGroup = region;
                }
            }
            groupedResult.Add(currentGroup);
            return groupedResult;
        }

        return result;
    }
}
