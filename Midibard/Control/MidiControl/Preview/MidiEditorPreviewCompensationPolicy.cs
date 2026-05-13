using System;

namespace MidiBard.Control.MidiControl.Preview;

internal interface IMidiEditorPreviewCompensationProvider
{
    int GetCompensationMs(uint instrumentId, int gameNote);
}

internal sealed class NoOpMidiEditorPreviewCompensationProvider : IMidiEditorPreviewCompensationProvider
{
    public static NoOpMidiEditorPreviewCompensationProvider Instance { get; } = new();

    private NoOpMidiEditorPreviewCompensationProvider()
    {
    }

    public int GetCompensationMs(uint instrumentId, int gameNote)
        => 0;
}

internal sealed class MidiEditorPreviewCompensationPolicy(IMidiEditorPreviewCompensationProvider compensationProvider)
{
    private readonly record struct LastNoteOn(int TrackIndex, long Time, int GameNote, int DelayMs);

    private LastNoteOn? lastNoteOn;

    public int GetDelayMs(uint instrumentId, int gameNote, int trackIndex, long time, bool isNoteOn)
    {
        var delayMs = Math.Max(0, compensationProvider.GetCompensationMs(instrumentId, gameNote));
        if (isNoteOn && lastNoteOn is { } previous &&
            previous.TrackIndex == trackIndex &&
            previous.Time == time &&
            ((delayMs < previous.DelayMs && gameNote > previous.GameNote) ||
             (delayMs > previous.DelayMs && gameNote < previous.GameNote)))
        {
            delayMs = previous.DelayMs;
        }

        if (isNoteOn)
            lastNoteOn = new LastNoteOn(trackIndex, time, gameNote, delayMs);

        return delayMs;
    }

    public void Reset()
        => lastNoteOn = null;
}
