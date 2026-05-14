namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public const string TrackSelectAll =
        "Select or deselect all tracks.";

    public const string ToggleAllTracksVisibility =
        "Toggle track visibility.";

    public const string TrackClearSelection =
        "Clear track selection.";

    public const string TrackDeleteSelected =
        "Hold Ctrl to delete selected tracks.";

    public const string TrackDragToReorder =
        "Drag to reorder.";

    public const string TrackChangeChannel =
        "Change track channel.";

    public const string TrackSaveName =
        "Save track name.";

    public const string TrackCancelNameEdit =
        "Cancel track name edit.";

    public const string TrackVisibleInPianoRoll =
        "Visible in piano roll.";

    public const string TrackHiddenInPianoRoll =
        "Hidden in piano roll.";

    public const string TrackEditName =
        "Edit track name.";

    public const string TrackDelete =
        "Hold Ctrl to delete this track.";

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

    public const string MergeRemoveDuplicateEqualNotes =
        "Removes duplicate notes with the same MIDI note number and start tick.";

    public const string MergeNoteTolerance =
        "When > 0 overlapping or adjacent same-pitch notes are merged\ninto a single longer note using DryWetMidi's native merger.";

    public const string Quantize =
        "Move selected notes toward a rhythmic grid. Strength controls how far notes move toward the target grid position.";

    public const string QuantizeStrength =
        "1.0 = fully snapped to grid, 0.5 = halfway, 0.0 = no change.";

    public const string QuantizePreserveNoteLength =
        "When quantizing Start, moves the NoteOff by the same delta so duration is preserved.";

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

    public const string SetTrackProgramReplaceAll =
        "When off, only the earliest Program Change event is updated. Tracks without one get a new event at tick 0.";

    public const string MergeSongSimultaneous =
        "All tracks from both files start at time 0.\nUse when the two files play together (ensemble parts).";

    public const string MergeSongSequential =
        "The imported file is placed after the current file ends.\nUse for medleys or song sections.";

    public const string MergeSongIgnoreTempo =
        "When enabled, uses this file's tempo map and ignores the imported file's tempo.\nRequired when the two files have different BPM/time signatures.";
}
