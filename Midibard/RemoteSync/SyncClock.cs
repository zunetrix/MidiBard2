using System.Diagnostics;

namespace MidiBard.RemoteSync;

/// <summary>
/// Millisecond clock shared by the sync protocol, read straight from the
/// system-wide performance counter (QPC). The same value is observed by every
/// process on one Windows machine, so same-machine clients (gateway siblings)
/// share one timeline without any offset, and wall-clock adjustments (NTP steps,
/// DST, manual changes) can never corrupt measured offsets. Across machines the
/// values are only comparable through measured offsets.
/// </summary>
internal static class SyncClock
{
    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    /// <summary>Current time in milliseconds since the machine's performance-counter epoch (boot).</summary>
    public static long NowMs => (long)(Stopwatch.GetTimestamp() * MsPerTick);
}
