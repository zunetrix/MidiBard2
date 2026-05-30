using System;
using System.Collections.Generic;
using System.Numerics;

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
/// Unified per-track display state: note data, visibility, and optional color override.
/// Replaces the previously separate PlotData tuple array, TrackVisible bool array,
/// and TrackColors dictionary.
/// </summary>
public class TrackDisplayState
{
    public TrackInfo TrackInfo { get; init; }
    public (double start, double end, int noteNumber)[] Notes { get; set; }
    /// <summary>Whether this track is shown in the piano roll.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>Prevent note selection/interaction in the piano roll editor.</summary>
    public bool IsLocked { get; set; } = false;
    /// <summary>User-chosen color. Null means auto-generated HSV color.</summary>
    public Vector4? Color { get; set; }
    /// <summary>Render notes transposed to the playable C3–C6 range.</summary>
    public bool ShowAdaptedNotes { get; set; } = true;
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

    // ==================== Track State ====================

    /// <summary>
    /// Per-track display state (notes, visibility, color).
    /// Null until the first MIDI file is loaded.
    /// </summary>
    public TrackDisplayState[] Tracks { get; set; }

    /// <summary>Whether "check all tracks" master checkbox is enabled</summary>
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

    // ==================== View Options ====================

    /// <summary>Whether left panel (track list) is visible</summary>
    public bool ShowLeftPanel { get; set; } = true;

    /// <summary>Whether note labels are shown</summary>
    public bool ShowNoteLabel { get; set; }

    /// <summary>Whether note borders are shown</summary>
    public bool ShowNoteBorder { get; set; } = true;

    /// <summary>Whether C3-C6 range markers are shown</summary>
    public bool ShowC3C6Range { get; set; } = true;

    /// <summary>Whether voice limit markers are shown</summary>
    public bool ShowVoiceLimit { get; set; } = true;

    /// <summary>Whether time markers (seconds) are shown</summary>
    public bool ShowSeconds { get; set; } = true;

    /// <summary>Beat subdivision for grid display</summary>
    public BeatSubdivision BeatDivision { get; set; } = BeatSubdivision.Quarter;

    /// <summary>When true, note moves/resizes snap to the active beat subdivision grid.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>When true, program change events are rendered as vertical markers in the piano roll.</summary>
    public bool ShowProgramChangeMarkers { get; set; } = false;

    /// <summary>When true, clicking a piano key plays a preview note.</summary>
    public bool ShowNotePreview { get; set; } = true;

    // ==================== Constants ====================

    public const float PianoKeyWidth = 80f;
    public const int MaxNote = 127;

    public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // ==================== Colors ====================

    /// <summary>Color for note borders (default black)</summary>
    public Vector4 NoteBorderColor { get; set; } = new Vector4(0f, 0f, 0f, 1f);

    /// <summary>Color for note labels (default black)</summary>
    public Vector4 NoteLabelColor { get; set; } = new Vector4(0f, 0f, 0f, 1f);

    /// <summary>Color for light grid lines (default light gray)</summary>
    public Vector4 GridLightColor { get; set; } = new Vector4(0.26f, 0.33f, 0.37f, 1f);

    /// <summary>Color for dark grid lines (default dark gray)</summary>
    public Vector4 GridDarkColor { get; set; } = new Vector4(0.25f, 0.32f, 0.36f, 1f);

    /// <summary>Color for grid line separators</summary>
    public Vector4 GridLineColor { get; set; } = new Vector4(0.12f, 0.19f, 0.23f, 1f);

    // ==================== Helper Methods ====================

    /// <summary>Reset state for a new MIDI file (dead-code stub - actual reset is in RefreshPlotData).</summary>
    public void ResetForNewFile(string filePath)
    {
        LastLoadedFilePath = filePath;
        Tracks = null;
        VoiceLimitRegions = new List<(double start, double end, int noteCount)>();
        InitialCenterCameraPositionDone = false;
        CameraTime = 0;
        TimelinePos = 0;
    }

    /// <summary>Get the maximum scrollable time based on current MIDI duration.</summary>
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
        return 10;
    }

    /// <summary>Clamp camera position to valid bounds.</summary>
    public void ClampCamera(float viewportHeight)
    {
        float visibleNotes = viewportHeight / NoteMinHeight;
        CameraTopNote = Math.Clamp(CameraTopNote, visibleNotes, MaxNote);

        if (CameraTime < 0)
            CameraTime = 0;
    }

    /// <summary>Clamp camera time to maximum duration.</summary>
    public void ClampCameraTime(double maxTime)
    {
        if (CameraTime > maxTime)
            CameraTime = maxTime;
    }
}
