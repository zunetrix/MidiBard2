using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

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
    public TempoMap TempoMap { get; }
    public List<EditableTrack> Tracks { get; } = new();
    public string? FilePath { get; set; }
    public int Version { get; private set; }
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set { if (value) Version++; _isDirty = value; }
    }

    public EditableMidiFile(MidiFile source, string? filePath = null)
    {
        Source = source;
        TempoMap = source.GetTempoMap();
        FilePath = filePath;
        LoadTracks();
    }

    private void LoadTracks()
    {
        Tracks.Clear();
        // Keep only chunks that carry channel events (playable tracks)
        // or tempo/time-signature events (true conductor track).
        // Meta-only chunks (SequenceSpecific, PortPrefix, TrackName…) are silently dropped.
        var chunks = Source.GetTrackChunks()
            .Where(c => c.Events.OfType<ChannelEvent>().Any()
                     || c.Events.OfType<SetTempoEvent>().Any()
                     || c.Events.OfType<TimeSignatureEvent>().Any())
            .OrderBy(c => c.Events.OfType<ChannelEvent>().Any() ? 1 : 0) // conductor first
            .ToList();
        for (int i = 0; i < chunks.Count; i++)
            Tracks.Add(new EditableTrack(chunks[i], i));
    }

    public void MoveTrack(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex
            || fromIndex < 0 || toIndex < 0
            || fromIndex >= Tracks.Count || toIndex >= Tracks.Count)
            return;

        var track = Tracks[fromIndex];
        Tracks.RemoveAt(fromIndex);
        Tracks.Insert(toIndex, track);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;

        IsDirty = true;
    }

    public void RemoveTrack(int index)
    {
        if (index < 0 || index >= Tracks.Count) return;

        Tracks[index].Dispose();
        Tracks.RemoveAt(index);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;

        IsDirty = true;
    }

    public void CloneTrack(int index)
    {
        if (index < 0 || index >= Tracks.Count) return;

        var source = Tracks[index];
        source.FlushChanges(); // write in-memory edits back to chunk

        var cloneChunk = new TrackChunk(source.Chunk.Events.Select(e => e.Clone()));
        var newTrack = new EditableTrack(cloneChunk, index + 1);

        Tracks.Insert(index + 1, newTrack);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;

        IsDirty = true;
    }

    public void ConsolidateTempoToConductorTrack()
    {
        // Flush any in-memory edits first
        foreach (var t in Tracks) t.FlushChanges();

        var conductor = Tracks.FirstOrDefault(t => t.IsConductorTrack);

        if (conductor == null)
        {
            // No conductor track - only create one when there are tempo events to move out
            bool hasTempoEvents = Tracks.Any(t =>
                t.Chunk.Events.OfType<SetTempoEvent>().Any());
            if (!hasTempoEvents) return;

            var conductorChunk = new TrackChunk();
            conductor = new EditableTrack(conductorChunk, 0);
            Tracks.Insert(0, conductor);
            for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;
        }

        using var conductorMgr = conductor.Chunk.ManageTimedEvents();

        foreach (var track in Tracks)
        {
            if (ReferenceEquals(track, conductor)) continue;

            using var trackMgr = track.Chunk.ManageTimedEvents();
            var toMove = trackMgr.Objects.Where(te => te.Event is SetTempoEvent).ToList();
            foreach (var te in toMove)
            {
                trackMgr.Objects.Remove(te);
                conductorMgr.Objects.Add(te);
            }
        }

        IsDirty = true;
    }

    public void SplitTrackByChannel(int trackIndex)
    {
        var track = Tracks[trackIndex];
        if (track.IsConductorTrack) return;

        track.FlushChanges();

        using var mgr = track.Chunk.ManageTimedEvents();

        var channelGroups = mgr.Objects
            .Where(te => te.Event is ChannelEvent)
            .GroupBy(te => (byte)((ChannelEvent)te.Event).Channel)
            .OrderBy(g => g.Key)
            .ToList();

        if (channelGroups.Count <= 1) return;

        var nonChannelEvents = mgr.Objects
            .Where(te => te.Event is not ChannelEvent)
            .ToList();

        var newTracks = new List<EditableTrack>();

        // Conductor chunk (non-channel events: tempo, time-sig…) - inserted first
        if (nonChannelEvents.Any())
        {
            var conductorChunk = new TrackChunk();
            {
                using var cm = conductorChunk.ManageTimedEvents();
                foreach (var te in nonChannelEvents)
                    cm.Objects.Add(new TimedEvent(te.Event.Clone(), te.Time));
            } // cm flushed to conductorChunk before EditableTrack reads it
            newTracks.Add(new EditableTrack(conductorChunk, 0));
        }

        // Channel assignment: same program → same output channel; channel 9 reserved for drums
        const byte DrumChannel = 9;
        var programToChannel = new Dictionary<byte, byte>();
        var regularChannels = Enumerable.Range(0, 16)
            .Where(c => c != DrumChannel)
            .Select(c => (byte)c)
            .ToList();
        int channelCursor = 0;

        foreach (var grp in channelGroups)
        {
            byte origChannel = grp.Key;
            var groupEvents = grp.OrderBy(te => te.Time).ToList();

            // Find first ProgramChange for channel assignment and naming
            byte programNumber = 0;
            bool hasProgramChange = false;
            foreach (var te in groupEvents)
            {
                if (te.Event is ProgramChangeEvent pc)
                {
                    programNumber = (byte)pc.ProgramNumber;
                    hasProgramChange = true;
                    break;
                }
            }

            byte outChannel;
            string trackName;

            if (origChannel == DrumChannel)
            {
                outChannel = DrumChannel;
                trackName = "Drumkit";
            }
            else
            {
                if (hasProgramChange && programToChannel.TryGetValue(programNumber, out var existing))
                {
                    outChannel = existing;
                }
                else
                {
                    outChannel = regularChannels[channelCursor % regularChannels.Count];
                    channelCursor++;
                    if (hasProgramChange)
                        programToChannel[programNumber] = outChannel;
                }
                var gmName = DryWetMidiExtensions.GetGMProgramName(programNumber);
                trackName = string.IsNullOrEmpty(gmName) ? string.Empty : gmName;
            }

            var chunk = new TrackChunk();
            {
                using var cm = chunk.ManageTimedEvents();
                foreach (var te in groupEvents)
                {
                    var cloned = te.Event.Clone();
                    if (cloned is ChannelEvent ce)
                        ce.Channel = (FourBitNumber)(byte)outChannel;
                    cm.Objects.Add(new TimedEvent(cloned, te.Time));
                }
            } // cm flushed to chunk before EditableTrack reads it

            var newTrack = new EditableTrack(chunk, 0);
            if (!string.IsNullOrEmpty(trackName))
            {
                newTrack.Name = trackName;
                newTrack.MarkNameDirty();
            }
            newTracks.Add(newTrack);
        }

        Tracks.RemoveAt(trackIndex);
        for (int i = 0; i < newTracks.Count; i++)
            Tracks.Insert(trackIndex + i, newTracks[i]);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;

        // Consolidate tempo events into the newly created conductor track
        ConsolidateTempoToConductorTrack();

        IsDirty = true;
    }

    /// <summary>
    /// Imports note-bearing, non-conductor tracks from <paramref name="importedFile"/> into this file.
    /// The imported file's conductor track is skipped to avoid TempoMap conflicts.
    /// Ticks are scaled when the two files have different PPQ values.
    /// Channels are remapped: same GM program → same channel as existing tracks where possible.
    /// Returns the number of tracks added.
    /// </summary>
    private static void ConsolidateTempoInFile(MidiFile file)
    {
        var chunks = file.GetTrackChunks().ToList();
        // Conductor = first chunk with no channel events; fall back to first chunk
        var conductorChunk = chunks.FirstOrDefault(c => c.Events.Count > 0 && !c.Events.OfType<ChannelEvent>().Any())
                          ?? chunks.FirstOrDefault();
        if (conductorChunk == null) return;

        using var conductorMgr = conductorChunk.ManageTimedEvents();
        foreach (var chunk in chunks)
        {
            if (chunk == conductorChunk) continue;
            using var trackMgr = chunk.ManageTimedEvents();
            var tempoEvents = trackMgr.Objects.Where(te => te.Event is SetTempoEvent).ToList();
            foreach (var te in tempoEvents)
            {
                trackMgr.Objects.Remove(te);
                conductorMgr.Objects.Add(te);
            }
        }
    }

    public int ImportTracksFromFile(MidiFile importedFile)
    {
        // Move tempo events from all tracks into the conductor before evaluating
        ConsolidateTempoInFile(importedFile);

        // Scale ticks if PPQ differs between the two files
        int currentPPQ = Source.TimeDivision is TicksPerQuarterNoteTimeDivision td1 ? td1.TicksPerQuarterNote : 480;
        int importedPPQ = importedFile.TimeDivision is TicksPerQuarterNoteTimeDivision td2 ? td2.TicksPerQuarterNote : 480;
        double tickScale = (double)currentPPQ / importedPPQ;

        // Build program→channel map from existing tracks so we reuse channels for same instruments
        const byte DrumChannel = 9;
        var programToChannel = new Dictionary<byte, byte>();
        var usedChannels = new HashSet<byte>();
        foreach (var t in Tracks)
        {
            if (t.IsConductorTrack) continue;
            usedChannels.Add((byte)t.Channel);
            foreach (var ev in t.Chunk.Events)
            {
                if (ev is ProgramChangeEvent pc)
                {
                    var prog = (byte)pc.ProgramNumber;
                    if (!programToChannel.ContainsKey(prog))
                        programToChannel[prog] = (byte)t.Channel;
                    break;
                }
            }
        }

        var regularChannels = Enumerable.Range(0, 16)
            .Where(c => c != DrumChannel).Select(c => (byte)c).ToList();
        int channelCursor = 0;
        int importedCount = 0;

        foreach (var chunk in importedFile.GetTrackChunks())
        {
            // Skip tracks that have no real note events (conductor, meta-only, SequenceSpecific, PortPrefix, etc.)
            var noteOns = chunk.Events.OfType<NoteOnEvent>().Where(n => (byte)n.Velocity > 0).ToList();
            if (noteOns.Count == 0) continue;

            var channelEvents = chunk.Events.OfType<ChannelEvent>().ToList();
            byte origChannel = (byte)channelEvents[0].Channel;

            // Find first ProgramChange for instrument identification
            byte programNumber = 0;
            bool hasProgramChange = false;
            foreach (var ev in chunk.Events)
            {
                if (ev is ProgramChangeEvent pc)
                {
                    programNumber = (byte)pc.ProgramNumber;
                    hasProgramChange = true;
                    break;
                }
            }

            // Determine output channel
            byte outChannel;
            if (origChannel == DrumChannel)
            {
                outChannel = DrumChannel;
            }
            else if (hasProgramChange && programToChannel.TryGetValue(programNumber, out var matched))
            {
                outChannel = matched;
            }
            else
            {
                // Pick next unused regular channel, cycling when all are taken
                outChannel = regularChannels[channelCursor % regularChannels.Count];
                for (int a = 0; a < regularChannels.Count; a++)
                {
                    var candidate = regularChannels[(channelCursor + a) % regularChannels.Count];
                    if (!usedChannels.Contains(candidate)) { outChannel = candidate; break; }
                }
                channelCursor++;
                if (hasProgramChange) programToChannel[programNumber] = outChannel;
            }
            usedChannels.Add(outChannel);

            // Build new chunk: clone events, remap channel, scale ticks
            var newChunk = new TrackChunk();
            using (var dstMgr = newChunk.ManageTimedEvents())
            {
                foreach (var te in chunk.GetTimedEvents())
                {
                    var cloned = te.Event.Clone();
                    if (cloned is ChannelEvent ce)
                        ce.Channel = (FourBitNumber)(byte)outChannel;
                    long tick = tickScale == 1.0 ? te.Time : (long)Math.Round(te.Time * tickScale);
                    dstMgr.Objects.Add(new TimedEvent(cloned, Math.Max(0, tick)));
                }
            }

            var newTrack = new EditableTrack(newChunk, Tracks.Count);

            // Use GM program name as fallback when the track has no embedded name
            if (string.IsNullOrEmpty(newTrack.Name))
            {
                var fallbackName = origChannel == DrumChannel ? "Drumkit"
                    : hasProgramChange ? DryWetMidiExtensions.GetGMProgramName(programNumber)
                    : string.Empty;
                if (!string.IsNullOrEmpty(fallbackName))
                {
                    newTrack.Name = fallbackName;
                    newTrack.MarkNameDirty();
                }
            }

            Tracks.Add(newTrack);
            importedCount++;
        }

        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;
        if (importedCount > 0) IsDirty = true;
        return importedCount;
    }

    /// <summary>Shifts all note numbers in the given tracks by <paramref name="semitones"/>.</summary>
    public void TransposeTracks(IEnumerable<int> trackIndices, int semitones)
    {
        if (semitones == 0) return;
        foreach (var idx in trackIndices)
        {
            if (idx < 0 || idx >= Tracks.Count) continue;
            var t = Tracks[idx];
            if (t.IsConductorTrack) continue;
            t.FlushChanges();
            foreach (var ev in t.Chunk.Events)
            {
                if (ev is NoteOnEvent noteOn)
                    noteOn.NoteNumber = (SevenBitNumber)(byte)Math.Clamp((int)(byte)noteOn.NoteNumber + semitones, 0, 127);
                else if (ev is NoteOffEvent noteOff)
                    noteOff.NoteNumber = (SevenBitNumber)(byte)Math.Clamp((int)(byte)noteOff.NoteNumber + semitones, 0, 127);
            }
        }
        IsDirty = true;
    }

    /// <summary>
    /// Clones the target track and merges events from the other selected tracks into it,
    /// skipping notes that overlap with existing notes in the target.
    /// The merged clone is inserted after the target. Returns the new track index, or -1 on failure.
    /// </summary>
    public int MergeTracks(int targetIdx, IEnumerable<int> allSelectedIndices,
        bool includeProgramChange, bool includePitchBend, int toleranceMs = 0)
    {
        if (targetIdx < 0 || targetIdx >= Tracks.Count) return -1;
        var target = Tracks[targetIdx];
        if (target.IsConductorTrack) return -1;
        target.FlushChanges();

        var cloneChunk = new TrackChunk(target.Chunk.Events.Select(e => e.Clone()));
        var existingNotes = cloneChunk.GetNotes().Select(n => (n.Time, n.EndTime)).ToList();
        {
            using var cloneMgr = cloneChunk.ManageTimedEvents();

            foreach (var srcIdx in allSelectedIndices.Where(i => i != targetIdx))
            {
                if (srcIdx < 0 || srcIdx >= Tracks.Count) continue;
                var src = Tracks[srcIdx];
                if (src.IsConductorTrack) continue;
                src.FlushChanges();

                // Add non-overlapping notes using GetNotes() (handles NoteOn/NoteOff pairing correctly in DryWetMidi 8.x)
                foreach (var n in src.Chunk.GetNotes())
                {
                    long start = n.Time, end = n.EndTime;
                    if (IsOverlapping(existingNotes, start, end)) continue;
                    cloneMgr.Objects.Add(new TimedEvent(
                        new NoteOnEvent(n.NoteNumber, n.Velocity) { Channel = n.Channel }, start));
                    cloneMgr.Objects.Add(new TimedEvent(
                        new NoteOffEvent(n.NoteNumber, n.OffVelocity) { Channel = n.Channel }, end));
                    existingNotes.Add((start, end));
                }

                // Add non-note channel events per flags
                foreach (var te in src.Chunk.GetTimedEvents())
                {
                    if (te.Event is NoteOnEvent || te.Event is NoteOffEvent) continue;
                    if (te.Event is not ChannelEvent) continue;
                    if (te.Event is ProgramChangeEvent && !includeProgramChange) continue;
                    if (te.Event is PitchBendEvent && !includePitchBend) continue;
                    cloneMgr.Objects.Add(new TimedEvent(te.Event.Clone(), te.Time));
                }
            }
        } // cloneMgr disposed → writes back to cloneChunk

        // Optionally merge overlapping/adjacent same-pitch notes using the native DryWetMidi merger
        if (toleranceMs > 0)
        {
            Merger.MergeObjects(cloneChunk, ObjectType.Note, TempoMap,
                new ObjectsMergingSettings { Tolerance = new MetricTimeSpan(toleranceMs * 1_000L) });
        }

        var newTrack = new EditableTrack(cloneChunk, targetIdx + 1);
        newTrack.Name = $"{target.DisplayName} (merged)";
        newTrack.MarkNameDirty();
        Tracks.Insert(targetIdx + 1, newTrack);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;
        IsDirty = true;
        return targetIdx + 1;
    }

    /// <summary>
    /// If the file has more than one conductor track (e.g. after merging two files), consolidates
    /// all conductor track events into the first one and removes the extras.
    /// </summary>
    public void MergeMultipleConductorTracks()
    {
        var conductorTracks = Tracks.Where(t => t.IsConductorTrack).ToList();
        if (conductorTracks.Count <= 1) return;

        var primary = conductorTracks[0];
        primary.FlushChanges();

        using (var mgr = primary.Chunk.ManageTimedEvents())
        {
            foreach (var extra in conductorTracks.Skip(1))
            {
                extra.FlushChanges();
                foreach (var te in extra.Chunk.GetTimedEvents())
                    mgr.Objects.Add(new TimedEvent(te.Event.Clone(), te.Time));
            }
        } // mgr disposed → writes back to primary.Chunk

        foreach (var extra in conductorTracks.Skip(1))
        {
            extra.Dispose();
            Tracks.Remove(extra);
        }
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;
        IsDirty = true;
    }

    /// <summary>
    /// Quantizes notes in the given tracks using the DryWetMidi native quantizer.
    /// If <paramref name="toNewTrack"/> is true, a new quantized track is inserted after each source track.
    /// </summary>
    public void QuantizeTracks(IEnumerable<int> trackIndices, IGrid grid, QuantizingSettings settings, bool toNewTrack)
    {
        foreach (var idx in trackIndices.OrderByDescending(i => i).ToList())
        {
            if (idx < 0 || idx >= Tracks.Count) continue;
            var t = Tracks[idx];
            if (t.IsConductorTrack) continue;
            t.FlushChanges();

            var targetChunk = toNewTrack
                ? new TrackChunk(t.Chunk.Events.Select(e => e.Clone()))
                : t.Chunk;

            QuantizerUtilities.QuantizeObjects(targetChunk, ObjectType.Note, grid, TempoMap, settings);

            if (toNewTrack)
            {
                var newTrack = new EditableTrack(targetChunk, idx + 1);
                newTrack.Name = $"{t.DisplayName} (quantized)";
                newTrack.MarkNameDirty();
                Tracks.Insert(idx + 1, newTrack);
            }
        }
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].Index = i;
        IsDirty = true;
    }

    /// <summary>
    /// Quantizes only the specified notes (identified by tick + noteNumber + channel) in the given track.
    /// </summary>
    public void QuantizeNotes(int trackIndex,
        HashSet<(long tick, byte noteNum, byte channel)> selectedKeys,
        IGrid grid, QuantizingSettings baseSettings)
    {
        if (trackIndex < 0 || trackIndex >= Tracks.Count) return;
        var t = Tracks[trackIndex];
        if (t.IsConductorTrack) return;
        t.FlushChanges();

        var settings = new QuantizingSettings
        {
            Target = baseSettings.Target,
            QuantizingLevel = baseSettings.QuantizingLevel,
            FixOppositeEnd = baseSettings.FixOppositeEnd,
            QuantizingBeyondZeroPolicy = baseSettings.QuantizingBeyondZeroPolicy,
            QuantizingBeyondFixedEndPolicy = baseSettings.QuantizingBeyondFixedEndPolicy,
            Filter = obj => obj is Note note
                && selectedKeys.Contains((note.Time, (byte)note.NoteNumber, (byte)note.Channel)),
        };

        QuantizerUtilities.QuantizeObjects(t.Chunk, ObjectType.Note, grid, TempoMap, settings);
        IsDirty = true;
    }

    /// <summary>
    /// Sanitizes the MIDI file using DryWetMidi's native Sanitizer and reloads all tracks.
    /// </summary>
    public void SanitizeFile(SanitizingSettings settings)
    {
        // Flush edits so Source reflects the current state
        foreach (var t in Tracks) t.FlushChanges();

        // Rebuild Source.Chunks to match current edited track order
        var nonTrackChunks = Source.Chunks.Where(c => c is not TrackChunk).ToList();
        Source.Chunks.Clear();
        foreach (var c in nonTrackChunks) Source.Chunks.Add(c);
        foreach (var t in Tracks)
        {
            if (!t.IsConductorTrack && !t.Chunk.Events.OfType<ChannelEvent>().Any()) continue;
            Source.Chunks.Add(t.Chunk);
        }

        Sanitizer.Sanitize(Source, settings);

        // Dispose existing tracks and reload from sanitized source
        foreach (var t in Tracks) t.Dispose();
        LoadTracks();
        IsDirty = true;
    }

    private static bool IsOverlapping(List<(long start, long end)> ranges, long start, long end)
    {
        foreach (var (rs, re) in ranges)
            if (start < re && rs < end) return true;
        return false;
    }

    public void Save()
    {
        if (FilePath == null) return;

        foreach (var t in Tracks) t.FlushChanges();

        // Rebuild chunk order: keep non-track chunks first, then tracks in edited order
        var nonTrackChunks = Source.Chunks.Where(c => c is not TrackChunk).ToList();
        Source.Chunks.Clear();
        foreach (var c in nonTrackChunks) Source.Chunks.Add(c);
        foreach (var t in Tracks)
        {
            // Skip non-conductor tracks that contain no channel events (empty tracks)
            if (!t.IsConductorTrack && !t.Chunk.Events.OfType<ChannelEvent>().Any()) continue;
            Source.Chunks.Add(t.Chunk);
        }

        using (var stream = File.Create(FilePath))
            Source.Write(stream);

        IsDirty = false;
    }

    public void SaveAs(string path)
    {
        FilePath = path;
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
    {
        Chunk.Events.RemoveAll(e => e is SequenceTrackNameEvent);
        if (!string.IsNullOrEmpty(Name))
            Chunk.Events.Insert(0, new SequenceTrackNameEvent(Name));
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
        NoteOnEvent n => $"{NoteNumberToName(n.NoteNumber)} vel:{(byte)n.Velocity}",
        NoteOffEvent n => NoteNumberToName(n.NoteNumber),
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

    public static string NoteNumberToName(SevenBitNumber n)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return $"{names[(int)(byte)n % 12]}{(int)(byte)n / 12 - 1}";
    }
}
