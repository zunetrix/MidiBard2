using Melanchall.DryWetMidi.Multimedia;

namespace MidiBard.Extensions;

public static class DryWetMidiExtensions
{
    public static string DeviceName(this InputDevice device)
    {
        return device?.Name ?? "None";
    }
}
