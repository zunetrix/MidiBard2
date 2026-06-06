namespace MidiBard;

internal enum MidiEditorKeyboardAction
{
    None,
    TransposeOctaveUp,
    TransposeOctaveDown,
    MoveNotesLeft,
    MoveNotesRight,
    SelectAllNotes,
    CopySelectedNotes,
    PasteCopiedNotes,
    DeleteSelection,
    ClearSelection,
    DeselectAll,
}

internal readonly record struct MidiEditorKeyboardShortcutState(
    bool PianoRollFocused,
    bool TextInputActive,
    bool CtrlDown,
    bool ShiftDown,
    bool UpPressed,
    bool DownPressed,
    bool LeftPressed,
    bool RightPressed,
    bool APressed,
    bool CPressed,
    bool VPressed,
    bool DeletePressed,
    bool EscapePressed);

internal static class MidiEditorKeyboardShortcuts
{
    public static MidiEditorKeyboardAction Resolve(MidiEditorKeyboardShortcutState state)
    {
        if (!state.PianoRollFocused || state.TextInputActive)
            return MidiEditorKeyboardAction.None;

        if (state.CtrlDown)
        {
            if (state.CPressed)
                return MidiEditorKeyboardAction.CopySelectedNotes;
            if (state.VPressed)
                return MidiEditorKeyboardAction.PasteCopiedNotes;
            if (state.ShiftDown && state.APressed)
                return MidiEditorKeyboardAction.DeselectAll;
            if (state.APressed)
                return MidiEditorKeyboardAction.SelectAllNotes;
            if (state.UpPressed)
                return MidiEditorKeyboardAction.TransposeOctaveUp;
            if (state.DownPressed)
                return MidiEditorKeyboardAction.TransposeOctaveDown;
            if (state.LeftPressed)
                return MidiEditorKeyboardAction.MoveNotesLeft;
            if (state.RightPressed)
                return MidiEditorKeyboardAction.MoveNotesRight;

            return MidiEditorKeyboardAction.None;
        }

        if (state.DeletePressed)
            return MidiEditorKeyboardAction.DeleteSelection;
        if (state.EscapePressed)
            return MidiEditorKeyboardAction.ClearSelection;

        return MidiEditorKeyboardAction.None;
    }
}
