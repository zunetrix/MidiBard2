using System;

using Melanchall.DryWetMidi.Common;

using MidiBard.Util;

namespace MidiBard.Control.MidiControl;

internal static class GuitarToneProgramResolver
{
    private static readonly int[] GuitarToneProgramFallbacks = [29, 27, 28, 30, 31];

    public static bool TryResolveToneFromProgram(SevenBitNumber programNumber, out int tone)
    {
        if (InstrumentHelper.ProgramInstruments != null &&
            InstrumentHelper.ProgramInstruments.TryGetValue(programNumber, out var instrumentId) &&
            TryResolveToneFromInstrumentId(instrumentId, out tone))
        {
            return true;
        }

        for (var i = 0; i < GuitarToneProgramFallbacks.Length; i++)
        {
            if ((byte)programNumber == GuitarToneProgramFallbacks[i])
            {
                tone = i;
                return true;
            }
        }

        tone = 0;
        return false;
    }

    public static bool TryResolveInstrumentFromProgram(SevenBitNumber programNumber, out uint instrumentId)
    {
        if (TryResolveToneFromProgram(programNumber, out var tone))
        {
            instrumentId = (uint)(24 + tone);
            return true;
        }

        instrumentId = 0;
        return false;
    }

    public static bool TryResolveToneFromInstrumentId(uint? instrumentId, out int tone)
    {
        tone = instrumentId switch
        {
            24 => 0,
            25 => 1,
            26 => 2,
            27 => 3,
            28 => 4,
            _ => -1,
        };

        return tone >= 0;
    }

    public static bool TryResolveProgramForTone(int tone, out SevenBitNumber programNumber)
    {
        tone = Math.Clamp(tone, 0, GuitarToneProgramFallbacks.Length - 1);
        var instrumentId = 24 + tone;

        if (InstrumentHelper.Instruments != null &&
            instrumentId >= 0 &&
            instrumentId < InstrumentHelper.Instruments.Length)
        {
            programNumber = InstrumentHelper.Instruments[instrumentId].ProgramNumber;
            return true;
        }

        programNumber = (SevenBitNumber)(byte)GuitarToneProgramFallbacks[tone];
        return true;
    }
}
