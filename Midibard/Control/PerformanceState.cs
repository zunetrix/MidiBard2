using MidiBard.Managers;
using MidiBard.Util;

namespace MidiBard.Control;

// Read-only snapshot of the local player's current performance state via game memory.
internal static class PerformanceState
{
    // The instrument rowId currently equipped by the local player (0 = none).
    public static unsafe byte CurrentInstrument => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset);

    // The guitar tone index (0-4) when playing a guitar instrument; irrelevant otherwise.
    internal static unsafe byte CurrentTone => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset + 1);

    // Resolves the effective instrument index including guitar tone offset (24–28) for compensation lookups.
    internal static int CurrentInstrumentWithTone => CurrentInstrument >= 24 ? 24 + CurrentTone : CurrentInstrument;

    // True when the local player currently has a guitar-family instrument equipped.
    internal static bool PlayingGuitar => InstrumentHelper.IsGuitar(CurrentInstrument);
}
