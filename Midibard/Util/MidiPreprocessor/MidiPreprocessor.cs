using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Util.MidiPreprocessor;

internal class MidiPreprocessor
{
    /// <summary>
    /// Retrocompatibility fix for old MIDI files where NoteOff events are on channel 0
    /// even when the corresponding NoteOn was on a different channel (a common bug in
    /// 90s sequencers). DryWetMidi 8.x is strictly channel-aware in GetNotes() pairing,
    /// while 7.x was lenient. Without this fix, orphaned NoteOns cause stuck keys or
    /// silence when AntiStack is enabled.
    /// </summary>
    internal static void FixNoteOffChannels(TrackChunk chunk)
    {
        // Fast scan: detect if the chunk has NoteOff on a different channel than its NoteOn.
        // Track the most recent NoteOn channel per note number keyed to avoid false positives
        // on polyphonic files where the same note is played on different channels simultaneously.
        bool hasMismatch = false;
        var lastOnChannel = new Dictionary<SevenBitNumber, FourBitNumber>();
        foreach (var ev in chunk.Events)
        {
            if (ev is NoteOnEvent nOn && (byte)nOn.Velocity > 0)
            {
                lastOnChannel[nOn.NoteNumber] = nOn.Channel;
            }
            else if (ev is NoteOffEvent nOff)
            {
                if (lastOnChannel.TryGetValue(nOff.NoteNumber, out var onCh) && nOff.Channel != onCh)
                { hasMismatch = true; break; }
            }
            else if (ev is NoteOnEvent nOn2 && (byte)nOn2.Velocity == 0)
            {
                if (lastOnChannel.TryGetValue(nOn2.NoteNumber, out var onCh) && nOn2.Channel != onCh)
                { hasMismatch = true; break; }
            }
        }
        if (!hasMismatch) return;

        using var manager = chunk.ManageTimedEvents();
        var events = manager.Objects.OrderBy(te => te.Time).ThenBy(te => te.Event is NoteOffEvent ? 0 : 1).ToList();

        // Map: note number -> list of (time, channel) for active (unmatched) NoteOn events, FIFO
        var activeNoteOns = new Dictionary<SevenBitNumber, List<(long time, FourBitNumber channel)>>();

        foreach (var te in events)
        {
            if (te.Event is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0)
            {
                if (!activeNoteOns.TryGetValue(noteOn.NoteNumber, out var list))
                    activeNoteOns[noteOn.NoteNumber] = list = new List<(long, FourBitNumber)>();
                list.Add((te.Time, noteOn.Channel));
                continue;
            }

            SevenBitNumber noteNum;
            FourBitNumber eventChannel;
            if (te.Event is NoteOffEvent nOff2)
            { noteNum = nOff2.NoteNumber; eventChannel = nOff2.Channel; }
            else if (te.Event is NoteOnEvent silentOn && (byte)silentOn.Velocity == 0)
            { noteNum = silentOn.NoteNumber; eventChannel = silentOn.Channel; }
            else continue;

            if (!activeNoteOns.TryGetValue(noteNum, out var noteOnList) || noteOnList.Count == 0)
                continue;

            // Prefer same-channel match first (standard case)
            var sameChannelIdx = noteOnList.FindIndex(n => n.channel == eventChannel);
            if (sameChannelIdx >= 0)
            {
                noteOnList.RemoveAt(sameChannelIdx);
                continue;
            }

            // No same-channel NoteOn - fix the NoteOff channel to match oldest active NoteOn
            var correctChannel = noteOnList[0].channel;
            noteOnList.RemoveAt(0);
            if (te.Event is NoteOffEvent nOff3)
                nOff3.Channel = correctChannel;
            else if (te.Event is NoteOnEvent nOn3)
                nOn3.Channel = correctChannel;
        }
    }

    /// <summary>
    /// Realign the the notes and Events in a <see cref="MidiFile"/> to the beginning
    /// </summary>
    /// <param name="midi"></param>
    /// <returns><see cref="MidiFile"/></returns>
    public static MidiFile RealignMidiFile(MidiFile midi, double startOffsetSeconds = 0)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        var firstNoteTime = midi.GetTrackChunks().GetNotes().First().GetTimedNoteOnEvent().Time;

        // Realign all chunks
        foreach (var chunk in midi.GetTrackChunks())
        {
            long startOffsetTicks = startOffsetSeconds > 0 ? SecondsToTicks(startOffsetSeconds, midi.GetTempoMap()) : 0;
            RealignTrackEvents(chunk, firstNoteTime, startOffsetTicks);
        }

