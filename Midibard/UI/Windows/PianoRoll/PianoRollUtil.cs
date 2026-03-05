using System;
using System.Collections.Generic;
using System.Linq;

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
            State.TrackVisible = null;
            // resets timeline position when file changed
            State.CameraTime = 0;
        }

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
