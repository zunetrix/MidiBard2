using System.Linq;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private EditorCommandContext CreateEditorCommandContext(bool requireFile = true)
    {
        SyncEditorCommandSessionState();
        return EditorCommandContext.Create(
            _editorCommandSession,
            CreateEditorCommandServices(),
            requireFile: requireFile);
    }

    private EditorQueryContext CreateEditorQueryContext()
    {
        SyncEditorCommandSessionState();
        return EditorQueryContext.Create(_editorCommandSession);
    }

    private PreviewQueryContext CreatePreviewQueryContext()
    {
        SyncEditorCommandSessionState();
        return new PreviewQueryContext(
            _editorCommandSession.Preview,
            _editorCommandSession.File,
            _editorCommandSession.Selection.CreateSnapshot(),
            EmptyEditorPreviewSettings.Instance,
            EmptyEditorPreviewInstrumentCatalog.Instance,
            default);
    }

    private PreviewCommandContext CreatePreviewCommandContext()
    {
        SyncEditorCommandSessionState();
        return new PreviewCommandContext(
            _editorCommandSession.Preview,
            _editorCommandSession.File,
            _editorCommandSession.Selection.CreateSnapshot(),
            EmptyEditorPreviewSettings.Instance,
            EmptyEditorPreviewInstrumentCatalog.Instance,
            EmptyEditorPreviewSoundPlayer.Instance,
            EmptyEditorPreviewScheduler.Instance,
            _playbackPreview,
            default);
    }

    private EditorCommandServices CreateEditorCommandServices()
        => new()
        {
            MidiMapProvider = CreateEditorMidiMapProvider(),
        };

    private IEditorMidiMapProvider CreateEditorMidiMapProvider()
        => new ConfigurationEditorMidiMapProvider(_plugin.Config.MidiForgeMaps);

    private void SyncEditorCommandSessionState()
    {
        _editorCommandSession.File = _file;
        _editorCommandSession.Selection.SelectedTrackIndex = _selectedTrackIndex;
        _editorCommandSession.Selection.SelectedTrackIndices.Clear();
        _editorCommandSession.Selection.SelectedTrackIndices.AddRange(_selectedTrackIndices.OrderBy(index => index));
        _editorCommandSession.Selection.SelectedEventIndices.Clear();
        _editorCommandSession.Selection.SelectedEventIndices.AddRange(_selectedEventIndices.OrderBy(index => index));
    }

    private void ApplyEditorCommandRefreshHints()
    {
        var hints = _editorCommandSession.PendingRefreshHints;

        if (hints.ClearSelectedTrack)
            _selectedTrackIndex = -1;

        if (hints.ClearTrackSelection)
        {
            _selectedTrackIndices.Clear();
            _globalTracksChecked = false;
        }

        if (hints.ClearEventSelection)
        {
            _selectedEventIndices.Clear();
            _globalEventsChecked = false;
        }

        if (hints.ReloadSelectedTrack
            && _file != null
            && _selectedTrackIndex >= 0
            && _selectedTrackIndex < _file.Tracks.Count)
        {
            _file.Tracks[_selectedTrackIndex].LoadEvents(_file.TempoMap);
        }

        _editorCommandSession.ClearRefreshHints();
    }

    private bool BeginEditorCommandGesture()
    {
        if (_file == null || _editorCommandExecutor.IsGestureActive)
            return false;

        _editorCommandExecutor.BeginGesture(CreateEditorCommandContext());
        return true;
    }

    private void CommitEditorCommandGesture()
    {
        if (_file != null && _editorCommandExecutor.IsGestureActive)
            _editorCommandExecutor.CommitGesture(CreateEditorCommandContext());

        ApplyEditorCommandRefreshHints();
    }

    private void CancelEditorCommandGesture()
        => _editorCommandExecutor.CancelGesture();

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
        CancelEditorCommandGesture();
        _preDragSnapshot.Clear();
        _noteHitList.Clear();
    }
}
