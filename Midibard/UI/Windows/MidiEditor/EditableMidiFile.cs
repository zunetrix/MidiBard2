using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Extensions.DryWetMidi;

namespace MidiBard;

[Flags]
public enum MidiEventFilter
{
    Notes = 1 << 0,
    ProgramChange = 1 << 1,
    PitchBend = 1 << 2,
    Tempo = 1 << 3,
    Other = 1 << 4,
    All = Notes | ProgramChange | PitchBend | Tempo | Other
}

public class EditableMidiFile
{
    public MidiFile Source { get; }
    public TempoMap TempoMap { get; private set; }
    public List<EditableTrack> Tracks { get; } = new();
    public string? FilePath { get; set; }
    public string DisplayName { get; set; }
    public int Version { get; private set; }
    private bool _isDirty;

    public bool IsDirty => _isDirty;

    public void MarkChanged()
    {
        Version++;
        _isDirty = true;
    }

    public void MarkClean()
        => _isDirty = false;

    internal void SetDirtyStateForLoad(bool isDirty)
        => _isDirty = isDirty;

    public EditableMidiFile(MidiFile source, string? filePath = null, string? displayName = null)
    {
        Source = source;
        TempoMap = source.GetTempoMap();
        FilePath = filePath;
        DisplayName = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : Path.GetFileName(filePath ?? "untitled.mid");
        LoadTracks();
    }

    private void LoadTracks()
    {
        Tracks.Clear();
        // Keep only chunks that carry channel events (playable tracks)
        // or tempo/time-signature events (true conductor track).
        // Meta-only chunks (SequenceSpecific, PortPrefix, TrackName...) are silently dropped.
        var chunks = Source.GetTrackChunks()
            .Where(c => c.Events.OfType<ChannelEvent>().Any()
                     || c.Events.OfType<SetTempoEvent>().Any()
                     || c.Events.OfType<TimeSignatureEvent>().Any())
            .OrderBy(c => c.Events.OfType<ChannelEvent>().Any() ? 1 : 0) // conductor first
            .ToList();
        for (int i = 0; i < chunks.Count; i++)
            Tracks.Add(new EditableTrack(chunks[i], i));
    }

    internal TrackChunk[] CloneTrackChunksForSnapshot()
        => Tracks.Select(t => t.CloneCurrentChunk()).ToArray();

    internal void RestoreTrackSnapshot(Control.MidiControl.Editing.MidiForgeHistorySnapshot snapshot)
    {
        foreach (var track in Tracks)
            track.Dispose();

        Tracks.Clear();
        for (int i = 0; i < snapshot.TrackChunks.Count; i++)
            Tracks.Add(new EditableTrack(CloneTrackChunk(snapshot.TrackChunks[i]), i));

        RebuildSourceChunksFromTracks();
        TempoMap = Source.GetTempoMap();
        Version++;
        _isDirty = snapshot.IsDirty;
    }

    internal void RebuildSourceChunksFromTracks()
    {
        var nonTrackChunks = Source.Chunks.Where(c => c is not TrackChunk).ToList();
        Source.Chunks.Clear();
        foreach (var chunk in nonTrackChunks)
            Source.Chunks.Add(chunk);

        foreach (var track in Tracks)
        {
            if (!track.IsConductorTrack && !track.Chunk.Events.OfType<ChannelEvent>().Any()) continue;
            Source.Chunks.Add(track.Chunk);
        }
    }

    private static TrackChunk CloneTrackChunk(TrackChunk chunk)
        => new(chunk.Events.Select(e => e.Clone()));

    internal void FlushAllTracks()
    {
        foreach (var track in Tracks)
            track.FlushChanges();
    }

    internal void ReloadTracksFromSource()
    {
        foreach (var track in Tracks)
            track.Dispose();

        LoadTracks();
        TempoMap = Source.GetTempoMap();
    }

    public void Save()
    {
        if (FilePath == null) return;

        foreach (var t in Tracks) t.FlushChanges();

        RebuildSourceChunksFromTracks();

        using (var stream = File.Create(FilePath))
            Source.Write(stream);

        MarkClean();
    }

