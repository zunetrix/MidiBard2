using System;

namespace MidiBard;

internal static class MidiEditorPreviewControlTooltips
{
    public const string RestartPreview = "Restart Preview";
    public const string ResumePreview = "Resume Preview";
    public const string PausePreview = "Pause Preview";
    public const string StopPreview = "Stop Preview";
}

internal static class MidiEditorPreviewCamera
{
    public static double FollowPlayback(double cameraTime, double playbackPosition, double visibleTime, double maxTime)
    {
        if (visibleTime <= 0)
            return Clamp(cameraTime, maxTime);

        var start = Clamp(cameraTime, maxTime);
        var midpoint = start + visibleTime * 0.5;

        return playbackPosition < start || playbackPosition >= midpoint
            ? Clamp(playbackPosition - visibleTime * 0.5, maxTime)
            : start;
    }

    public static double EnsureVisible(double cameraTime, double playbackPosition, double visibleTime, double maxTime)
    {
        if (visibleTime <= 0)
            return Clamp(cameraTime, maxTime);

        var start = Clamp(cameraTime, maxTime);
        var end = start + visibleTime;

        return playbackPosition < start || playbackPosition > end
            ? Clamp(playbackPosition - visibleTime * 0.5, maxTime)
            : start;
    }

    public static double GetVisibleTime(float pianoRollWidth, float pixelsPerSecond)
        => pianoRollWidth > 0 && pixelsPerSecond > 0 ? pianoRollWidth / pixelsPerSecond : 0;

    public static double Clamp(double cameraTime, double maxTime)
        => Math.Max(0, Math.Min(cameraTime, Math.Max(0, maxTime)));
}
