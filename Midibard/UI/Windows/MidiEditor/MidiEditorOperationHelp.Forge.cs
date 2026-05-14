namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public const string PrepareForPlayback =
        "Runs a conservative whole-file cleanup for raw MIDI: fills missing names, applies track-name transposes, splits drumkits, reduces chords, and fits notes into C3-C6.";

    public const string QuickPrepareForPlayback =
        "Runs the conservative prepare defaults immediately: fill empty names, apply track-name transposes, split drumkits, keep one chord note, and lower high notes into C3-C6.";

    public const string PrepareForPlaybackOptions =
        "The operation replaces generated source tracks and uses one undo step. Existing track names are kept unless they are empty.";

    public const string PrepareFillEmptyTrackNames =
        "Names unnamed tracks from their MIDI program so later operations can identify instruments.";

    public const string PrepareSplitDrumkits =
        "Splits drumkit tracks into playable note tracks before the final cleanup pass.";

    public const string AdaptToRange =
        "Adapt selected tracks into the playable C3-C6 range. The range fit option controls how notes are moved.";

    public const string RangeFitStrategy =
        "Move each note into range: fixes out-of-range notes one at a time.\n" +
        "Lower high notes first: lowers the whole track when notes are above C6, then fixes anything still outside C3-C6.\n" +
        "Find the best octave: tries an octave shift that keeps more notes naturally inside C3-C6 before fixing the rest.";

    public const string ApplyTrackNameTransposes =
        "Applies Midibard track-name octave transposes such as +1 or -1 to the MIDI notes, then removes those transpose markers from the track names.";

    public const string MergeGuitarToneTracks =
        "Combines selected guitar-tone tracks into one ProgramElectricGuitar track and writes Program Change events so tone switches travel with the MIDI.";

    public const string MergeGuitarToneDeleteOriginal =
        "When enabled, replaces the selected guitar tone tracks with the generated ProgramElectricGuitar track.";

    public const string CreateNewTracks =
        "When enabled, creates edited copies and keeps the original tracks. When disabled, replaces the selected tracks.";

    public const string AutoEdit =
        "Create playable edited tracks by choosing chord lines from simultaneous notes, then optionally fitting the result into C3-C6.";

    public const string AutoEditMaxSimultaneousNotes =
        "Limits how many notes from the same chord start are kept in the edited output.";

    public const string AutoEditPickStrategy =
        "Highest chord lines keeps the top chord parts. Odd chord lines favors alternating chord parts for denser chords.";

    public const string SplitChords =
        "Split chord notes from selected tracks into new tracks. Notes below the minimum simultaneous count are kept in a no-chords output track.";

    public const string ChordSplitStrategy =
        "Same start tick groups notes that begin together. Same start tick and length also requires matching duration.";

    public const string ChordGroupMode =
        "Merge by chord part creates one output per chord position. Individual separates chord size and part. Group whole chords by size keeps each chord size together.";

    public const string ChordMinimumSimultaneousNotes =
        "Only note groups with at least this many simultaneous notes are treated as chords.";

    public const string ChordInsertPartsAtEnd =
        "When enabled, split chord tracks are appended after existing tracks. When disabled, they are inserted after each source track.";

    public const string SplitToneRange =
        "Split selected tracks into in-range and out-of-range note tracks using the selected MIDI note bounds.";

    public const string SplitToneMinimumNote =
        "The lowest MIDI note number included in the in-range output track.";

    public const string SplitToneMaximumNote =
        "The highest MIDI note number included in the in-range output track.";

    public const string SplitLengthRange =
        "Split selected tracks into in-range and out-of-range note tracks using each note's duration in ticks.";

    public const string SplitLengthMinimumTicks =
        "The shortest note duration included in the in-range output track.";

    public const string SplitLengthMaximumTicks =
        "The longest note duration included in the in-range output track. Use 0 to match only zero-length notes.";

    public const string SplitOverlappedNotes =
        "Creates new tracks that separate duplicate notes with the same MIDI note and start tick from non-overlapped notes.";

    public const string TrimOverlappedSustainedNotes =
        "Creates trimmed copies where sustained notes are shortened before later overlapping notes begin.";

    public const string ExtendNotesDuration =
        "Creates extended copies where notes stretch until the next note starts, optionally capped by a maximum duration.";

    public const string ExtendNotesMaximumDuration =
        "Caps each extended note duration. Set to 0 to extend to the next note without a fixed maximum.";

    public const string RespectEmptyMeasures =
        "Prevents extensions from spilling into an empty following measure when the current measure can end cleanly.";

    public const string SplitEqualNotes =
        "Compare selected tracks and split the target track into notes that also appear in other selected tracks and notes that do not.";

    public const string DifferenceTracks =
        "Compare selected tracks and split the target track into notes that do not overlap any other selected track, plus the overlapping rest.";

    public const string TargetTrack =
        "The target track is the selected track that will be analyzed and split into new derived tracks.";

    public const string SplitNotesIntoTracks =
        "Distribute notes from each selected track across multiple generated tracks in round-robin order.";

    public const string SplitNotesIntoTracksCount =
        "How many generated tracks to distribute each source track's notes across.";

    public const string SplitNotesIntoTracksEvery =
        "How many consecutive notes go to the same generated track before moving to the next track.";

    public const string GeneratePitchBendNotes =
        "Convert pitch-bend movement into note segments using BardForge's -2 to +2 semitone mapping. Generated tracks do not keep Pitch Bend events.";

    public const string GeneratePitchBendDeleteOriginal =
        "When enabled, replaces the source tracks. When disabled, generated note-segment tracks are inserted after the source tracks.";
}