    public void SaveAs(string path)
    {
        FilePath = path;
        DisplayName = Path.GetFileName(path);
        Save();
    }
}

public class EditableTrack : IDisposable
{
    public int Index { get; set; }
    public TrackChunk Chunk { get; }
    public string Name { get; set; }
    public int Channel => ExtractChannel(Chunk);

    /// <summary>True when the track contains no channel events (tempo/time-sig only).</summary>
    public bool IsConductorTrack { get; }

    /// <summary>
    /// Display label: "Conductor Track" for conductor tracks; the track name if set;
    /// "(no name)" for normal tracks without a name.
    /// </summary>
    public string DisplayName => IsConductorTrack
        ? "Conductor Track"
        : string.IsNullOrEmpty(Name) ? "(no name)" : Name;

    /// <summary>True when the track has channel events on more than one MIDI channel.</summary>
    public bool HasMultipleChannels =>
        Chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Skip(1).Any();

    public List<EditableEvent>? Events { get; private set; }

    private TimedObjectsManager<TimedEvent>? _eventsManager;
    private bool _nameDirty;

    public EditableTrack(TrackChunk chunk, int index)
    {
        Chunk = chunk;
        Index = index;
        Name = ExtractName(chunk);
        IsConductorTrack = chunk.Events.Count > 0
                        && !chunk.Events.OfType<ChannelEvent>().Any();
    }

    public void MarkNameDirty() => _nameDirty = true;

    internal TrackChunk CloneCurrentChunk()
    {
        TrackChunk clone;

        if (_eventsManager != null)
        {
            clone = new TrackChunk();
            using var cloneManager = clone.ManageTimedEvents();
            foreach (var timedEvent in _eventsManager.Objects)
                cloneManager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), timedEvent.Time));
        }
        else
        {
            clone = new TrackChunk(Chunk.Events.Select(e => e.Clone()));
        }

        if (_nameDirty)
            ApplyNameToChunk(clone, Name);

        return clone;
    }

    public void LoadEvents(TempoMap tempoMap)
    {
        _eventsManager?.Dispose();
        _eventsManager = Chunk.ManageTimedEvents();

        var allTe = _eventsManager.Objects
            .Where(te => EditableEvent.IsRelevant(te.Event))
            .ToList();

        // Pair each NoteOn (vel>0) with its first matching NoteOff/NoteOn-vel0
        var usedAsNoteOff = new HashSet<TimedEvent>();
        var noteOffMap = new Dictionary<TimedEvent, TimedEvent>();

        for (int i = 0; i < allTe.Count; i++)
        {
            var te = allTe[i];
            if (te.Event is not NoteOnEvent noteOn || (byte)noteOn.Velocity == 0) continue;

            for (int j = i + 1; j < allTe.Count; j++)
            {
                var other = allTe[j];
                if (usedAsNoteOff.Contains(other)) continue;
                var oe = other.Event;
                if ((oe is NoteOffEvent nOff && nOff.NoteNumber == noteOn.NoteNumber && nOff.Channel == noteOn.Channel)
                 || (oe is NoteOnEvent nOn2 && (byte)nOn2.Velocity == 0
                     && nOn2.NoteNumber == noteOn.NoteNumber && nOn2.Channel == noteOn.Channel))
                {
                    noteOffMap[te] = other;
                    usedAsNoteOff.Add(other);
                    break;
                }
            }
        }

        // Build Events list: NoteOff peers are hidden but preserved in the manager
        Events = allTe
            .Where(te => !usedAsNoteOff.Contains(te))
            .Select(te =>
            {
                noteOffMap.TryGetValue(te, out var noteOff);
                return new EditableEvent(te, noteOff);
            })
            .ToList();
    }

    public void UnloadEvents()
    {
        FlushChanges();
        Events = null;
    }

    public void RemoveEvent(EditableEvent ev)
    {
        if (_eventsManager == null || Events == null) return;
        _eventsManager.Objects.Remove(ev.Source);
        if (ev.NoteOffSource != null)
            _eventsManager.Objects.Remove(ev.NoteOffSource);
        Events.Remove(ev);
    }

    public void SetChannel(int newChannel)
    {
        var ch = (FourBitNumber)(byte)Math.Clamp(newChannel, 0, 15);
        // Update raw chunk events (and manager objects share the same references)
        foreach (var ev in Chunk.Events.OfType<ChannelEvent>())
            ev.Channel = ch;
    }

    public EditableEvent? InsertNote(long tick, int noteNumber, int velocity, long durationTicks)
    {
        if (_eventsManager == null || Events == null) return null;
        var channel = (FourBitNumber)(byte)(Channel < 0 ? 0 : Channel & 0xF);
        var noteNum = (SevenBitNumber)(byte)Math.Clamp(noteNumber, 0, 127);
        var vel = (SevenBitNumber)(byte)Math.Clamp(velocity, 1, 127);
        long noteOnTick = Math.Max(0, tick);
        long noteOffTick = Math.Max(noteOnTick + 1, noteOnTick + durationTicks);
        var noteOnTe = new TimedEvent(new NoteOnEvent(noteNum, vel) { Channel = channel }, noteOnTick);
        var noteOffTe = new TimedEvent(new NoteOffEvent(noteNum, (SevenBitNumber)0) { Channel = channel }, noteOffTick);
        _eventsManager.Objects.Add(noteOnTe);
        _eventsManager.Objects.Add(noteOffTe);
        var ev = new EditableEvent(noteOnTe, noteOffTe);
        int insertIndex = Events.Count;
        for (int i = 0; i < Events.Count; i++)
            if (Events[i].Tick > noteOnTick) { insertIndex = i; break; }
        Events.Insert(insertIndex, ev);
        return ev;
    }

    public void FlushChanges()
    {
        _eventsManager?.Dispose();
        _eventsManager = null;

        if (_nameDirty)
        {
            ApplyNameToChunk();
            _nameDirty = false;
        }
    }

    public void Dispose()
    {
        FlushChanges();
    }

    private void ApplyNameToChunk()
        => ApplyNameToChunk(Chunk, Name);

    private static void ApplyNameToChunk(TrackChunk chunk, string name)
    {
        chunk.Events.RemoveAll(e => e is SequenceTrackNameEvent);
        if (!string.IsNullOrEmpty(name))
            chunk.Events.Insert(0, new SequenceTrackNameEvent(name));
    }

    private static string ExtractName(TrackChunk chunk)
        => chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text ?? string.Empty;

    private static int ExtractChannel(TrackChunk chunk)
        => chunk.Events.OfType<ChannelEvent>().FirstOrDefault() is { } ev ? (byte)ev.Channel : 0;
}

