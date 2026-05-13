using System;

namespace MidiBard.Control.MidiControl.Editing;

internal static class MidiEditorDirectEditExecutor
{
    public static bool Execute(
        MidiForgeHistory history,
        EditableMidiFile file,
        Func<bool> edit)
    {
        var pendingHistory = history.BeginPendingCapture(file);
        var changed = edit();
        if (!changed)
            return false;

        if (file.Version == pendingHistory.Version)
            file.MarkChanged();

        return history.CommitPendingCapture(file, pendingHistory);
    }
}
