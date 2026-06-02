namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public const string OpenMidiFile =
        "Open MIDI file";

    public const string SaveMidiFile =
        "Save";

    public const string SaveMidiFileAs =
        "Save as";

    public const string Undo =
        "Undo";

    public const string Redo =
        "Redo";

    public const string InsertMeasures =
        "Insert empty measures at a given position, shifting all downstream events forward.";

    public const string DeleteMeasures =
        "Delete a range of measures, removing their events and shifting all downstream events backward.";
}
