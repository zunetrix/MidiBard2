// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using static Dalamud.api;

namespace MidiBard.Util.MidiPreprocessor
{
    internal class MidiPreprocessor
    {
        /// <summary>
        /// Realign the the notes and Events in a <see cref="MidiFile"/> to the beginning
        /// </summary>
        /// <param name="midi"></param>
        /// <returns><see cref="MidiFile"/></returns>
        public static MidiFile RealignMidiFile(MidiFile midi, long newStartOffset = 0)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Get the time of the first note on event
            var firstNoteTime = midi.GetTrackChunks().GetNotes().First().GetTimedNoteOnEvent().Time;

            // Realign all chunks to start at newStartOffset instead of 0
            Parallel.ForEach(midi.GetTrackChunks(), chunk =>
            {
                chunk = RealignTrackEvents(chunk, firstNoteTime, newStartOffset).Result;
            });

            PluginLog.Debug($"[MidiPreprocessor] Realign tracks took: {stopwatch.Elapsed.TotalMilliseconds} ms");
            stopwatch.Stop();
            return midi;
        }

        /// <summary>
        /// Realigns the track events in <see cref="TrackChunk"/>
        /// </summary>
        /// <param name="originalChunk"></param>
        /// <param name="delta"></param>
        /// <returns><see cref="Task{TResult}"/> is <see cref="TrackChunk"/></returns>
        internal static Task<TrackChunk> RealignTrackEvents(TrackChunk originalChunk, long delta, long newStartOffset)
        {
            using (var manager = originalChunk.ManageTimedEvents())
            {
                foreach (var timedEvent in manager.Objects)
                {
                    var newTime = timedEvent.Time - delta + newStartOffset;
                    timedEvent.Time = newTime < 0 ? 0 : newTime;
                }
            }

            return Task.FromResult(originalChunk);
        }

        public static TrackChunk[] ProcessTracks(TrackChunk[] trackChunks, TempoMap tempoMap)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (var track in trackChunks)
            {
                track.ProcessNotes(note => CutNote(note, tempoMap));
            }

            stopwatch.Stop();
            PluginLog.Debug($"[MidiPreprocessor] Process tracks took: {stopwatch.Elapsed.TotalMilliseconds} ms");
            return trackChunks;
        }

        private static void CutNote(Note note, TempoMap tempoMap)
        {
            var length = note.LengthAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000;
            //PluginLog.Verbose($"Note: {n.ToString()} Length: {length}ms");
            if (length > 2000)
            {
                var newLength = length - 50; // cut long notes by 50ms to add a small interval between key up/down
                note.SetLength<Note>(new MetricTimeSpan(newLength * 1000), tempoMap);
            }
        }
    }
}
