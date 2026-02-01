using System;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

namespace MidiBard.Extensions.DryWetMidi;

public static class DryWetMidiExtensions
{
    public static string DeviceName(this InputDevice device)
    {
        return device?.Name ?? "None";
    }

    public static TimeSpan? GetDurationTimeSpan(this MidiFile midiFile)
    {
        try
        {
            return midiFile?.GetDuration<MetricTimeSpan>();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error when getting midifile timespan");
            return null;
        }
    }
}
