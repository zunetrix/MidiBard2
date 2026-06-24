namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public const string ConductorSetTempo =
        "Set the BPM at a specific position. Adds a tempo event to the conductor track. If a tempo event already exists at that tick, it is replaced.";

    public const string ConductorSetTimeSignature =
        "Set the time signature at a specific position. Adds a time signature event to the conductor track.";

    public const string ConductorTempoMarkers =
        "Show vertical markers at each tempo event in the conductor track, including the initial tempo at tick 0.";

    public const string ConductorTimeSigMarkers =
        "Show vertical markers at each time signature event in the conductor track.";

    public const string SelectAllLeft =
        "Select all notes at or before the click position in the current track.";

    public const string SelectAllRight =
        "Select all notes at or after the click position in the current track.";
}
