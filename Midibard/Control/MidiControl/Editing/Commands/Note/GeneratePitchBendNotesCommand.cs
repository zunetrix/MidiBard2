using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.generate-pitch-bend-notes",
    "Generate Pitch-Bend Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Arrangement",
    RequiresSelectedTracks = true)]
public sealed class GeneratePitchBendNotesCommand
    : EditorOperationBase, IEditorCommand<GeneratePitchBendNotesCommandOptions, MidiForgeGeneratePitchBendNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, GeneratePitchBendNotesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeGeneratePitchBendNotesResult> Execute(
        EditorCommandContext context,
        GeneratePitchBendNotesCommandOptions commandOptions)
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
        var generatedNotes = 0;
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes()
                .OrderBy(note => note.Time)
                .ThenBy(note => (byte)note.NoteNumber)
                .ToArray();
            var pitchBends = sourceChunk.GetTimedEvents()
                .Where(timedEvent => timedEvent.Event is PitchBendEvent)
                .OrderBy(timedEvent => timedEvent.Time)
                .ToArray();

            if (notes.Length == 0 || pitchBends.Length == 0)
            {
                skippedTracks++;
                continue;
            }

            var generatedTrackNotes = new List<MidiNote>();
            foreach (var note in notes)
                generatedTrackNotes.AddRange(GeneratePitchBendNotesForNote(note, pitchBends));

            if (generatedTrackNotes.Count == 0)
            {
                skippedTracks++;
                continue;
            }

            var generatedTrack = new EditableTrack(
                MidiForgeNotePrimitives.CreateTrackFromNotes(
                    sourceChunk,
                    $"{track.DisplayName} (Pitch Bend Notes)",
                    generatedTrackNotes,
                    includePitchBendEvents: false),
                0);

            sourceTracks++;
            generatedNotes += generatedTrackNotes.Count;

            if (options.DeleteOriginalTracks)
            {
                track.Dispose();
                file.Tracks[trackIndex] = generatedTrack;
                replacedTracks++;
            }
            else
            {
                file.Tracks.Insert(trackIndex + 1, generatedTrack);
                createdTracks++;
            }
        }

        var result = new MidiForgeGeneratePitchBendNotesResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            generatedNotes,
            skippedTracks);
        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeGeneratePitchBendNotesResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeGeneratePitchBendNotesResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: options.DeleteOriginalTracks,
                ReloadEventList: options.DeleteOriginalTracks,
                ClearTrackSelection: true,
                ClearEventSelection: options.DeleteOriginalTracks,
                RebuildPreview: true));
    }

    private static IEnumerable<MidiNote> GeneratePitchBendNotesForNote(
        MidiNote note,
        IReadOnlyList<TimedEvent> pitchBends)
    {
        var noteStartTick = note.Time;
        var noteEndTick = note.EndTime;
        var notePitchBends = pitchBends
            .Where(timedEvent => timedEvent.Event is PitchBendEvent bend && bend.Channel == note.Channel)
            .OrderBy(timedEvent => timedEvent.Time)
            .ToArray();

        if (notePitchBends.Length == 0)
            return [MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length)];

        var firstNotePitchBendEvent = notePitchBends.LastOrDefault(timedEvent => timedEvent.Time <= noteStartTick);
        var pitchBendsDuringNote = notePitchBends
            .Where(timedEvent =>
                (timedEvent.Time > noteStartTick && timedEvent.Time <= noteEndTick) ||
                ReferenceEquals(timedEvent, firstNotePitchBendEvent))
            .OrderBy(timedEvent => timedEvent.Time)
            .ToArray();

        var uniquePitchBends = new List<TimedEvent>();
        foreach (var pitchBend in pitchBendsDuringNote)
        {
            if (uniquePitchBends.Count == 0 ||
                GetPitchBendSemitones((PitchBendEvent)pitchBend.Event) !=
                GetPitchBendSemitones((PitchBendEvent)uniquePitchBends[^1].Event))
            {
                uniquePitchBends.Add(pitchBend);
            }
        }

        if (uniquePitchBends.Count == 0)
            return [MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length)];

        var generatedNotes = new List<MidiNote>();
        for (int i = 0; i < uniquePitchBends.Count; i++)
        {
            var pitchBend = uniquePitchBends[i];
            if (i == 0 && pitchBend.Time > noteStartTick)
                AddPitchBendGeneratedNote(generatedNotes, note, (byte)note.NoteNumber, noteStartTick, pitchBend.Time);

            var semitones = GetPitchBendSemitones((PitchBendEvent)pitchBend.Event);
            var noteNumber = Math.Clamp((byte)note.NoteNumber + semitones, 0, 127);
            var segmentStartTick = i == 0 && pitchBend.Time <= noteStartTick
                ? noteStartTick
                : pitchBend.Time;
            var segmentEndTick = i == uniquePitchBends.Count - 1
                ? noteEndTick
                : uniquePitchBends[i + 1].Time;

            AddPitchBendGeneratedNote(generatedNotes, note, noteNumber, segmentStartTick, segmentEndTick);
        }

        return generatedNotes;
    }

    private static void AddPitchBendGeneratedNote(
        ICollection<MidiNote> notes,
        MidiNote sourceNote,
        int noteNumber,
        long startTick,
        long endTick)
    {
        var length = endTick - startTick;
        if (length <= 0) return;

        var note = MidiForgeNotePrimitives.CloneNoteWithNumber(sourceNote, noteNumber);
        note.Time = Math.Max(0, startTick);
        note.Length = length;
        notes.Add(note);
    }

    private static int GetPitchBendSemitones(PitchBendEvent pitchBend)
        => pitchBend.PitchValue switch
        {
            < 4096 => -2,
            < 8192 => -1,
            < 12288 => 0,
            < 16383 => 1,
            _ => 2,
        };
}

public sealed record GeneratePitchBendNotesCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeGeneratePitchBendNotesOptions Options);
