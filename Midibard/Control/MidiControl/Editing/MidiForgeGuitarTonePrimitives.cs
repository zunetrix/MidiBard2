using System;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

using MidiBard.Control.MidiControl;

namespace MidiBard.Control.MidiControl.Editing;

public static class MidiForgeGuitarTonePrimitives
{
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
        => GuitarToneProgramResolver.TryResolveToneFromProgram(programNumber, out tone);

    public static bool TryResolveToneFromInstrumentId(uint? instrumentId, out int tone)
        => GuitarToneProgramResolver.TryResolveToneFromInstrumentId(instrumentId, out tone);

    public static bool TryResolveProgramForTone(int tone, out SevenBitNumber programNumber)
        => GuitarToneProgramResolver.TryResolveProgramForTone(tone, out programNumber);

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
