using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing;

internal static class MidiForgeNotePrimitives
{
    public static int AdaptMidiNoteToPlayableRange(int midiNote)
        => TrackInfo.TranslateNoteNumber(midiNote, adaptOOR: true) + MidiForgeAnalysis.PlayableLowestMidiNote;

    public static int GetRangeFitOctaveShift(IEnumerable<int> notes, MidiForgeRangeFitStrategy strategy)
    {
        var noteNumbers = notes.ToArray();
        if (noteNumbers.Length == 0)
            return 0;

        return strategy switch
        {
            MidiForgeRangeFitStrategy.BestOctaveFit => MidiForgeAnalysis.GetOptimalTransposeAmount(noteNumbers),
            MidiForgeRangeFitStrategy.LowerHighNotesFirst => GetLowerHighNotesFirstOctaveShift(noteNumbers),
            _ => 0,
        };
    }

    public static int AdaptChunkNoteNumbers(TrackChunk chunk, int octaveShift)
    {
        var changedNotes = chunk.GetNotes()
            .Count(note => AdaptMidiNoteToPlayableRange((byte)note.NoteNumber + octaveShift) != (byte)note.NoteNumber);

        foreach (var midiEvent in chunk.Events)
        {
            switch (midiEvent)
            {
                case NoteOnEvent noteOn:
                    noteOn.NoteNumber = (SevenBitNumber)(byte)AdaptMidiNoteToPlayableRange((byte)noteOn.NoteNumber + octaveShift);
                    break;
                case NoteOffEvent noteOff:
                    noteOff.NoteNumber = (SevenBitNumber)(byte)AdaptMidiNoteToPlayableRange((byte)noteOff.NoteNumber + octaveShift);
                    break;
            }
        }

        return changedNotes;
    }

    public static IEnumerable<MidiForgeChordSplitGroup> SplitChordNotes(
        IEnumerable<Note> notes,
        string trackName,
        MidiForgeChordSplitStrategy strategy,
        MidiForgeChordGroupMode groupMode,
        int minimumSimultaneousNotes)
    {
        var splitGroups = new Dictionary<string, MidiForgeChordSplitGroup>();

        foreach (var group in notes
            .GroupBy(note => strategy == MidiForgeChordSplitStrategy.SameStartTickAndLength
                ? (note.Time, note.Length)
                : (note.Time, Length: 0))
            .OrderBy(group => group.Key.Time))
        {
            var groupNotes = group
                .OrderByDescending(note => (byte)note.NoteNumber)
                .ToArray();
            var groupSize = groupNotes.Length;
            var isChord = groupSize >= minimumSimultaneousNotes;

            for (int i = 0; i < groupNotes.Length; i++)
            {
                var partOrder = i + 1;
                var trackGroupName = GetSplitChordGroupTrackName(trackName, groupSize, partOrder, isChord, groupMode);
                if (!splitGroups.TryGetValue(trackGroupName, out var splitGroup))
                {
                    splitGroup = new MidiForgeChordSplitGroup(
                        trackGroupName,
                        isChord ? groupSize : 0,
                        isChord ? partOrder : 0,
                        isChord,
                        new List<Note>());
                    splitGroups.Add(trackGroupName, splitGroup);
                }

                splitGroup.Notes.Add(groupNotes[i]);
            }
        }

        return splitGroups.Values
            .OrderBy(group => group.GroupSize)
            .ThenBy(group => group.Order)
            .ThenBy(group => group.TrackName, StringComparer.Ordinal);
    }

    public static Note CloneNoteWithLength(Note note, long length)
        => new(
            note.NoteNumber,
            Math.Max(0, length),
            note.Time)
        {
            Channel = note.Channel,
            Velocity = note.Velocity,
            OffVelocity = note.OffVelocity,
        };

    public static bool NotesOverlap(Note note, Note other)
    {
        var noteStart = note.Time;
        var noteEnd = note.Time + note.Length;
        var otherStart = other.Time;
        var otherEnd = other.Time + other.Length;

        return otherEnd > noteStart && otherStart < noteEnd;
    }