public class EditableEvent
{
    public TimedEvent Source { get; }
    public MidiEventFilter Category { get; }
    public string TypeName { get; }
    /// <summary>Paired NoteOff TimedEvent for Note rows; null for all other event types.</summary>
    public TimedEvent? NoteOffSource { get; internal set; }
    /// <summary>Duration in ticks, live from NoteOffSource when available.</summary>
    public long DurationTicks => NoteOffSource != null ? NoteOffSource.Time - Source.Time : 0;

    // Edit buffer – populated by RefreshEditValues, applied by ApplyEditValues
    public int EditTick;
    public int EditValue1;
    public int EditValue2;
    public int EditDuration; // Note rows only

    public EditableEvent(TimedEvent te, TimedEvent? noteOff = null)
    {
        Source = te;
        NoteOffSource = noteOff;
        var (type, cat) = Classify(te.Event);
        TypeName = noteOff != null ? "Note" : type;
        Category = cat;
        RefreshEditValues();
    }

    public long Tick
    {
        get => Source.Time;
        set => Source.Time = value;
    }

    public static bool IsRelevant(MidiEvent e)
        => e is not EndOfTrackEvent;

    public bool MatchesFilter(MidiEventFilter filter) => (Category & filter) != 0;

    public string GetValueDisplay() => Source.Event switch
    {
        NoteOnEvent n => $"{MidiForgeNotePrimitives.GetMidiNoteName(n.NoteNumber)} vel:{(byte)n.Velocity}",
        NoteOffEvent n => MidiForgeNotePrimitives.GetMidiNoteName(n.NoteNumber),
        ProgramChangeEvent p => $"[{(byte)p.ProgramNumber + 1}] {p.GetGMProgramName()}",
        ControlChangeEvent c => $"CC{(byte)c.ControlNumber} = {(byte)c.ControlValue}",
        PitchBendEvent p => $"{p.PitchValue}",
        SetTempoEvent t => $"{(int)(60_000_000.0 / t.MicrosecondsPerQuarterNote)} BPM",
        TimeSignatureEvent ts => $"{ts.Numerator}/{1 << ts.Denominator}",
        KeySignatureEvent ks => $"Key={ks.Key} ({ks.Scale})",
        BaseTextEvent bt => $"\"{bt.Text}\"",
        _ => ""
    };

