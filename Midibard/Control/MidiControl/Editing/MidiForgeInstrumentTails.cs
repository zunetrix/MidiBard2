using System;
using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Editing;

internal static class MidiForgeInstrumentTails
{
    private readonly record struct OneShotZone(
        uint InstrumentId,
        int KeyLow,
        int KeyHigh,
        int RootKey,
        double AttackMs,
        double AudibleMsAtRoot);

    private static readonly OneShotZone[] OneShotZones =
    {
        // Harp (1)
        new(1, 48, 58, 57, 66, 1273.88),
        new(1, 59, 67, 67, 65, 1274.09),
        new(1, 68, 76, 72, 66, 1304.25),
        new(1, 77, 84, 77, 65, 1094.12),
        // Piano (2)
        new(2, 48, 63, 57, 70, 1489.86),
        new(2, 64, 68, 65, 70, 1469.82),
        new(2, 69, 73, 72, 70, 1329.52),
        new(2, 74, 77, 76, 69, 1259.59),
        new(2, 78, 84, 79, 70, 1480.71),
        // Lute (3)
        new(3, 48, 62, 60, 81, 1714.70),
        new(3, 63, 69, 67, 78, 1716.50),
        new(3, 70, 76, 72, 79, 1672.15),
        new(3, 77, 84, 79, 79, 1474.48),
        // Fiddle (4)
        new(4, 48, 50, 48, 78, 632.74),
        new(4, 51, 54, 52, 68, 631.68),
        new(4, 55, 58, 55, 79, 631.65),
        new(4, 59, 61, 60, 68, 593.17),
        new(4, 62, 66, 64, 67, 631.07),
        new(4, 67, 69, 67, 72, 633.47),
        new(4, 70, 75, 72, 69, 633.61),
        new(4, 76, 84, 79, 71, 633.50),
        // Timpani (10)
        new(10, 48, 63, 60, 67, 1190.95),
        new(10, 64, 71, 67, 65, 1243.06),
        new(10, 72, 84, 72, 67, 1306.33),
        // Bongo (11)
        new(11, 48, 55, 55, 69, 700.34),
        new(11, 56, 69, 60, 65, 534.51),
        new(11, 70, 84, 72, 54, 275.19),
        // Bass Drum (12)
        new(12, 48, 54, 54, 71, 447.51),
        new(12, 55, 65, 60, 65, 333.97),
        new(12, 66, 71, 66, 55, 342.98),
        new(12, 72, 84, 72, 46, 253.86),
        // Snare Drum (13)
        new(13, 48, 59, 59, 71, 189.16),
        new(13, 60, 67, 67, 63, 196.61),
        new(13, 68, 71, 68, 62, 197.34),
        new(13, 72, 84, 72, 55, 204.41),
        // Cymbal (14)
        new(14, 48, 50, 48, 5, 1264.69),
        new(14, 51, 56, 54, 5, 1266.31),
        new(14, 57, 62, 60, 5, 1267.31),
        new(14, 63, 68, 66, 5, 1268.04),
        new(14, 69, 74, 72, 5, 1268.58),
        new(14, 75, 80, 78, 5, 1268.54),
        new(14, 81, 84, 84, 5, 1269.21),
        // Muted Guitar (26)
        new(26, 48, 50, 48, 65, 215.32),
        new(26, 51, 53, 52, 60, 209.10),
        new(26, 54, 57, 55, 68, 202.42),
        new(26, 58, 61, 60, 63, 183.73),
        new(26, 62, 65, 64, 74, 181.43),
        new(26, 66, 69, 67, 74, 173.25),
        new(26, 70, 73, 72, 68, 169.51),
        new(26, 74, 77, 76, 69, 164.39),
        new(26, 78, 81, 79, 70, 153.81),
        new(26, 82, 84, 84, 68, 154.05),
        // Special Guitar (28)
        new(28, 48, 53, 48, 11, 1596.15),
        new(28, 54, 60, 54, 0, 974.51),
        new(28, 61, 67, 61, 0, 1316.84),
        new(28, 68, 76, 68, 5, 1199.08),
        new(28, 77, 84, 77, 0, 2005.94),
    };

    private static readonly HashSet<uint> DynamicHoldInstruments = new()
    {
        5u, 6u, 7u, 8u, 9u,
        15u, 16u, 17u, 18u, 19u,
        20u, 21u, 22u, 23u, 24u, 25u, 27u,
    };

    public static bool IsDynamicHold(uint instrumentId)
        => DynamicHoldInstruments.Contains(instrumentId);

    public static double GetVoiceTotalMs(uint instrumentId, int midiNote, double heldSeconds)
    {
        if (DynamicHoldInstruments.Contains(instrumentId))
        {
            var heldMs = Math.Max(0.0, heldSeconds) * 1000.0;
            return heldSeconds * 1000.0 + Math.Clamp(heldMs, 300.0, 1000.0);
        }

        if (TryFindZone(instrumentId, midiNote, out var zone))
        {
            var ratio = Math.Pow(2.0, (midiNote - zone.RootKey) / 12.0);
            var audible = zone.AudibleMsAtRoot / ratio;
            return zone.AttackMs + audible;
        }

        return 2500.0;
    }

    private static bool TryFindZone(uint instrumentId, int midiNote, out OneShotZone result)
    {
        foreach (var zone in OneShotZones)
        {
            if (zone.InstrumentId == instrumentId && midiNote >= zone.KeyLow && midiNote <= zone.KeyHigh)
            {
                result = zone;
                return true;
            }
        }

        result = default;
        return false;
    }
}
