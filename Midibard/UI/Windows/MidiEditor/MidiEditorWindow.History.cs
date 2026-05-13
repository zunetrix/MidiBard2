using System;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void CaptureHistorySnapshot()
    {
        if (_file == null) return;
        _history.Capture(_file);
    }

    private bool ExecuteDirectEdit(Func<bool> edit)
    {
        if (_file == null)
            return false;

        return MidiEditorDirectEditExecutor.Execute(_history, _file, edit);
    }

    private void BeginGestureHistoryScope()
        => _gestureHistoryCaptured = false;

    private void CaptureHistorySnapshotForGesture()
    {
        if (_gestureHistoryCaptured) return;
        CaptureHistorySnapshot();
        _gestureHistoryCaptured = true;
    }

    private void EndGestureHistoryScope()
        => _gestureHistoryCaptured = false;

    private void UndoMidiEdit()
    {
        if (_file == null || !_history.Undo(_file)) return;
        ResetEditorAfterHistoryRestore();
    }

    private void RedoMidiEdit()
    {
        if (_file == null || !_history.Redo(_file)) return;
        ResetEditorAfterHistoryRestore();
    }

    private void ResetEditorAfterHistoryRestore()
    {
        SelectTrack(-1);
        _selectedTrackIndices.Clear();
        _selectedEventIndices.Clear();
        _globalTracksChecked = false;
        _globalEventsChecked = false;
        _editingEvent = null;
        _editingTrack = null;
        _editorDragMode = EditorDragMode.None;
        EndGestureHistoryScope();
        _preDragSnapshot.Clear();
        _noteHitList.Clear();
    }
}
