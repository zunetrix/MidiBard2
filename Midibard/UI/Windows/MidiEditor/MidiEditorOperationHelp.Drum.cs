namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
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

    public const string DisassembleDrumkitDeleteOriginal =
        "When enabled, deletes the selected drumkit source tracks after generated per-note tracks are created.";

    public const string TransposeSingleNoteToDrum =
        "Use this on tracks that contain only one MIDI note value, such as a disassembled hand-clap track. Choose the target drum note instead of calculating a transpose amount.";

    public const string TransposeToDrumTarget =
        "Choose the output drum note that all notes in the selected single-note tracks should become.";

    public const string TransposeToDrumTrackName =
        "Generated or replaced tracks are renamed to this value.";

    public const string TransposeToDrumDeleteOriginal =
        "When enabled, replaces the selected single-note tracks. When disabled, creates converted copies and keeps the originals.";
}
