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

    public void RefreshPlotData()
    {
        // check if a new MIDI file was loaded
        var currentFilePath = Plugin.CurrentBardPlayback?.FilePath;
        bool fileChanged = currentFilePath != _lastLoadedFilePath;
        if (fileChanged)
        {
            _lastLoadedFilePath = currentFilePath;
            _trackVisible = null; // reset track visibility for new file
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

                _plotData = Plugin.CurrentBardPlayback.TrackChunks.Select((trackChunk, index) =>
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
        float visibleNotes = height / noteHeight;

        float minTop = visibleNotes;
        float maxTop = 127;

        _cameraTopNote = Math.Clamp(_cameraTopNote, minTop, maxTop);
    }

    private void HandlePianoInput(PianoRenderContext ctx)
    {
        var io = ImGui.GetIO();

        // pan move
        if (_panMode && ImGui.IsItemActive() &&
            ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _autoFollowPlayback = false;
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            Vector2 delta = io.MouseDelta;

            _cameraTime -= delta.X / ctx.View.PixelsPerSecond;
            _cameraTopNote -= delta.Y / ctx.View.NoteHeight;

            ClampCamera(ctx.Height, ctx.View.NoteHeight);

            if (_cameraTime < 0)
                _cameraTime = 0;

            // limit vertical scroll to max song duration
            var midiMaxTime = GetMaxScrollTime();
            if (_cameraTime > midiMaxTime)
                _cameraTime = midiMaxTime;
        }

        // zoom
        if (ImGui.IsItemHovered() && io.MouseWheel != 0)
        {
            // fixed zoom
            // _noteMinHeight = Math.Clamp(_noteMinHeight + io.MouseWheel * 2f, 10f, 40f);
            // _timePixelsPerSecond = Math.Clamp(_timePixelsPerSecond + io.MouseWheel * 15f, 25f, 500f);

            float zoomFactor = MathF.Pow(1.1f, io.MouseWheel);
            // 1.1f = 10% per scroll notch

            _noteMinHeight = Math.Clamp(_noteMinHeight * zoomFactor, 10f, 40f);
            _timePixelsPerSecond = Math.Clamp(_timePixelsPerSecond * zoomFactor, 25f, 500f);
        }
    }

    private void CenterOnNote(int note, float viewportHeight)
    {
        float visibleNotes = viewportHeight / _noteMinHeight;

        _cameraTopNote = note + (visibleNotes / 2f);

        ClampCamera(viewportHeight, _noteMinHeight);
    }

    private void CenterOnTime(double time, float viewportWidth)
    {
        float visibleTime = viewportWidth / _timePixelsPerSecond;

        _cameraTime = time - (visibleTime * 0.3); // offset to show some context after the point

        // clamp
        if (_cameraTime < 0)
            _cameraTime = 0;

        var maxTime = GetMaxScrollTime();
        if (_cameraTime > maxTime)
            _cameraTime = maxTime;
    }

    private void DrawTrackList()
    {
        if (ImGui.Checkbox($"##CheckAllTracks", ref _checlAllTracks))
        {
            if (_trackVisible == null || _trackVisible.Length == 0) return;
            for (int i = 0; i < _trackVisible.Length; i++) _trackVisible[i] = _checlAllTracks;
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
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

    private void DrawVoiceLimitList(float pianoRollWidth)
    {
        if (_plotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return;

        var voiceLimitRegions = GetSimultaneousNoteRegions(_maxVoiceLimit, true);
        if (voiceLimitRegions.Count == 0) return;

        if (ImGui.CollapsingHeader($"Voice Limit List##VoiceLimitList"))
        {
            for (int i = 0; i < voiceLimitRegions.Count; i++)
            {
                var voiceLimitRegion = voiceLimitRegions[i];
                bool isSelected = _selectedVoiceLimitItem == i;

                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered);
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered);
                    ImGui.PushStyleColor(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered);
                }

                string label = $"{voiceLimitRegion.start.GetDurationString()} ({voiceLimitRegion.noteCount})##voiceLimit_{i}";
                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedVoiceLimitItem = i;
                    CenterOnTime(voiceLimitRegion.start, pianoRollWidth);
                }

                if (isSelected)
                    ImGui.PopStyleColor(3);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    public List<(double start, double end, int noteCount)> GetSimultaneousNoteRegions(int maxSimultaneousNotes, bool groupRegions = false)
    {
        var result = new List<(double start, double end, int noteCount)>();

        if (_plotData?.Any() != true || !Plugin.CurrentBardPlayback.IsLoaded)
            return result;

        var events = new List<(double time, int delta)>();

        // all track events
        foreach (var (track, notes) in _plotData)
        {
            // compute only enabled tracks
            if (_trackVisible != null &&
                track.Index < _trackVisible.Length &&
                !_trackVisible[track.Index])
                continue;

            foreach (var note in notes)
            {
                events.Add((note.Item1, +1)); // start
                events.Add((note.Item2, -1)); // end
            }
        }

        if (events.Count == 0)
            return result;

        // order time
        events = events
            .OrderBy(e => e.time)
            .ThenBy(e => e.delta) // ensure -1 comes before +1 in same time
            .ToList();

        int activeNotes = 0;
        double? regionStart = null;
        int regionNoteCount = 0;

        foreach (var ev in events)
        {
            int previous = activeNotes;
            activeNotes += ev.delta;

            // voice limit region start
            if (previous < maxSimultaneousNotes &&
                activeNotes >= maxSimultaneousNotes)
            {
                regionStart = ev.time;
                regionNoteCount = activeNotes;
            }

            // voice limit region end
            if (previous >= maxSimultaneousNotes &&
                activeNotes < maxSimultaneousNotes &&
                regionStart.HasValue)
            {
                result.Add((regionStart.Value, ev.time, regionNoteCount));
                regionStart = null;
                regionNoteCount = 0;
            }
        }

        // if midi ends with max limit
        if (regionStart.HasValue)
        {
            result.Add((regionStart.Value, events.Last().time, regionNoteCount));
        }

        // group regions that are close together (within 1 second) to avoid visual clutter
        if (groupRegions && result.Count > 0)
        {
            var groupedResult = new List<(double start, double end, int noteCount)>();
            var currentGroup = result[0];

            for (int i = 1; i < result.Count; i++)
            {
                var region = result[i];
                // if the time difference is less than 1 second, group them
                if (region.start - currentGroup.end < 1.0)
                {
                    // keep the one with higher note count
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
