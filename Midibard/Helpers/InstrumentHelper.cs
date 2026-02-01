namespace MidiBard.Util;

internal static class InstrumentHelper
{

    internal static bool IsGuitar(uint instrumentId) => instrumentId is 24 or 25 or 26 or 27 or 28;

    internal static int GetGuitarTone(uint instrumentId) => instrumentId switch
    {
        24 => 0,
        25 => 1,
        26 => 2,
        27 => 3,
        28 => 4,
        _ => -1
    };
}