    public static bool IsEqualNoteAtStart(Note note, Note other)
        => note.Time == other.Time && (byte)note.NoteNumber == (byte)other.NoteNumber;

    public static TrackChunk CreateTrackFromNotes(
        TrackChunk sourceChunk,
        string trackName,
        IEnumerable<Note> notes,
        bool includePitchBendEvents = true)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();

        manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(trackName), 0));

        foreach (var timedEvent in sourceChunk.GetTimedEvents()
            .Where(te => te.Event is not NoteOnEvent and not NoteOffEvent and not SequenceTrackNameEvent))
        {
            if (!includePitchBendEvents && timedEvent.Event is PitchBendEvent)
                continue;

            manager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), timedEvent.Time));
        }

        foreach (var note in notes.OrderBy(note => note.Time).ThenBy(note => (byte)note.NoteNumber))
        {
            manager.Objects.Add(new TimedEvent(
                new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                note.Time));
            manager.Objects.Add(new TimedEvent(
                new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                note.EndTime));
        }

        return chunk;
    }

    public static int InsertDerivedTrackAfterTarget(
        EditableMidiFile file,
        int targetTrackIndex,
        TrackChunk sourceChunk,
        string trackName,
        IReadOnlyCollection<Note> notes)
    {
        if (notes.Count == 0)
            return 0;

        file.Tracks.Insert(targetTrackIndex + 1, new EditableTrack(
            CreateTrackFromNotes(sourceChunk, trackName, notes),
            targetTrackIndex + 1));
        return 1;
    }

    public static long LimitDurationToCurrentMeasureWhenNextMeasureIsEmpty(
        Note note,
        IReadOnlyCollection<Note> trackNotes,
        long newLength,
        long barDurationTicks)
    {
        if (barDurationTicks <= 0)
            return newLength;

        var noteMeasureIndex = note.Time / barDurationTicks;
        var currentMeasureEnd = (noteMeasureIndex + 1) * barDurationTicks;
        var nextMeasureEnd = currentMeasureEnd + barDurationTicks;
        if (note.Time + newLength <= currentMeasureEnd)
            return newLength;

        var nextMeasureHasNotes = trackNotes.Any(other =>
            other.Time >= currentMeasureEnd && other.Time < nextMeasureEnd);
        if (nextMeasureHasNotes)
            return newLength;

        return Math.Max(1, currentMeasureEnd - note.Time);
    }

    public static long GetBarDurationTicks(EditableMidiFile file)
    {
        var ticksPerQuarter = file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision timeDivision
            ? timeDivision.TicksPerQuarterNote
            : 480;

        return ticksPerQuarter * 4L;
    }

    public static void RefreshTrackIndexes(EditableMidiFile file)
    {
        for (int i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
    }

    private static int GetLowerHighNotesFirstOctaveShift(IEnumerable<int> notes)
    {
        var highestNote = notes.Max();
        if (highestNote <= MidiForgeAnalysis.PlayableHighestMidiNote)
            return 0;

        var octaves = (highestNote - MidiForgeAnalysis.PlayableHighestMidiNote + 11) / 12;
        return -12 * octaves;
    }

    private static string GetSplitChordGroupTrackName(
        string trackName,
        int groupSize,
        int partOrder,
        bool isChord,
        MidiForgeChordGroupMode groupMode)
    {
        if (!isChord)
            return $"{trackName} no chords";

        return groupMode switch
        {
            MidiForgeChordGroupMode.Group => $"{trackName} chords of {groupSize}",
            MidiForgeChordGroupMode.Individual => $"{trackName} chords of {groupSize} ({partOrder})",
            _ => $"{trackName} chords parts ({partOrder})",
        };
    }
}

internal sealed record MidiForgeChordSplitGroup(
    string TrackName,
    int GroupSize,
    int Order,
    bool IsChord,
    List<Note> Notes);
