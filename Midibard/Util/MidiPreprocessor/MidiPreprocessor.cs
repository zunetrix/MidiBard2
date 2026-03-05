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

