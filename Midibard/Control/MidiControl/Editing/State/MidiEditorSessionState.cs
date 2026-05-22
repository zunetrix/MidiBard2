using System.Collections.Generic;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;

namespace MidiBard.Control.MidiControl.Editing.State;

public sealed class MidiEditorSessionState
{
    public MidiEditorSessionState()
        : this(new MidiForgeHistory())
    {
    }

    public MidiEditorSessionState(MidiForgeHistory history)
    {
        History = history ?? throw new System.ArgumentNullException(nameof(history));
    }

    public EditableMidiFile File { get; set; }
    public MidiForgeHistory History { get; }
    public EditorSelectionState Selection { get; } = new();
    public EditorPopupStateStore PopupStates { get; } = new();
    public EditorNoteClipboard NoteClipboard { get; } = new();
    public PreviewSessionState Preview { get; } = new();
    public bool IsDirty { get; set; }
    public EditorRefreshHints PendingRefreshHints { get; private set; } = EditorRefreshHints.None;

    public void AddRefreshHints(EditorRefreshHints hints)
    {
        if (hints is null)
            return;

        PendingRefreshHints = PendingRefreshHints.Merge(hints);
    }

    public void ClearRefreshHints()
        => PendingRefreshHints = EditorRefreshHints.None;
}

public sealed class EditorNoteClipboard
{
    private IReadOnlyList<CopiedNote> notes = [];

    public IReadOnlyList<CopiedNote> Notes => notes;
    public bool HasNotes => notes.Count > 0;

    public void Set(IReadOnlyList<CopiedNote> copiedNotes)
        => notes = copiedNotes ?? [];

    public void Clear()
        => notes = [];
}

public sealed class EditorSelectionState
{
    public int SelectedTrackIndex { get; set; } = -1;
    public List<int> SelectedTrackIndices { get; } = new();
    public List<int> SelectedEventIndices { get; } = new();

    public EditorSelectionSnapshot CreateSnapshot()
        => new(
            SelectedTrackIndex,
            SelectedTrackIndices.ToArray(),
            SelectedEventIndices.ToArray());
}

public sealed record EditorSelectionSnapshot(
    int SelectedTrackIndex,
    IReadOnlyList<int> SelectedTrackIndices,
    IReadOnlyList<int> SelectedEventIndices);

public sealed class EditorPopupStateStore
{
    private readonly Dictionary<string, object> states = new();

    public int Count => states.Count;

    public TState GetOrCreate<TState>(string key)
        where TState : class, new()
        => GetOrCreate(key, static () => new TState());

    public TState GetOrCreate<TState>(string key, System.Func<TState> factory)
        where TState : class
    {
        ValidateKey(key);
        System.ArgumentNullException.ThrowIfNull(factory);

        if (states.TryGetValue(key, out var existing))
        {
            if (existing is TState typedExisting)
                return typedExisting;

            throw new System.InvalidOperationException(
                $"Popup state '{key}' is a {existing.GetType().FullName}, not a {typeof(TState).FullName}.");
        }

        var created = factory();
        states[key] = created ?? throw new System.InvalidOperationException(
            $"Popup state factory for '{key}' returned null.");
        return created;
    }

    public bool TryGet<TState>(string key, out TState state)
        where TState : class
    {
        ValidateKey(key);

        if (!states.TryGetValue(key, out var existing))
        {
            state = null;
            return false;
        }

        if (existing is TState typedExisting)
        {
            state = typedExisting;
            return true;
        }

        throw new System.InvalidOperationException(
            $"Popup state '{key}' is a {existing.GetType().FullName}, not a {typeof(TState).FullName}.");
    }

    public void Set<TState>(string key, TState state)
        where TState : class
    {
        ValidateKey(key);
        System.ArgumentNullException.ThrowIfNull(state);
        states[key] = state;
    }

    public bool Remove(string key)
    {
        ValidateKey(key);
        return states.Remove(key);
    }

    public void Clear()
        => states.Clear();

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new System.ArgumentException("Popup state key is required.", nameof(key));
    }
}

public sealed class PreviewSessionState
{
    public bool HasEvents { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public double PlaybackPositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public long PlaybackPositionTicks { get; set; }
    public int? FocusTrackIndex { get; set; }
    public object CurrentSnapshot { get; set; }
}
