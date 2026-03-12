using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;

namespace MidiBard;

public partial class PianoRollWindow
{
    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;
    }

    public void RefreshPlotData()
    {
        var currentFilePath = Plugin.CurrentBardPlayback?.FilePath;
        bool fileChanged = currentFilePath != State.LastLoadedFilePath;
        if (fileChanged)
        {
            State.LastLoadedFilePath = currentFilePath;
            State.Tracks = null;
            State.CameraTime = 0;
            _cachedTempoMap = null;  // Invalidate cached tempo map
            _voiceLimitCacheKey = -1;
        }

        // Skip rebuild if tracks are already populated - MIDI data doesn't change mid-song.
        if (State.Tracks != null)
        {
            UpdateVoiceLimitRegions();
            return;
        }

        try
        {
            if (Plugin.CurrentBardPlayback?.TrackInfos == null)
            {
                DalamudApi.PluginLog.Debug("try RefreshPlotData but CurrentTracks is null");
                return;
            }

            var tmap = Plugin.CurrentBardPlayback.TempoMap;

            State.Tracks = Plugin.CurrentBardPlayback.TrackChunks.Select((trackChunk, index) =>
                {
                    var notes = trackChunk.GetNotes()
                        .Select(j => (j.TimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(),
                            j.EndTimeAs<MetricTimeSpan>(tmap).GetTotalSeconds(), (int)j.NoteNumber))
                        .ToArray();

                    return new TrackDisplayState
                    {
                        TrackInfo = Plugin.CurrentBardPlayback.TrackInfos[index],
                        Notes = notes,
                    };
                })
                .ToArray();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error when refreshing piano roll plot data");
        }

        InitTrackList();
        UpdateVoiceLimitRegions();
    }

    private double GetMaxScrollTime()
    {
        if (Plugin.CurrentBardPlayback?.IsLoaded == true)
        {
            var duration = Plugin.CurrentBardPlayback.GetDuration<MetricTimeSpan>();
            return duration.GetTotalSeconds();
        }

        return 10;
    }

    private void ClampCamera(float height, float noteHeight)
    {
        float visibleNotes = height / State.NoteMinHeight;
        float minTop = visibleNotes;
        float maxTop = 127;
        State.CameraTopNote = Math.Clamp(State.CameraTopNote, minTop, maxTop);
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

    private void FollowPlaybackCursor(float width, float pixelsPerSecond, double timelinePos)
    {
        if (State.AutoFollowPlayback)
        {
            double visibleTime = width / pixelsPerSecond;
            State.CameraTime = timelinePos - visibleTime * 0.3;

            if (State.CameraTime < 0)
                State.CameraTime = 0;

            var midiMaxTime = GetMaxScrollTime();
            if (State.CameraTime > midiMaxTime)
                State.CameraTime = midiMaxTime;
        }
    }

    private void InitTrackList()
    {
        if (State.Tracks == null) return;

        try
        {
            var cfgTracks = Plugin.CurrentBardPlayback?.MidiFileConfig?.Tracks;
            if (cfgTracks != null && cfgTracks.Count > 0)
            {
                // Start with all hidden; enable only tracks that have an active assignment.
                foreach (var t in State.Tracks) t.Visible = false;
                foreach (var cfgTrack in cfgTracks)
                {
                    if (cfgTrack == null) continue;
                    var track = State.Tracks.FirstOrDefault(t => t.TrackInfo.Index == cfgTrack.Index);
                    if (track != null)
                        track.Visible = cfgTrack.Enabled && cfgTrack.AssignedCids.Count >= 1;
                }
            }
            else
            {
                foreach (var t in State.Tracks) t.Visible = true;
            }
        }
        catch
        {
            foreach (var t in State.Tracks) t.Visible = true;
        }
    }

    private void UpdateVoiceLimitRegions()
    {
        // Skip expensive O(N log N) recomputation when nothing affecting voice regions has changed
        var key = ComputeVoiceLimitCacheKey();
        if (key == _voiceLimitCacheKey) return;
        _voiceLimitCacheKey = key;
        State.VoiceLimitRegions = ComputeSimultaneousNoteRegions(State.MaxVoiceLimit, State.GroupVoiceLimitRegions);
    }

    private int ComputeVoiceLimitCacheKey()
    {
        var h = new HashCode();
        h.Add(State.MaxVoiceLimit);
        h.Add(State.GroupVoiceLimitRegions);
        if (State.Tracks != null)
        {
            foreach (var track in State.Tracks)
            {
                h.Add(track.Visible);
                h.Add(track.TrackInfo.Index);
            }
        }
        return h.ToHashCode();
    }

    private List<(double start, double end, int noteCount)> ComputeSimultaneousNoteRegions(int maxSimultaneousNotes, bool groupRegions = false)
    {
        var result = new List<(double start, double end, int noteCount)>();

        if (State.Tracks?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return result;

        var events = new List<(double time, int delta)>();

        foreach (var track in State.Tracks)
        {
            if (!track.Visible) continue;
            foreach (var note in track.Notes)
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

    private void DrawSplitter(string id, ref float leftWidth, float minWidth, float maxWidth)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(0, 0)))
        {
            ImGui.InvisibleButton(id, ImGuiHelpers.ScaledVector2(5, -1));
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                leftWidth += ImGui.GetIO().MouseDelta.X;
                leftWidth = MathF.Max(minWidth, MathF.Min(leftWidth, maxWidth));
            }
        }
    }
}