        // Parallel.ForEach(midi.GetTrackChunks(), chunk =>
        // {
        //     chunk = RealignTrackEvents(chunk, firstNoteTime).Result;
        // });

        stopwatch.Stop();

        DalamudApi.PluginLog.Warning($"[MidiPreprocessor] Realign tracks took: {stopwatch.Elapsed.TotalMilliseconds} ms");
        return midi;
    }

    /// <summary>
    /// Realigns the track events in <see cref="TrackChunk"/>
    /// </summary>
    /// <param name="originalChunk"></param>
    /// <param name="delta"></param>
    /// <returns><see cref="Task{TResult}"/> is <see cref="TrackChunk"/></returns>
    internal static Task<TrackChunk> RealignTrackEvents(TrackChunk originalChunk, long delta, long startOffsetTicks = 0)
    {
        using (var manager = originalChunk.ManageTimedEvents())
        {
            foreach (var timedEvent in manager.Objects)
            {
                var newTime = timedEvent.Time - delta + startOffsetTicks;
                timedEvent.Time = newTime < 0 ? 0 : newTime;
            }
        }

        return Task.FromResult(originalChunk);
    }

    private static long SecondsToTicks(double seconds, TempoMap tempoMap)
    {
        var ticks = TimeConverter.ConvertFrom(seconds.ToMetricTimeSpan(), tempoMap);
        return ticks;
    }

    public static TrackChunk[] ProcessTracks(TrackChunk[] trackChunks, TempoMap tempoMap)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        foreach (var track in trackChunks)
        {
            // track.Events.RemoveAll(e => e is PitchBendEvent);
            FixNoteOffChannels(track); // compat: DryWetMidi 8.x requires same-channel NoteOn/NoteOff pairing
            track.ProcessNotes(note => CutNote(note, tempoMap));
        }

        stopwatch.Stop();
        DalamudApi.PluginLog.Debug($"[MidiPreprocessor] Process tracks took: {stopwatch.Elapsed.TotalMilliseconds} ms");
        return trackChunks;
    }

    private static void CutNote(Note note, TempoMap tempoMap)
    {
        var length = note.LengthAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000;
        //DalamudApi.PluginLog.Verbose($"Note: {n.ToString()} Length: {length}ms");
        if (length > 2000)
        {
            var newLength = length - 50; // cut long notes by 50ms to add a small interval between key up/down
            note.SetLength(new MetricTimeSpan(newLength * 1000), tempoMap);
        }
    }

    /// <summary>
    /// Removes stacked notes
    /// Types:
    /// 0 - Do Nothing
    /// 1 - FIFO
    /// 2 - Keep short
    /// 3 - Keep long
    /// </summary>
    public static MidiFile RemoveStackedNotes(MidiFile outputMidi, AntiStackType type)
    {
        if (type == 0)
            return outputMidi;
        Parallel.ForEach(outputMidi.GetTrackChunks().Where(static x => x.GetNotes().Any()), (originalChunk) =>
        {
            Dictionary<KeyValuePair<long, SevenBitNumber>, Note> notes = new Dictionary<KeyValuePair<long, SevenBitNumber>, Note>();
            Note cnote = new Note((SevenBitNumber)0);
            foreach (Note note in originalChunk.GetNotes())
            {
                if (type == AntiStackType.KeepFirstNote)
                {
                    if (!notes.ContainsKey(new KeyValuePair<long, SevenBitNumber>(note.Time, note.NoteNumber)))
                        notes.Add(new KeyValuePair<long, SevenBitNumber>(note.Time, note.NoteNumber), note);
                }
                else
                {
                    if (!notes.ContainsKey(new KeyValuePair<long, SevenBitNumber>(note.Time, note.NoteNumber)))
                        notes.Add(new KeyValuePair<long, SevenBitNumber>(note.Time, note.NoteNumber), note);
                    else
                    {
                        var found = notes.First(n => (n.Value.Time == note.Time) && (n.Value.NoteNumber == note.NoteNumber));
                        if (((note.Length < found.Value.Length) && (type == AntiStackType.KeepShortestNote)) ||
                            ((note.Length > found.Value.Length) && (type == AntiStackType.KeepLongestNote)))
                        {
                            notes.Remove(found.Key);
                            notes.Add(new KeyValuePair<long, SevenBitNumber>(note.Time, note.NoteNumber), note);
                        }
                    }
                }
            }
            originalChunk.RemoveNotes(n => n != null);
            originalChunk.AddObjects(notes.Values.ToArray<Note>());
        });
        return outputMidi;
    }
}

