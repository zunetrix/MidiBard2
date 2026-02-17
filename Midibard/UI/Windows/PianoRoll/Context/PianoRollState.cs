using System;
using System.Collections.Generic;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;

namespace MidiBard;

public enum BeatSubdivision
{
    Bars = 0,
    Beat = 1,
    Half = 2,
    Quarter = 4,
    Eighth = 8,
    Sixteenth = 16,
    ThirtySecond = 32,
    SixtyFourth = 64,
    OneTwentyEighth = 128
}

/// <summary>
/// Represents the state of the Piano Roll viewer.
/// Contains all mutable state data separated from rendering logic.
/// </summary>
public class PianoRollState
{
    // ==================== Camera / View State ====================

    /// <summary>Visible time on the left side of the viewport (in seconds)</summary>
    public double CameraTime { get; set; } = 0;

    /// <summary>Top note visible in the viewport (highest note number)</summary>
    public float CameraTopNote { get; set; } = 127;

    /// <summary>Whether initial camera position has been set</summary>
    public bool InitialCenterCameraPositionDone { get; set; }

    /// <summary>Pixels per second for time axis (controls zoom)</summary>
    public float TimePixelsPerSecond { get; set; } = 25f;

    /// <summary>Minimum height for note rendering (controls vertical zoom)</summary>
    public float NoteMinHeight { get; set; } = 10f;

    /// <summary>Current playback position (in seconds)</summary>
    public double TimelinePos { get; set; }

    /// <summary>Whether camera follows playback automatically</summary>
    public bool AutoFollowPlayback { get; set; } = true;

    /// <summary>Whether panning mode is enabled (vs note selection)</summary>
    public bool PanMode { get; set; } = true;

    // ==================== Track Visibility ====================

    /// <summary>Array of track visibility states (true = visible)</summary>
    public bool[] TrackVisible { get; set; }

    /// <summary>Whether "check all tracks" is enabled</summary>
    public bool CheckAllTracks { get; set; } = true;

    // ==================== Voice Limit ====================

    /// <summary>List of regions where voice limit is exceeded</summary>
    public List<(double start, double end, int noteCount)> VoiceLimitRegions { get; set; } = new();

    /// <summary>Currently selected voice limit region index</summary>
    public int SelectedVoiceLimitItem { get; set; }

    /// <summary>Maximum allowed simultaneous notes</summary>
    public int MaxVoiceLimit { get; set; } = 16;

    /// <summary>Whether to group voice limit regions</summary>
    public bool GroupVoiceLimitRegions { get; set; } = true;

    // ==================== File/MIDI State ====================

    /// <summary>Path of currently loaded MIDI file</summary>
    public string LastLoadedFilePath { get; set; }

    /// <summary>Display name of current song</summary>
    public string SongName { get; set; } = string.Empty;

    /// <summary>Plot data containing note information for all tracks</summary>
    public (TrackInfo trackInfo, (double start, double end, int noteNumber)[] notes)[] PlotData { get; set; }

    // ==================== View Options ====================

    /// <summary>Whether left panel (track list) is visible</summary>
    public bool ShowLeftPanel { get; set; } = true;

    /// <summary>Whether note labels are shown</summary>
    public bool ShowNoteLabel { get; set; }
    /// <summary>Render notes as auto tranposed to range c3-c6</summary>
    public bool ShowAdaptedNotes { get; set; }

    /// <summary>Whether note borders are shown</summary>
    public bool ShowNoteBorder { get; set; } = true;

    /// <summary>Whether C3-C6 range markers are shown</summary>
    public bool ShowC3C6Range { get; set; } = true;

    /// <summary>Whether voice limit markers are shown</summary>
    public bool ShowVoiceLimit { get; set; } = true;

    /// <summary>Whether time markers (seconds) are shown</summary>
    public bool ShowSeconds { get; set; } = true;

    /// <summary>Beat subdivision for grid display</summary>
    public BeatSubdivision BeatDivision { get; set; }

    // ==================== Constants ====================

    public const float PianoKeyWidth = 80f;
    public const int MaxNote = 127;

    public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // ==================== Helper Methods ====================

    /// <summary>
    /// Reset state for a new MIDI file
    /// </summary>
    public void ResetForNewFile(string filePath)
    {
        LastLoadedFilePath = filePath;
        TrackVisible = null;
        VoiceLimitRegions = new List<(double start, double end, int noteCount)>();
        InitialCenterCameraPositionDone = false;
        CameraTime = 0;
        TimelinePos = 0;
    }

    /// <summary>
    /// Get the maximum scrollable time based on current MIDI duration
    /// </summary>
    public double GetMaxScrollTime(Plugin plugin)
    {
        try
        {
            if (plugin.CurrentBardPlayback?.IsLoaded == true)
            {
                var duration = plugin.CurrentBardPlayback.GetDuration<MetricTimeSpan>();
                return duration.GetTotalSeconds();
            }
        }
        catch { }
        return 10; // default fallback
    }

    /// <summary>
    /// Clamp camera position to valid bounds
    /// </summary>
    public void ClampCamera(float viewportHeight)
    {
        float visibleNotes = viewportHeight / NoteMinHeight;
        CameraTopNote = Math.Clamp(CameraTopNote, visibleNotes, MaxNote);

        if (CameraTime < 0)
            CameraTime = 0;
    }

    /// <summary>
    /// Clamp camera time to maximum duration
    /// </summary>
    public void ClampCameraTime(double maxTime)
    {
        if (CameraTime > maxTime)
            CameraTime = maxTime;
    }
}
