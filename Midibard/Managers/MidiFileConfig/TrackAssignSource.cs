namespace MidiBard.Managers;

/// <summary>
/// Indicates which source was used to assign tracks to ensemble members for the currently loaded song.
/// </summary>
internal enum TrackAssignSource
{
    /// <summary>No song loaded, or source not yet determined.</summary>
    None,

    /// <summary>Track assignment loaded from the song's .json sidecar file.</summary>
    JsonFile,

    /// <summary>Track assignment applied from MidiBardDefaultPerformer.json.</summary>
    DefaultPerformer,

    /// <summary>Track assignment built from the configured track assignment rules.</summary>
    Rules,

    /// <summary>Fallback: track enabled/disabled state from the user's Config.TrackStatus.</summary>
    TrackStatus,
}
