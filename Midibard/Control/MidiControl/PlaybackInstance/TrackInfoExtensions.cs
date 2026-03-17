namespace MidiBard;

public static class TrackInfoExtensions
{
    public static bool IsEnabled(this TrackInfo t, TrackStatus[] trackStatus)
        => trackStatus[t.Index].Enabled;

    public static bool IsPlaying(this TrackInfo t, int? soloedTrack, TrackStatus[] trackStatus)
        => soloedTrack is int solo ? solo == t.Index : t.IsEnabled(trackStatus);

    public static uint? InstrumentIdFromTrackName(this TrackInfo t, ushort? defaultId = null, bool overrideToDefault = false)
        => overrideToDefault && defaultId.HasValue
            ? defaultId
            : TrackInfo.GetInstrumentIdByName(t.TrackName, defaultId);

    public static uint? GuitarToneFromTrackName(this TrackInfo t, ushort? defaultId = null)
        => t.InstrumentIdFromTrackName(defaultId) - 24;
}
