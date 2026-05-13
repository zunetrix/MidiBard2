using Dalamud.Bindings.ImGui;

namespace MidiBard;

internal static class MidiEditorOperationHelp
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

    public const string Transpose =
        "Move selected track notes by semitone amount. Use the note range fields to affect only low, playable, or high note bands.";

    public const string TransposeCreateNew =
        "When enabled, writes transposed copies after the selected tracks and keeps the originals unchanged.";

    public const string Merge =
        "Merge selected tracks into a clone of the target track. Event options control whether non-note MIDI data is copied along with notes.";

    public const string MergeEvents =
        "Includes this MIDI event type from the selected source tracks in the merged output.";

    public const string MergeDeleteOriginal =
        "Deletes the selected source tracks after the merged track is created.";

    public const string Quantize =
        "Move selected notes toward a rhythmic grid. Strength controls how far notes move toward the target grid position.";

    public const string ChangeNoteLength =
        "Change the duration of notes whose current length falls inside the selected tick range. Useful for zero-length notes or very long notes from old editors or tab conversion.";

    public const string ChangeNoteLengthRange =
        "Only notes with a duration between the minimum and maximum tick values are changed.";

    public const string ChangeNoteLengthDeleteOriginal =
        "When enabled, replaces the selected tracks. When disabled, creates changed copies and keeps the originals.";

    public const string SetTrackProgram =
        "Set or add MIDI Program Change events on selected tracks. This can also rename tracks to match the selected FFXIV instrument or MIDI program.";

    public const string SetTrackProgramRename =
        "Renames selected tracks after changing the program so playback and assignment tools can infer the intended instrument.";

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

    public const string SplitToneRange =
        "Split selected tracks into in-range and out-of-range note tracks using the selected MIDI note bounds.";

    public const string SplitLengthRange =
        "Split selected tracks into in-range and out-of-range note tracks using each note's duration in ticks.";

    public const string SplitOverlappedNotes =
        "Creates new tracks that separate duplicate notes with the same MIDI note and start tick from non-overlapped notes.";

    public const string TrimOverlappedSustainedNotes =
        "Creates trimmed copies where sustained notes are shortened before later overlapping notes begin.";

    public const string ExtendNotesDuration =
        "Creates extended copies where notes stretch until the next note starts, optionally capped by a maximum duration.";

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

    public const string SplitNotesIntoTracksEvery =
        "How many consecutive notes go to the same generated track before moving to the next track.";

    public const string GeneratePitchBendNotes =
        "Convert pitch-bend movement into note segments using BardForge's -2 to +2 semitone mapping. Generated tracks do not keep Pitch Bend events.";

    public const string SplitDrumkit =
        "Split drum-channel notes into game drum tracks such as BassDrum, SnareDrum, Cymbal, Bongo, and Timpani.";

    public const string DrumTransposePreset =
        "Chooses the drum note mapping used when generated drum tracks are transposed into playable output notes.";

    public const string DrumAutoEdit =
        "Removes extra simultaneous drum hits from generated tracks, keeping the highest hit for each same-start group.";

    public const string DrumRestTrack =
        "Creates a Drumkit Rest track for drum notes that are not covered by the known drum mapping.";

    public const string DrumMoveSource =
        "Moves original drumkit tracks after generated tracks so the new playable parts stay grouped first.";

    public const string DisassembleDrumkit =
        "Split each distinct drum MIDI note from selected drumkit tracks into its own generated track.";

    public const string TransposeSingleNoteToDrum =
        "Use this on tracks that contain only one MIDI note value, such as a disassembled hand-clap track. Choose the target drum note instead of calculating a transpose amount.";

    public static void DrawDescription(string description)
    {
        ImGui.TextWrapped(description);
        ImGui.Spacing();
    }
}
