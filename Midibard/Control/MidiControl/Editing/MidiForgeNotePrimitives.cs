using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing;

internal static class MidiForgeNotePrimitives
{
    private static readonly Dictionary<string, int> NoteIndexByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C"] = 0, ["Cb"] = -1, ["Db"] = 1, ["C#"] = 1, ["D"] = 2,
        ["Eb"] = 3, ["D#"] = 3, ["E"] = 4, ["F"] = 5,
        ["Gb"] = 6, ["F#"] = 6, ["G"] = 7,
        ["Ab"] = 8, ["G#"] = 8, ["A"] = 9,
        ["Bb"] = 10, ["A#"] = 10, ["B"] = 11, ["B#"] = 12,
    };

    /// <summary>
    /// Parses a note text string like "C3", "C#4", "Db5" into a MIDI note number.
    /// Does NOT accept plain integers — use <see cref="ResolveNoteBoundary"/> for that.
    /// </summary>
    public static bool TryParseNoteText(string input, out int midiNote)
    {
        midiNote = -1;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (trimmed.Length != 2 && trimmed.Length != 3)
            return false;

        // Last char must be a digit 0-9
        if (!char.IsDigit(trimmed[^1]))
            return false;

        var octave = trimmed[^1] - '0';

        // Everything before the last char is the note name (1 char for natural, 2 for accidental)
        var namePart = trimmed[..^1];
        if (!NoteIndexByName.TryGetValue(namePart, out var semitone))
            return false;

        var computed = (octave + 1) * 12 + semitone;
        if (computed < 0 || computed > 127)
            return false;

        midiNote = computed;
        return true;
    }

    /// <summary>
    /// Parses a note text string and throws if it cannot be parsed.
    /// </summary>
    public static int ParseNoteText(string input)
    {
        if (!TryParseNoteText(input, out var midiNote))
            throw new ArgumentException($"Invalid note text: {input}", nameof(input));
        return midiNote;
    }

    /// <summary>
    /// Resolves a boundary string to a MIDI note number.
    /// Tries integer parsing first (including negative values), then note text.
    /// Falls back to <paramref name="fallback"/> if neither parses.
    /// </summary>
    public static int ResolveNoteBoundary(string input, int fallback = 0)
    {
        if (int.TryParse(input, out var intValue))
            return intValue;

        if (TryParseNoteText(input, out var noteValue))
            return noteValue;

        return fallback;
    }

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

    public static long GetTicksPerQuarterNote(EditableMidiFile file)
        => file.Source.TimeDivision is TicksPerQuarterNoteTimeDivision timeDivision
            ? timeDivision.TicksPerQuarterNote
            : 480;

    public static long ResolveChordTimingToleranceTicks(
        EditableMidiFile file,
        MidiForgeChordTimingToleranceOptions? options)
        => ResolveChordTimingToleranceTicks(GetTicksPerQuarterNote(file), options);

    public static long ResolveChordTimingToleranceTicks(
        long ticksPerQuarterNote,
        MidiForgeChordTimingToleranceOptions? options)
    {
        options ??= new MidiForgeChordTimingToleranceOptions();

        return options.Mode switch
        {
            MidiForgeChordTimingToleranceMode.OneOver128Note => Math.Max(1, (long)Math.Round(ticksPerQuarterNote / 32d)),
            MidiForgeChordTimingToleranceMode.OneOver64Note => Math.Max(1, (long)Math.Round(ticksPerQuarterNote / 16d)),
            MidiForgeChordTimingToleranceMode.CustomTicks => Math.Max(0, options.CustomTicks),
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
        int minimumSimultaneousNotes,
        long timingToleranceTicks = 0)
    {
        var splitGroups = new Dictionary<string, MidiForgeChordSplitGroup>();

        foreach (var group in BuildChordNoteGroups(notes, strategy, timingToleranceTicks))
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

    public static IEnumerable<IReadOnlyList<Note>> BuildChordNoteGroups(
        IEnumerable<Note> notes,
        MidiForgeChordSplitStrategy strategy,
        long timingToleranceTicks = 0)
    {
        var orderedNotes = notes
            .OrderBy(note => note.Time)
            .ThenBy(note => (byte)note.NoteNumber)
            .ThenBy(note => note.Length)
            .ToArray();

        if (timingToleranceTicks <= 0)
        {
            return orderedNotes
                .GroupBy(note => strategy == MidiForgeChordSplitStrategy.SameStartTickAndLength
                    ? (note.Time, note.Length)
                    : (note.Time, Length: 0))
                .OrderBy(group => group.Key.Time)
                .Select(group => (IReadOnlyList<Note>)group.ToArray())
                .ToArray();
        }

        var used = new bool[orderedNotes.Length];
        var groups = new List<IReadOnlyList<Note>>();
        for (int i = 0; i < orderedNotes.Length; i++)
        {
            if (used[i])
                continue;

            var anchor = orderedNotes[i];
            var group = new List<Note> { anchor };
            used[i] = true;

            for (int j = i + 1; j < orderedNotes.Length; j++)
            {
                if (used[j])
                    continue;

                var candidate = orderedNotes[j];
                if (candidate.Time - anchor.Time > timingToleranceTicks)
                    break;

                if (!NotesOverlap(anchor, candidate))
                    continue;

                if (strategy == MidiForgeChordSplitStrategy.SameStartTickAndLength &&
                    Math.Abs(candidate.Length - anchor.Length) > timingToleranceTicks)
                    continue;

                group.Add(candidate);
                used[j] = true;
            }

            groups.Add(group);
        }

        return groups;
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

    public static Note CloneNoteWithNumber(Note note, int noteNumber)
        => new(
            (SevenBitNumber)(byte)Math.Clamp(noteNumber, 0, 127),
            note.Length,
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
        var ticksPerQuarter = GetTicksPerQuarterNote(file);
        return ticksPerQuarter * 4L;
    }

    /// <summary>
    /// Converts a MIDI note number (0–127) to a note text string like "C3", "F#4".
    /// Clamps out-of-range values to 0–127. Note 0 produces "C-1".
    /// </summary>
    public static string GetMidiNoteName(int noteNumber)
    {
        var noteNames = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var clampedNoteNumber = Math.Clamp(noteNumber, 0, 127);
        return $"{noteNames[clampedNoteNumber % 12]}{clampedNoteNumber / 12 - 1}";
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
