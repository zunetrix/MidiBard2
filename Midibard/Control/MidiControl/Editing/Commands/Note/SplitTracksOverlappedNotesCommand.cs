using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-overlapped-notes",
    "Split Overlapped Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Overlaps",
    RequiresSelectedTracks = true)]
public sealed class SplitTracksOverlappedNotesCommand
    : EditorOperationBase, IEditorCommand<SplitTracksOverlappedNotesCommandOptions, MidiForgeSplitOverlappedNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitTracksOverlappedNotesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSplitOverlappedNotesResult> Execute(
        EditorCommandContext context,
        SplitTracksOverlappedNotesCommandOptions options)
    {
        var file = context.File;
        var validTrackIndices = options.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var sourceTracks = 0;
        var createdTracks = 0;
        var overlapGroups = 0;
        var overlappedNotes = 0;
        var nonOverlappedNotes = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
                continue;

            var groups = notes
                .GroupBy(note => ((int)(byte)note.NoteNumber, note.Time))
                .OrderBy(group => group.Key.Time)
                .ThenBy(group => group.Key.Item1)
                .ToArray();
            var duplicateGroups = groups
                .Where(group => group.Count() >= 2)
                .ToArray();
            if (duplicateGroups.Length == 0)
                continue;

            var trackGroups = new Dictionary<string, List<MidiNote>>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var groupNotes = group.ToArray();
                var isOverlapped = groupNotes.Length >= 2;

                for (var i = 0; i < groupNotes.Length; i++)
                {
                    var trackName = isOverlapped
                        ? $"{track.DisplayName} overlap ({i + 1})"
                        : $"{track.DisplayName} no overlap";

                    if (!trackGroups.TryGetValue(trackName, out var splitNotes))
                    {
                        splitNotes = new List<MidiNote>();
                        trackGroups.Add(trackName, splitNotes);
                    }

                    splitNotes.Add(MidiForgeNotePrimitives.CloneNoteWithLength(groupNotes[i], groupNotes[i].Length));
                }
            }

            foreach (var (trackName, splitNotes) in trackGroups
                .OrderBy(pair => pair.Key.Contains(" no overlap", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (splitNotes.Count == 0)
                    continue;

                file.Tracks.Add(new EditableTrack(
                    MidiForgeNotePrimitives.CreateTrackFromNotes(sourceChunk, trackName, splitNotes),
                    file.Tracks.Count));
                createdTracks++;
            }

            sourceTracks++;
            overlapGroups += duplicateGroups.Length;
            overlappedNotes += duplicateGroups.Sum(group => group.Count());
            nonOverlappedNotes += groups.Where(group => group.Count() == 1).Sum(group => group.Count());
        }

        var result = new MidiForgeSplitOverlappedNotesResult(
            sourceTracks,
            createdTracks,
            overlapGroups,
            overlappedNotes,
            nonOverlappedNotes);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeSplitOverlappedNotesResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeSplitOverlappedNotesResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record SplitTracksOverlappedNotesCommandOptions(IReadOnlyList<int> TrackIndices);
