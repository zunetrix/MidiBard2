using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using DryWetMidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.adapt-to-playable-range",
    "Adapt Tracks to C3-C6",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Range",
    RequiresSelectedTracks = true)]
public sealed class AdaptTracksToPlayableRangeCommand
    : EditorOperationBase, IEditorCommand<AdaptTracksToPlayableRangeCommandOptions, MidiForgeAdaptToRangeResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, AdaptTracksToPlayableRangeCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeAdaptToRangeResult> Execute(
        EditorCommandContext context,
        AdaptTracksToPlayableRangeCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var octaveShiftedTracks = 0;
        var changedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            sourceTracks++;

            TrackChunk adaptedChunk;
            int changedNotesInTrack;
            bool usedOctaveShift;
            if (options.RangeStrategy == MidiForgeRangeFitStrategy.PhraseAwareOctaveFit)
            {
                var phraseResult = AdaptChunkPhraseAware(file, sourceChunk);
                adaptedChunk = phraseResult.Chunk;
                changedNotesInTrack = phraseResult.ChangedNotes;
                usedOctaveShift = phraseResult.UsedOctaveShift;
            }
            else
            {
                var octaveShift = MidiForgeNotePrimitives.GetRangeFitOctaveShift(
                    notes.Select(note => (int)(byte)note.NoteNumber),
                    options.RangeStrategy);
                usedOctaveShift = octaveShift != 0;

                adaptedChunk = new TrackChunk(sourceChunk.Events.Select(midiEvent => midiEvent.Clone()));
                changedNotesInTrack = MidiForgeNotePrimitives.AdaptChunkNoteNumbers(adaptedChunk, octaveShift);
            }

            if (usedOctaveShift)
                octaveShiftedTracks++;

            changedNotes += changedNotesInTrack;

            if (options.RenameTracks)
                SetTrackName(adaptedChunk, $"{track.DisplayName} (Adapted {changedNotesInTrack} notes)");

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, new EditableTrack(adaptedChunk, trackIndex + 1));
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = new EditableTrack(adaptedChunk, trackIndex);
                replacedTracks++;
            }
        }

        var result = new MidiForgeAdaptToRangeResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            octaveShiftedTracks,
            changedNotes);
        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeAdaptToRangeResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeAdaptToRangeResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: !options.CreateNewTracks,
                ReloadEventList: !options.CreateNewTracks,
                ClearTrackSelection: true,
                ClearEventSelection: !options.CreateNewTracks,
                RebuildPreview: true));
    }

    private static void SetTrackName(TrackChunk chunk, string name)
    {
        chunk.Events.RemoveAll(midiEvent => midiEvent is SequenceTrackNameEvent);
        chunk.Events.Insert(0, new SequenceTrackNameEvent(name));
    }

    private static PhraseAdaptationResult AdaptChunkPhraseAware(
        EditableMidiFile file,
        TrackChunk sourceChunk)
    {
        var notes = sourceChunk.GetNotes()
            .OrderBy(note => note.Time)
            .ThenBy(note => (byte)note.NoteNumber)
            .ToArray();
        var adaptedNotes = new List<DryWetMidiNote>(notes.Length);
        var changedNotes = 0;
        var usedOctaveShift = false;

        foreach (var phrase in SplitPhrases(file, notes))
        {
            var shift = ChoosePhraseOctaveShift(phrase);
            usedOctaveShift |= shift != 0;

            foreach (var note in phrase)
            {
                var originalNoteNumber = (byte)note.NoteNumber;
                var shiftedNoteNumber = Math.Clamp(originalNoteNumber + shift, 0, 127);
                var adaptedNoteNumber = MidiForgeNotePrimitives.AdaptMidiNoteToPlayableRange(shiftedNoteNumber);
                if (adaptedNoteNumber != originalNoteNumber)
                    changedNotes++;

                adaptedNotes.Add(MidiForgeNotePrimitives.CloneNoteWithNumber(note, adaptedNoteNumber));
            }
        }

        var trackName = sourceChunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text ?? string.Empty;
        return new PhraseAdaptationResult(
            MidiForgeNotePrimitives.CreateTrackFromNotes(sourceChunk, trackName, adaptedNotes),
            changedNotes,
            usedOctaveShift);
    }

    private static IEnumerable<IReadOnlyList<DryWetMidiNote>> SplitPhrases(
        EditableMidiFile file,
        IReadOnlyList<DryWetMidiNote> notes)
    {
        if (notes.Count == 0)
            yield break;

        var beatTicks = Math.Max(1, MidiForgeNotePrimitives.GetTicksPerQuarterNote(file));
        var barTicks = Math.Max(beatTicks, MidiForgeNotePrimitives.GetBarDurationTicks(file));
        var maxPhraseTicks = barTicks * 4;
        var phrase = new List<DryWetMidiNote>();
        long phraseStart = 0;
        long phraseEnd = 0;
        long previousStart = 0;
        long previousEnd = 0;

        foreach (var note in notes)
        {
            var startNewPhrase = phrase.Count == 0;
            if (!startNewPhrase)
            {
                var gap = note.Time - phraseEnd;
                var crossedMeasure = note.Time / barTicks > previousStart / barTicks;
                startNewPhrase =
                    gap >= beatTicks ||
                    (crossedMeasure && note.Time > previousEnd) ||
                    note.Time - phraseStart >= maxPhraseTicks;
            }

            if (startNewPhrase)
            {
                if (phrase.Count > 0)
                    yield return phrase.ToArray();

                phrase.Clear();
                phraseStart = note.Time;
                phraseEnd = note.EndTime;
            }

            phrase.Add(note);
            phraseEnd = Math.Max(phraseEnd, note.EndTime);
            previousStart = note.Time;
            previousEnd = Math.Max(previousEnd, note.EndTime);
        }

        if (phrase.Count > 0)
            yield return phrase.ToArray();
    }

    private static int ChoosePhraseOctaveShift(IReadOnlyList<DryWetMidiNote> phrase)
    {
        var notes = phrase
            .Select(note => (int)(byte)note.NoteNumber)
            .ToArray();

        return new[] { -24, -12, 0, 12, 24 }
            .Select(shift => new
            {
                Shift = shift,
                InRange = notes.Count(note => IsInPlayableRange(note + shift)),
                OutsideDistance = notes.Sum(note => GetOutsidePlayableRangeDistance(note + shift)),
                WeightedOutsideDistance = notes.Sum(note => GetWeightedOutsidePlayableRangeDistance(note + shift)),
            })
            .OrderBy(result => result.WeightedOutsideDistance)
            .ThenByDescending(result => result.InRange)
            .ThenBy(result => result.OutsideDistance)
            .ThenBy(result => Math.Abs(result.Shift))
            .Select(result => result.Shift)
            .First();
    }

    private static bool IsInPlayableRange(int noteNumber)
        => noteNumber >= MidiForgeAnalysis.PlayableLowestMidiNote &&
           noteNumber <= MidiForgeAnalysis.PlayableHighestMidiNote;

    private static int GetOutsidePlayableRangeDistance(int noteNumber)
    {
        if (noteNumber < MidiForgeAnalysis.PlayableLowestMidiNote)
            return MidiForgeAnalysis.PlayableLowestMidiNote - noteNumber;

        return noteNumber > MidiForgeAnalysis.PlayableHighestMidiNote
            ? noteNumber - MidiForgeAnalysis.PlayableHighestMidiNote
            : 0;
    }

    private static int GetWeightedOutsidePlayableRangeDistance(int noteNumber)
    {
        if (noteNumber < MidiForgeAnalysis.PlayableLowestMidiNote)
            return MidiForgeAnalysis.PlayableLowestMidiNote - noteNumber;

        return noteNumber > MidiForgeAnalysis.PlayableHighestMidiNote
            ? (noteNumber - MidiForgeAnalysis.PlayableHighestMidiNote) * 4
            : 0;
    }

    private sealed record PhraseAdaptationResult(
        TrackChunk Chunk,
        int ChangedNotes,
        bool UsedOctaveShift);
}

public sealed record AdaptTracksToPlayableRangeCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeAdaptToRangeOptions Options);
