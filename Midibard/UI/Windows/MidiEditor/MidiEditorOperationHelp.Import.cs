namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public const string OpenWithOptions =
        "Open a local MIDI file and optionally normalize it before editing. These options are useful for new or messy files; leave them conservative for already edited MIDI files.";

    public const string ImportFromUrl =
        "Download a MIDI or supported guitar tab from a direct URL, or resolve a MuseScore score URL when possible. MuseScore import is best-effort and depends on MuseScore page/API internals.";

    public const string ImportSplitTracksByChannel =
        "Creates separate tracks when one source track contains events for multiple MIDI channels.";

    public const string ImportSortTracks =
        "Moves conductor tracks first, then melody or vocal tracks, other instruments, and drum tracks.";

    public const string ImportOverwriteTrackNames =
        "Replaces existing performance-track names with inferred FFXIV instrument, MIDI program, or drum names. Leave off for files that were already edited.";

    public const string ImportRemoveNonLyricMetadata =
        "Removes nonessential copyright, marker, cue point, device name, and sequence number events while keeping lyrics available for LRC export.";

    public const string ImportRemoveLyricsAndText =
        "Removes MIDI lyric and text events. Leave this off if you want to export LRC lyrics after import.";

    public const string ImportRemoveSequencerSpecificEvents =
        "Removes sequencer-specific events such as editor helper data, mute state, and track color data from other tools.";

    public const string ImportOptimizeChannels =
        "Assigns non-drum performance tracks to compact MIDI channels while preserving shared channels for tracks with the same program.";

    public const string ImportTrimStart =
        "Removes blank time at the beginning of the song. Until first note starts playback at tick 0; remove empty bars keeps partial pickup timing.";
}
