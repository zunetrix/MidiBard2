using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.difference-tracks",
    "Difference Tracks",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Comparison",
    RequiresSelectedTracks = true)]
public sealed class DifferenceTracksCommand
    : EditorOperationBase, IEditorCommand<DifferenceTracksCommandOptions, MidiForgeDifferenceTracksResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, DifferenceTracksCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least two tracks.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeDifferenceTracksResult> Execute(
        EditorCommandContext context,
        DifferenceTracksCommandOptions options)
    {
        var file = context.File;
        var validTrackIndices = options.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        if (validTrackIndices.Length < 2 || !validTrackIndices.Contains(options.TargetTrackIndex))
        {
            return EditorCommandResult<MidiForgeDifferenceTracksResult>.UnchangedResult(
                new MidiForgeDifferenceTracksResult(validTrackIndices.Length, 0, 0, 0));
        }

        var targetTrack = file.Tracks[options.TargetTrackIndex];
        var sourceChunk = targetTrack.CloneCurrentChunk();
        var targetNotes = sourceChunk.GetNotes().ToArray();
        var comparisonNotes = validTrackIndices
            .Where(index => index != options.TargetTrackIndex)
            .SelectMany(index => file.Tracks[index].CloneCurrentChunk().GetNotes())
            .ToArray();

        if (targetNotes.Length == 0 || comparisonNotes.Length == 0)
        {
            return EditorCommandResult<MidiForgeDifferenceTracksResult>.UnchangedResult(
                new MidiForgeDifferenceTracksResult(validTrackIndices.Length, 0, 0, 0));
        }

        var diffNotes = targetNotes
            .Where(note => !comparisonNotes.Any(comparison => MidiForgeNotePrimitives.NotesOverlap(note, comparison)))
            .Select(note => MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length))
            .ToArray();
        var restNotes = targetNotes
            .Where(note => comparisonNotes.Any(comparison => MidiForgeNotePrimitives.NotesOverlap(note, comparison)))
            .Select(note => MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length))
            .ToArray();

        var createdTracks = 0;
        createdTracks += MidiForgeNotePrimitives.InsertDerivedTrackAfterTarget(
            file,
            options.TargetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Diff Rest)",
            restNotes);
        createdTracks += MidiForgeNotePrimitives.InsertDerivedTrackAfterTarget(
            file,
            options.TargetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Diff)",
            diffNotes);

        var result = new MidiForgeDifferenceTracksResult(
            validTrackIndices.Length,
            createdTracks,
            diffNotes.Length,
            restNotes.Length);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeDifferenceTracksResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeDifferenceTracksResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record DifferenceTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    int TargetTrackIndex);
