using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

using MidiBard.Extensions.DryWetMidi;
using MidiBard.Util;

namespace MidiBard.Control.MidiControl.Editing;

internal static class MidiForgeTrackNaming
{
    public static string GetDefaultTrackName(
        TrackChunk chunk,
        int fallbackIndex,
        MidiForgeTrackNameFillMode fillMode = MidiForgeTrackNameFillMode.Ffxiv)
    {
        if (IsDrumTrack(chunk))
            return "Drumkit";

        var program = chunk.Events.OfType<ProgramChangeEvent>().FirstOrDefault()?.ProgramNumber;
        return program is { } programNumber
            ? GetTrackNameForProgram(programNumber, fillMode, fallbackIndex)
            : $"Track {fallbackIndex:00}";
    }

    public static string GetTrackNameForProgram(
        SevenBitNumber programNumber,
        MidiForgeTrackNameFillMode fillMode,
        int fallbackIndex)
    {
        if (fillMode == MidiForgeTrackNameFillMode.Ffxiv &&
            TryGetFfxivInstrumentName(programNumber, out var ffxivName))
            return ffxivName;

        var midiName = DryWetMidiExtensions.GetGMProgramName(programNumber);
        return string.IsNullOrWhiteSpace(midiName)
            ? $"Track {fallbackIndex:00}"
            : midiName;
    }

    private static bool TryGetFfxivInstrumentName(SevenBitNumber programNumber, out string trackName)
    {
        trackName = string.Empty;

        if (InstrumentHelper.ProgramInstruments == null ||
            InstrumentHelper.Instruments == null ||
            !InstrumentHelper.ProgramInstruments.TryGetValue(programNumber, out var instrumentId) ||
            instrumentId >= InstrumentHelper.Instruments.Length)
            return false;

        trackName = InstrumentHelper.Instruments[instrumentId].FFXIVDisplayName;
        return !string.IsNullOrWhiteSpace(trackName);
    }

    private static bool IsDrumTrack(TrackChunk chunk)
        => chunk.Events.OfType<ChannelEvent>().Any(e => (byte)e.Channel == MidiForgeAnalysis.DrumChannel);
}
