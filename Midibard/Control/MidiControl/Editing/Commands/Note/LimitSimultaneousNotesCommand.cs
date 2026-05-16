using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;
using DryWetMidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.limit-simultaneous",
    "Limit Simultaneous Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Chords",
    RequiresSelectedTracks = true)]
public sealed class LimitSimultaneousNotesCommand
    : EditorOperationBase, IEditorCommand<LimitSimultaneousNotesCommandOptions, MidiForgeLimitSimultaneousNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, LimitSimultaneousNotesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeLimitSimultaneousNotesResult> Execute(
        EditorCommandContext context,
        LimitSimultaneousNotesCommandOptions commandOptions)
    {
        var file = context.File;
        var options = commandOptions.Options ?? new MidiForgeLimitSimultaneousNotesOptions();
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderByDescending(index => index)
            .ToArray();
        var maximumActiveNotes = Math.Clamp(options.MaximumActiveNotes, 1, 8);
        var sourceTracks = 0;
        var createdTracks = 0;
        var replacedTracks = 0;
        var removedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var notesToRemove = options.LimitMode == MidiForgeSimultaneousLimitMode.SameStartChordsOnly
                ? SelectSameStartNotesToRemove(notes, maximumActiveNotes, options.KeepPolicy)
                : SelectActiveOverlappingNotesToRemove(notes, maximumActiveNotes, options.KeepPolicy);
            if (notesToRemove.Count == 0)
                continue;

            sourceTracks++;
            removedNotes += notesToRemove.Count;

            var keptNotes = notes
                .Where(note => !notesToRemove.Contains(note))
                .ToArray();
            var outputChunk = MidiForgeNotePrimitives.CreateTrackFromNotes(
                sourceChunk,
                $"{track.DisplayName} (Limited Max {maximumActiveNotes})",
                keptNotes);
            var outputTrack = new EditableTrack(outputChunk, options.CreateNewTracks ? trackIndex + 1 : trackIndex);

            if (options.CreateNewTracks)
            {
                file.Tracks.Insert(trackIndex + 1, outputTrack);
                createdTracks++;
            }
            else
            {
                track.Dispose();
                file.Tracks[trackIndex] = outputTrack;
                replacedTracks++;
            }
        }

        var result = new MidiForgeLimitSimultaneousNotesResult(
            sourceTracks,
            createdTracks,
            replacedTracks,
            removedNotes);
        if (createdTracks == 0 && replacedTracks == 0)
            return EditorCommandResult<MidiForgeLimitSimultaneousNotesResult>.UnchangedResult(
                result,
                BuildResultMessage(result));

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeLimitSimultaneousNotesResult>.ChangedResult(
            result,
            BuildResultMessage(result),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: !options.CreateNewTracks,
                ReloadEventList: !options.CreateNewTracks,
                ClearTrackSelection: true,
                ClearEventSelection: !options.CreateNewTracks,
                RebuildPreview: true));
    }

    public static string BuildResultMessage(MidiForgeLimitSimultaneousNotesResult result)
        => result.RemovedNotes <= 0
            ? "Limit Simultaneous Notes found no notes to remove."
            : $"Limit Simultaneous Notes removed {result.RemovedNotes} note(s) across {result.SourceTracks} track(s); " +
              $"created {result.CreatedTracks} track(s), replaced {result.ReplacedTracks} track(s).";

    private static HashSet<DryWetMidiNote> SelectSameStartNotesToRemove(
        IEnumerable<DryWetMidiNote> notes,
        int maximumActiveNotes,
        MidiForgeNoteKeepPolicy keepPolicy)
    {
        var notesToRemove = new HashSet<DryWetMidiNote>();
        foreach (var group in notes.GroupBy(note => note.Time))
        {
            var groupNotes = group.ToArray();
            if (groupNotes.Length <= maximumActiveNotes)
                continue;

            var keep = SelectNotesToKeep(groupNotes, maximumActiveNotes, keepPolicy).ToHashSet();
            foreach (var note in groupNotes)
            {
                if (!keep.Contains(note))
                    notesToRemove.Add(note);
            }
        }

        return notesToRemove;
    }

    private static HashSet<DryWetMidiNote> SelectActiveOverlappingNotesToRemove(
        IReadOnlyCollection<DryWetMidiNote> notes,
        int maximumActiveNotes,
        MidiForgeNoteKeepPolicy keepPolicy)
    {
        var positiveLengthNotes = notes
            .Where(note => note.Length > 0)
            .ToArray();
        var notesToRemove = new HashSet<DryWetMidiNote>();
        foreach (var tick in positiveLengthNotes
            .SelectMany(note => new[] { note.Time, note.EndTime })
            .Distinct()
            .OrderBy(tick => tick))
        {
            var activeNotes = positiveLengthNotes
                .Where(note => !notesToRemove.Contains(note) && note.Time <= tick && tick < note.EndTime)
                .ToArray();
            if (activeNotes.Length <= maximumActiveNotes)
                continue;

            var keep = SelectNotesToKeep(activeNotes, maximumActiveNotes, keepPolicy).ToHashSet();
            foreach (var note in activeNotes)
            {
                if (!keep.Contains(note))
                    notesToRemove.Add(note);
            }
        }

        return notesToRemove;
    }

    private static IEnumerable<DryWetMidiNote> SelectNotesToKeep(
        IReadOnlyCollection<DryWetMidiNote> notes,
        int maximumActiveNotes,
        MidiForgeNoteKeepPolicy keepPolicy)
    {
        return keepPolicy switch
        {
            MidiForgeNoteKeepPolicy.Lowest => notes
                .OrderBy(note => (byte)note.NoteNumber)
                .ThenByDescending(note => note.Length)
                .ThenBy(note => note.Time)
                .Take(maximumActiveNotes),
            MidiForgeNoteKeepPolicy.Middle => SelectMiddleNotes(notes, maximumActiveNotes),
            _ => notes
                .OrderByDescending(note => (byte)note.NoteNumber)
                .ThenByDescending(note => note.Length)
                .ThenBy(note => note.Time)
                .Take(maximumActiveNotes),
        };
    }

    private static IEnumerable<DryWetMidiNote> SelectMiddleNotes(
        IReadOnlyCollection<DryWetMidiNote> notes,
        int maximumActiveNotes)
    {
        var sortedPitches = notes
            .Select(note => (int)(byte)note.NoteNumber)
            .OrderBy(noteNumber => noteNumber)
            .ToArray();
        var median = sortedPitches.Length % 2 == 1
            ? sortedPitches[sortedPitches.Length / 2]
            : (sortedPitches[(sortedPitches.Length / 2) - 1] + sortedPitches[sortedPitches.Length / 2]) / 2d;

        return notes
            .OrderBy(note => Math.Abs((byte)note.NoteNumber - median))
            .ThenByDescending(note => note.Length)
            .ThenBy(note => note.Time)
            .ThenBy(note => (byte)note.NoteNumber)
            .Take(maximumActiveNotes);
    }
}

public sealed record LimitSimultaneousNotesCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeLimitSimultaneousNotesOptions Options);
