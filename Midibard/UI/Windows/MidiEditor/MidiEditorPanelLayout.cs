using System;

namespace MidiBard;

internal readonly record struct MidiEditorPanelLayoutResult(
    float TrackWidth,
    float EventWidth,
    float PianoRollWidth);

internal static class MidiEditorPanelLayout
{
    public const float SplitterWidth = 5f;

    public static float DefaultTrackWidth(float scale) => 360f * scale;
    public static float DefaultEventWidth(float scale) => 420f * scale;
    public static float MinTrackWidth(float scale) => 300f * scale;
    public static float MinEventWidth(float scale) => 220f * scale;
    public static float MinPianoRollWidth(float scale) => 180f * scale;

    public static MidiEditorPanelLayoutResult Calculate(
        float availableWidth,
        bool showTrackPanel,
        bool showEventPanel,
        float requestedTrackWidth,
        float requestedEventWidth,
        float scale)
    {
        availableWidth = MathF.Max(0f, availableWidth);
        scale = MathF.Max(0.01f, scale);

        var splitterCount = (showTrackPanel ? 1 : 0) + (showEventPanel ? 1 : 0);
        var contentWidth = MathF.Max(0f, availableWidth - splitterCount * SplitterWidth * scale);
        var pianoMinimum = MathF.Min(MinPianoRollWidth(scale), contentWidth);
        var sidePanelBudget = MathF.Max(0f, contentWidth - pianoMinimum);

        var trackWidth = showTrackPanel
            ? MathF.Max(MinTrackWidth(scale), requestedTrackWidth)
            : 0f;
        var eventWidth = showEventPanel
            ? MathF.Max(MinEventWidth(scale), requestedEventWidth)
            : 0f;

        var sidePanelTotal = trackWidth + eventWidth;
        if (sidePanelTotal > sidePanelBudget && sidePanelTotal > 0f)
        {
            var shrinkRatio = sidePanelBudget / sidePanelTotal;
            trackWidth *= shrinkRatio;
            eventWidth *= shrinkRatio;
        }

        var pianoRollWidth = MathF.Max(0f, contentWidth - trackWidth - eventWidth);
        return new MidiEditorPanelLayoutResult(trackWidth, eventWidth, pianoRollWidth);
    }

    public static float MaxTrackResizeWidth(
        float availableWidth,
        bool showEventPanel,
        float requestedEventWidth,
        float scale)
    {
        scale = MathF.Max(0.01f, scale);
        var splitterCount = 1 + (showEventPanel ? 1 : 0);
        var reservedWidth =
            splitterCount * SplitterWidth * scale +
            MinPianoRollWidth(scale) +
            (showEventPanel ? MathF.Max(MinEventWidth(scale), requestedEventWidth) : 0f);
        return MathF.Max(MinTrackWidth(scale), availableWidth - reservedWidth);
    }

    public static float MaxEventResizeWidth(
        float availableWidth,
        bool showTrackPanel,
        float requestedTrackWidth,
        float scale)
    {
        scale = MathF.Max(0.01f, scale);
        var splitterCount = (showTrackPanel ? 1 : 0) + 1;
        var reservedWidth =
            splitterCount * SplitterWidth * scale +
            MinPianoRollWidth(scale) +
            (showTrackPanel ? MathF.Max(MinTrackWidth(scale), requestedTrackWidth) : 0f);
        return MathF.Max(MinEventWidth(scale), availableWidth - reservedWidth);
    }
}
