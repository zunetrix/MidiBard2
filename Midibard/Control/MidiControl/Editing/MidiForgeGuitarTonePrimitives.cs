using System;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

using MidiBard.Util;

namespace MidiBard.Control.MidiControl.Editing;

public static class MidiForgeGuitarTonePrimitives
{
    private static readonly int[] GuitarToneProgramFallbacks = [29, 27, 28, 30, 31];
    private static readonly int[] GuitarToneMergeChannels = Enumerable.Range(0, 16)
        .Where(channel => channel != MidiForgeAnalysis.DrumChannel)
        .ToArray();

    public static int MaximumMergeTracks => GuitarToneMergeChannels.Length;

    public static int GetMergeOutputChannel(int sourceIndex)
        => GuitarToneMergeChannels[sourceIndex];

    public static bool TryResolveToneFromTrackName(string trackName, out int tone)
    {
        var instrumentId = TrackInfo.GetInstrumentIdByName(trackName);
        return TryResolveToneFromInstrumentId(instrumentId, out tone);
    }

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

    public static bool ShouldMergeChannelEvent(
        MidiEvent midiEvent,
        MidiForgeMergeGuitarToneTracksOptions options)
        => midiEvent switch
        {
            PitchBendEvent => options.IncludePitchBendEvents,
            ControlChangeEvent => options.IncludeControlChangeEvents,
            _ => false,
        };
}
