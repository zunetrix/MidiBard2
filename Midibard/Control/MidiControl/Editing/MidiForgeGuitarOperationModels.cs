using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Editing;

public enum MidiForgeGuitarToneMergeChannelLayout
{
    SingleChannelToneSwitches,
    SeparateChannels,
}

public sealed record MidiForgeMergeGuitarToneTracksOptions(
    IReadOnlyDictionary<int, int> ToneByTrackIndex,
    bool DeleteOriginalTracks = false,
    string TrackName = "ProgramElectricGuitar",
    bool IncludePitchBendEvents = true,
    bool IncludeControlChangeEvents = true,
    MidiForgeGuitarToneMergeChannelLayout ChannelLayout = MidiForgeGuitarToneMergeChannelLayout.SingleChannelToneSwitches);

public sealed record MidiForgeMergeGuitarToneTracksResult(
    int SourceTracks,
    int CreatedTracks,
    int DeletedSourceTracks,
    int SkippedTracks,
    int GeneratedProgramChanges,
    int MergedNotes,
    int MergedChannelEvents);
