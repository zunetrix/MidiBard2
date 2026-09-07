using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

using MidiBard.Managers;
namespace MidiBard.Util.NetworkCapture;

/// <summary>
/// Converts a list of captured <see cref="EnsembleManager.PerformancePacketSnapshot"/> into a
/// MIDI file. One TrackChunk is created per unique performer EntityId. Consecutive packet slots
/// that carry the same active note are coalesced into a single NoteOn/NoteOff pair.
/// </summary>
internal static class NetworkPacketMidiExporter
{
    // Each packet covers ~3 seconds with 60 note slots → 50 ms per slot.
    private const double MsPerSlot = 50.0;

    // MIDI timing: 480 PPQ at 120 BPM (500 000 µs per beat) → 1 tick ≈ 1.042 ms
    private const int Ppq = 480;
    private const int MicrosecondsPerBeat = 500_000; // 120 BPM

    // Ticks per millisecond = PPQ / (µs_per_beat / 1000)
    private static readonly double TicksPerMs = Ppq / (MicrosecondsPerBeat / 1000.0);

    private static long MsToTick(double ms) => (long)Math.Round(ms * TicksPerMs);

    /// <summary>
    /// Builds and returns a <see cref="MidiFile"/> from the supplied packet log.
    /// </summary>
    /// <param name="packets">Packets in capture order. May be unsorted.</param>
    /// <param name="recordStart">The timestamp used as t=0 for the recording.</param>
    /// <param name="resolveNameFunc">
    /// Optional callback that turns an EntityId into a display name for the track name event.
    /// </param>
    public static MidiFile Export(
        IReadOnlyList<EnsembleManager.PerformancePacketSnapshot> packets,
        DateTime recordStart,
        Func<uint, string>? resolveNameFunc = null)
    {
        // 1. Flatten all note attacks into a single list
        var allAttacks = new List<(double ms, int midiNote, byte tone, uint entityId)>();
        var performerTones = new Dictionary<uint, byte>(); // Remember tone across packets

        foreach (var pkt in packets.OrderBy(p => p.Timestamp))
        {
            double packetStartMs = (pkt.Timestamp - recordStart).TotalMilliseconds;
            if (packetStartMs < 0) packetStartMs = 0;

            foreach (var perf in pkt.Performers)
            {
                if (!performerTones.TryGetValue(perf.EntityId, out byte currentTone))
                    currentTone = 0; // Default to Tone 0

                for (int slot = 0; slot < perf.Notes.Length && slot < 60; slot++)
                {
                    byte rawNote = perf.Notes[slot];
                    if (rawNote == 0xFF || rawNote == 0xFE) continue;

                    int midiNote = rawNote + 24;
                    double slotMs = packetStartMs + slot * MsPerSlot;

                    byte rawTone = perf.Tones[slot];
                    if (rawTone != 0xFF && rawTone != 0xFE)
                        currentTone = rawTone;

                    allAttacks.Add((slotMs, midiNote, currentTone, perf.EntityId));
                }

                performerTones[perf.EntityId] = currentTone;
            }
        }

        // 2. Group attacks by (EntityId, Tone)
        var byTrack = allAttacks
            .GroupBy(a => (a.entityId, a.tone))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ms).ToList());

        var midiFile = new MidiFile();
        midiFile.TimeDivision = new TicksPerQuarterNoteTimeDivision(Ppq);

        // Tempo track (track 0)
        var tempoTrack = new TrackChunk(new SetTempoEvent(MicrosecondsPerBeat));
        midiFile.Chunks.Add(tempoTrack);

        // 3. Build one track per (EntityId, Tone), assigning a unique channel per performer
        // We want all tracks of the same performer to use the same channel?
        // Or unique channel per track? Unique channel per track is safer for overlapping notes if they switch back and forth.
        int channelIndex = 0;
        foreach (var kvp in byTrack)
        {
            var (entityId, tone) = kvp.Key;
            var attacks = kvp.Value;

            if (channelIndex == 9) channelIndex++; // skip percussion channel
            if (channelIndex > 15) channelIndex = 0; // fallback if > 16 tracks

            var trackChunk = BuildInstrumentTrack(entityId, tone, attacks, resolveNameFunc, (FourBitNumber)channelIndex);
            midiFile.Chunks.Add(trackChunk);

            channelIndex++;
        }

        return midiFile;
    }

    /// <summary>
    /// Saves the exported MIDI file to the given path, creating directories as needed.
    /// Returns the full path that was written.
    /// </summary>
    public static string ExportToFile(
        IReadOnlyList<EnsembleManager.PerformancePacketSnapshot> packets,
        DateTime recordStart,
        string outputPath,
        Func<uint, string>? resolveNameFunc = null)
    {
        var midiFile = Export(packets, recordStart, resolveNameFunc);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        midiFile.Write(outputPath, overwriteFile: true);
        return outputPath;
    }

    // internals
    private static TrackChunk BuildInstrumentTrack(
        uint entityId,
        byte tone,
        List<(double ms, int midiNote, byte tone, uint entityId)> attacks,
        Func<uint, string>? resolveNameFunc,
        FourBitNumber channel)
    {
        var timedEvents = new List<(long tick, MidiEvent ev)>();

        string baseName = resolveNameFunc?.Invoke(entityId) ?? $"0x{entityId:X}";

        string instName = tone switch
        {
            0 => "Tone 0 (Default / Overdriven)",
            1 => "Guitar (Clean)",
            2 => "Guitar (Muted)",
            3 => "Guitar (Power)",
            4 => "Guitar (Special)",
            _ => $"Tone {tone}"
        };

        string trackName = $"{baseName} - {instName}";
        timedEvents.Add((0, new SequenceTrackNameEvent(trackName)));

        // Set the instrument at the beginning of the track
        SevenBitNumber programNum = GetProgramNumber(tone);
        timedEvents.Add((0, new ProgramChangeEvent(programNum) { Channel = channel }));

        // Fixed duration for notes since FFXIV's short keypresses sound too abrupt in MIDI
        const double FixedNoteDurationMs = 2000.0;

        // Deduplicate rapid same-note attacks (within 1 slot)
        var cleanAttacks = new List<(double ms, int note)>();
        foreach (var atk in attacks)
        {
            if (cleanAttacks.Count > 0)
            {
                var last = cleanAttacks.Last();
                if (last.note == atk.midiNote && (atk.ms - last.ms) <= MsPerSlot * 1.5)
                    continue; // Skip rapid re-trigger
            }
            cleanAttacks.Add((atk.ms, atk.midiNote));
        }

        // Convert attacks into NoteOn/NoteOff pairs with fixed duration
        for (int i = 0; i < cleanAttacks.Count; i++)
        {
            var atk = cleanAttacks[i];
            double startMs = atk.ms;
            double endMs = startMs + FixedNoteDurationMs;

            // In FFXIV performance, tracks are strictly monophonic.
            // Any new note attack immediately interrupts the previous note.
            if (i + 1 < cleanAttacks.Count)
            {
                var nextAtk = cleanAttacks[i + 1];
                if (nextAtk.ms < endMs)
                {
                    endMs = nextAtk.ms;
                }
            }

            long onTick = MsToTick(startMs);
            long offTick = MsToTick(endMs);
            if (offTick <= onTick) offTick = onTick + 1;

            timedEvents.Add((onTick, new NoteOnEvent((SevenBitNumber)atk.note, (SevenBitNumber)80) { Channel = channel }));
            timedEvents.Add((offTick, new NoteOffEvent((SevenBitNumber)atk.note, (SevenBitNumber)0) { Channel = channel }));
        }

        // Sort by tick, NoteOff before NoteOn/ProgramChange at the same tick
        timedEvents.Sort((a, b) =>
        {
            int cmp = a.tick.CompareTo(b.tick);
            if (cmp != 0) return cmp;

            bool aOff = a.ev is NoteOffEvent;
            bool bOff = b.ev is NoteOffEvent;
            if (aOff && !bOff) return -1;
            if (!aOff && bOff) return 1;
            return 0;
        });

        // Convert absolute ticks to delta ticks
        var chunk = new TrackChunk();
        long prevTick = 0;
        foreach (var (tick, ev) in timedEvents)
        {
            ev.DeltaTime = tick - prevTick;
            prevTick = tick;
            chunk.Events.Add(ev);
        }

        return chunk;
    }

    private static SevenBitNumber GetProgramNumber(byte tone)
    {
        // Instruments without tone mode do not transmit tone information;
        // it is impossible to distinguish guitar overdrive from other instruments, as the overdrive setting does not send tone data either
        uint rowId = tone switch
        {
            // 0 => 24, // Guitar: Overdriven
            0 => 0, // Guitar: Overdriven
            1 => 25, // Guitar: Clean
            2 => 26, // Guitar: Muted
            3 => 27, // Guitar: Power
            4 => 28, // Guitar: Special
            _ => 0
        };

        if (rowId != 0)
        {
            var instrument = InstrumentHelper.Instruments.FirstOrDefault(i => i.Row.RowId == rowId);
            if (instrument != null)
                return instrument.ProgramNumber;
        }

        return (SevenBitNumber)0; // Default to acoustic grand piano if unknown or default
    }
}
