using System;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Extensions.Time;

public static class TimeExtensions
{
    public static string GetDurationString(this TimeSpan duration)
    {
        var fmt = duration.TotalDays >= 1 ? @"d\d\ h\:mm\:ss"
                : duration.TotalHours >= 1 ? @"h\:mm\:ss"
                : @"m\:ss";
        return duration.ToString(fmt);
    }

    public static string GetDurationString(this double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public static float SafeDivideMetricTimeSpan(this MetricTimeSpan current, MetricTimeSpan total)
    {
        try
        {
            if (current == null) return 0;

            return (float)current.Divide(total);
        }
        catch
        {
            return 0f;
        }
    }

    public static string FormatSecondsToTime(this double seconds)
    {
        int totalSeconds = (int)seconds;
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        return $"{minutes}:{secs:00}";
    }

    public static TimeSpan GetTimeSpan(this MetricTimeSpan t) => new TimeSpan(t.TotalMicroseconds * 10);

    public static double GetTotalSeconds(this MetricTimeSpan t) => t.TotalMicroseconds / 1000_000d;
}
