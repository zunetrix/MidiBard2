using System.Collections.Generic;

namespace MidiBard;

public enum TrackGroupMode
{
    GroupByCapture = 0,
    OneTrackPerPlayer = 1,
}

public class TrackAssignmentRule
{
    public bool Enabled = true;
    public string Label = string.Empty;
    public string Pattern = string.Empty;
    public bool IgnoreCase = true;
    public TrackGroupMode Mode = TrackGroupMode.GroupByCapture;
}

public class TrackAssignmentConfig
{
    public bool Enabled = false;
    public int MaxPerformers = 8;
    public bool AssignUnmatchedTracksSequentially = true;
    public bool CompactAbsentMembers = false;
    /// <summary>
    /// When true: once all MaxPerformers slots are allocated, no further tracks
    /// are assigned - including tracks that match an existing capture group key.
    /// Prevents "overflow" tracks (e.g. raw reference tracks at the end of a MIDI)
    /// from being silently routed to an already-assigned performer.
    /// </summary>
    public bool StopAssignmentAfterMaxPerformers = false;
    public List<TrackAssignmentRule> CaptureRules { get; set; } = DefaultCaptureRules();

    /// <summary>
    /// Pre-configured capture rules (disabled by default) covering the most common
    /// track-naming conventions. Shown as starting presets for new configurations.
    /// </summary>
    public static List<TrackAssignmentRule> DefaultCaptureRules() => new()
    {
        new TrackAssignmentRule
        {
            Enabled = false,
            Label = "Letter suffix: Piano a",
            Pattern = @"\s([a-z])$",
            IgnoreCase = true,
            Mode = TrackGroupMode.GroupByCapture,
        },
        new TrackAssignmentRule
        {
            Enabled = false,
            Label = "Number prefix: 1-Trumpet",
            Pattern = @"^(\d+)-",
            IgnoreCase = false,
            Mode = TrackGroupMode.GroupByCapture,
        },
        new TrackAssignmentRule
        {
            Enabled = false,
            Label = "Number in parens: Piano (1)",
            Pattern = @"\s\((\d+)\)$",
            IgnoreCase = false,
            Mode = TrackGroupMode.GroupByCapture,
        },
        new TrackAssignmentRule
        {
            Enabled = false,
            Label = "Letter in parens: Piano (a)",
            Pattern = @"\s\(([a-z])\)$",
            IgnoreCase = true,
            Mode = TrackGroupMode.GroupByCapture,
        },
    };
}
