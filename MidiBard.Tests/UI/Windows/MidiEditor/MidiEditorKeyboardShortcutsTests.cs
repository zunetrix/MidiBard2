namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class MidiEditorKeyboardShortcutsTests
{
    [Fact]
    public void Resolve_CtrlCMapsToCopy()
    {
        var action = MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, CPressed: true));

        action.ShouldBe(MidiEditorKeyboardAction.CopySelectedNotes);
    }

    [Fact]
    public void Resolve_CtrlVMapsOnlyToPaste()
    {
        var action = MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, VPressed: true));

        action.ShouldBe(MidiEditorKeyboardAction.PasteCopiedNotes);
        action.ShouldNotBe(MidiEditorKeyboardAction.SelectAllNotes);
        action.ShouldNotBe(MidiEditorKeyboardAction.MoveNotesLeft);
        action.ShouldNotBe(MidiEditorKeyboardAction.MoveNotesRight);
    }

    [Fact]
    public void Resolve_CtrlASelectsAllNotes()
    {
        var action = MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, APressed: true));

        action.ShouldBe(MidiEditorKeyboardAction.SelectAllNotes);
    }

    [Fact]
    public void Resolve_CtrlUpDownTransposeByOctave()
    {
        MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, UpPressed: true))
            .ShouldBe(MidiEditorKeyboardAction.TransposeOctaveUp);
        MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, DownPressed: true))
            .ShouldBe(MidiEditorKeyboardAction.TransposeOctaveDown);
    }

    [Fact]
    public void Resolve_CtrlLeftRightMoveByGrid()
    {
        MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, LeftPressed: true))
            .ShouldBe(MidiEditorKeyboardAction.MoveNotesLeft);
        MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, RightPressed: true))
            .ShouldBe(MidiEditorKeyboardAction.MoveNotesRight);
    }

    [Fact]
    public void Resolve_IgnoresShortcutWhenPianoRollIsNotFocused()
    {
        var action = MidiEditorKeyboardShortcuts.Resolve(Focused(
            PianoRollFocused: false,
            CtrlDown: true,
            CPressed: true));

        action.ShouldBe(MidiEditorKeyboardAction.None);
    }

    [Fact]
    public void Resolve_IgnoresShortcutDuringTextInput()
    {
        var action = MidiEditorKeyboardShortcuts.Resolve(Focused(
            TextInputActive: true,
            CtrlDown: true,
            VPressed: true));

        action.ShouldBe(MidiEditorKeyboardAction.None);
    }

    [Fact]
    public void Resolve_DeleteAndEscapeRequireNoCtrl()
    {
        MidiEditorKeyboardShortcuts.Resolve(Focused(DeletePressed: true))
            .ShouldBe(MidiEditorKeyboardAction.DeleteSelection);
        MidiEditorKeyboardShortcuts.Resolve(Focused(EscapePressed: true))
            .ShouldBe(MidiEditorKeyboardAction.ClearSelection);
        MidiEditorKeyboardShortcuts.Resolve(Focused(CtrlDown: true, DeletePressed: true))
            .ShouldBe(MidiEditorKeyboardAction.None);
    }

    [Fact]
    public void Resolve_CtrlShiftADeselectsAll()
    {
        MidiEditorKeyboardShortcuts
            .Resolve(Focused(CtrlDown: true, ShiftDown: true, APressed: true))
            .ShouldBe(MidiEditorKeyboardAction.DeselectAll);
    }

    [Fact]
    public void Resolve_CtrlAStillSelectsAllWithoutShift()
    {
        MidiEditorKeyboardShortcuts
            .Resolve(Focused(CtrlDown: true, APressed: true))
            .ShouldBe(MidiEditorKeyboardAction.SelectAllNotes);
    }

    private static MidiEditorKeyboardShortcutState Focused(
        bool PianoRollFocused = true,
        bool TextInputActive = false,
        bool CtrlDown = false,
        bool ShiftDown = false,
        bool UpPressed = false,
        bool DownPressed = false,
        bool LeftPressed = false,
        bool RightPressed = false,
        bool APressed = false,
        bool CPressed = false,
        bool VPressed = false,
        bool DeletePressed = false,
        bool EscapePressed = false)
        => new(
            PianoRollFocused,
            TextInputActive,
            CtrlDown,
            ShiftDown,
            UpPressed,
            DownPressed,
            LeftPressed,
            RightPressed,
            APressed,
            CPressed,
            VPressed,
            DeletePressed,
            EscapePressed);
}
