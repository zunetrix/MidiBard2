using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;

namespace MidiBard.Control.MidiControl.Editing;

public sealed class MidiForgeHistory
{
    private const int DefaultCapacity = 100;

    private readonly int _capacity;
    private readonly Stack<MidiForgeHistorySnapshot> _undo = new();
    private readonly Stack<MidiForgeHistorySnapshot> _redo = new();

    public MidiForgeHistory(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Capture(EditableMidiFile file)
    {
        _undo.Push(CreateSnapshot(file));
        TrimUndoStack();
        _redo.Clear();
    }

    public bool Undo(EditableMidiFile file)
    {
        if (!CanUndo) return false;

        _redo.Push(CreateSnapshot(file));
        file.RestoreTrackSnapshot(_undo.Pop());
        return true;
    }

    public bool Redo(EditableMidiFile file)
    {
        if (!CanRedo) return false;

        _undo.Push(CreateSnapshot(file));
        file.RestoreTrackSnapshot(_redo.Pop());
        TrimUndoStack();
        return true;
    }

    private static MidiForgeHistorySnapshot CreateSnapshot(EditableMidiFile file)
        => new(file.CloneTrackChunksForSnapshot(), file.IsDirty);

    private void TrimUndoStack()
    {
        if (_undo.Count <= _capacity) return;

        var retained = _undo.Take(_capacity).Reverse().ToArray();
        _undo.Clear();
        foreach (var snapshot in retained)
            _undo.Push(snapshot);
    }
}

public sealed record MidiForgeHistorySnapshot(IReadOnlyList<TrackChunk> TrackChunks, bool IsDirty);
