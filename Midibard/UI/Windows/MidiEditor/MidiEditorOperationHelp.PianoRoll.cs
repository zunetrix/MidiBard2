namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public const string PianoRollKeyboardShortcuts = """
        Keyboard shortcuts:
        Ctrl + A = Select all notes
        Ctrl + C / Ctrl + V = Copy or paste selected notes
        Ctrl + mouse selection = Select or deselect notes

        Ctrl + Up = Transpose selected notes up 12 semitones
        Ctrl + Down = Transpose selected notes down 12 semitones
        Ctrl + Left / Right = Move selected notes by the active grid

        Drag note body = Move selected notes
        Drag left or right edge = Resize selected notes
        Hold Shift while dragging = Keep timing fixed
        Hold Alt after drag starts = Keep pitch fixed

        Alt + left-click = Add note
        Alt + right-click = Delete note
        Delete = Delete selected notes
        """;

    public static string ClearNoteSelection(int count) =>
        $"Clear note selection ({count}).";

    public const string DeleteSelectedNotes =
        "Delete selected notes.";

    public const string PencilModeOn =
        "Pencil: ON - click in the piano roll to add notes.";

    public const string PencilModeOff =
        "Pencil: OFF";

    public const string PencilMode =
        """
        Left-click to add a note.
        Right-click to delete a note.
        Outside pencil mode, drag selected notes to move them.
        """;

    public const string PencilNoteSize =
        "Sets the note length for newly drawn notes.";

    public const string SnapToGridOn =
        "Snap to grid: ON";

    public const string SnapToGridOff =
        "Snap to grid: OFF";

    public const string PencilAutoTrimOn =
        "Auto-trim: ON - new notes are shortened before the next same-pitch note.";

    public const string PencilAutoTrimOff =
        "Auto-trim: OFF - notes are blocked when they would overlap.";
}