    public void RefreshEditValues()
    {
        EditTick = (int)Source.Time;
        switch (Source.Event)
        {
            case NoteOnEvent n:
                EditValue1 = (byte)n.NoteNumber;
                EditValue2 = (byte)n.Velocity;
                EditDuration = (int)DurationTicks;
                break;
            case NoteOffEvent n: EditValue1 = (byte)n.NoteNumber; EditValue2 = 0; break;
            case ProgramChangeEvent p: EditValue1 = (byte)p.ProgramNumber; EditValue2 = 0; break;
            case PitchBendEvent pb: EditValue1 = pb.PitchValue; EditValue2 = 0; break;
            case SetTempoEvent t: EditValue1 = (int)(60_000_000.0 / t.MicrosecondsPerQuarterNote); EditValue2 = 0; break;
        }
    }

    public void ApplyEditValues()
    {
        Source.Time = Math.Max(0, EditTick);
        switch (Source.Event)
        {
            case NoteOnEvent n:
                n.NoteNumber = (SevenBitNumber)(byte)Math.Clamp(EditValue1, 0, 127);
                n.Velocity = (SevenBitNumber)(byte)Math.Clamp(EditValue2, 0, 127);
                if (NoteOffSource != null)
                {
                    NoteOffSource.Time = Source.Time + Math.Max(1, EditDuration);
                    var nn = (SevenBitNumber)(byte)Math.Clamp(EditValue1, 0, 127);
                    if (NoteOffSource.Event is NoteOffEvent nOff2) nOff2.NoteNumber = nn;
                    else if (NoteOffSource.Event is NoteOnEvent nOn0) nOn0.NoteNumber = nn;
                }
                break;
            case NoteOffEvent n: n.NoteNumber = (SevenBitNumber)(byte)Math.Clamp(EditValue1, 0, 127); break;
            case ProgramChangeEvent p: p.ProgramNumber = (SevenBitNumber)(byte)Math.Clamp(EditValue1, 0, 127); break;
            case PitchBendEvent pb: pb.PitchValue = (ushort)Math.Clamp(EditValue1, 0, 16383); break;
            case SetTempoEvent t: if (EditValue1 > 0) t.MicrosecondsPerQuarterNote = (long)(60_000_000.0 / EditValue1); break;
        }
    }

    public (string label1, string label2) GetEditLabels() => Source.Event switch
    {
        NoteOnEvent => ("Note (0-127)", "Velocity (0-127)"),
        NoteOffEvent => ("Note (0-127)", ""),
        ProgramChangeEvent => ("", ""),  // handled by combo
        PitchBendEvent => ("Value (0-16383)", ""),
        SetTempoEvent => ("BPM", ""),
        _ => ("", "")   // Other: only tick editing
    };

    private static (string type, MidiEventFilter cat) Classify(MidiEvent e) => e switch
    {
        NoteOnEvent => ("Note On", MidiEventFilter.Notes),
        NoteOffEvent => ("Note Off", MidiEventFilter.Notes),
        ProgramChangeEvent => ("Program Change", MidiEventFilter.ProgramChange),
        PitchBendEvent => ("Pitch Bend", MidiEventFilter.PitchBend),
        SetTempoEvent => ("Set Tempo", MidiEventFilter.Tempo),
        _ => (e.GetType().Name.Replace("Event", ""), MidiEventFilter.Other)
    };
}
