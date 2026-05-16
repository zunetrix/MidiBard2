using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.split-equal-notes",
    "Split Equal Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Comparison",
    RequiresSelectedTracks = true)]
public sealed class SplitTracksEqualNotesCommand
    : EditorOperationBase, IEditorCommand<SplitTracksEqualNotesCommandOptions, MidiForgeSplitEqualNotesResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SplitTracksEqualNotesCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least two tracks.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSplitEqualNotesResult> Execute(
        EditorCommandContext context,
        SplitTracksEqualNotesCommandOptions options)
    {
        var file = context.File;
        var validTrackIndices = options.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        if (validTrackIndices.Length < 2 || !validTrackIndices.Contains(options.TargetTrackIndex))
        {
            return EditorCommandResult<MidiForgeSplitEqualNotesResult>.UnchangedResult(
                new MidiForgeSplitEqualNotesResult(validTrackIndices.Length, 0, 0, 0));
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
            return EditorCommandResult<MidiForgeSplitEqualNotesResult>.UnchangedResult(
                new MidiForgeSplitEqualNotesResult(validTrackIndices.Length, 0, 0, 0));
        }

        var equalNotes = targetNotes
            .Where(note => comparisonNotes.Any(comparison => MidiForgeNotePrimitives.IsEqualNoteAtStart(note, comparison)))
            .Select(note => MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length))
            .ToArray();
        var nonEqualNotes = targetNotes
            .Where(note => !comparisonNotes.Any(comparison => MidiForgeNotePrimitives.IsEqualNoteAtStart(note, comparison)))
            .Select(note => MidiForgeNotePrimitives.CloneNoteWithLength(note, note.Length))
            .ToArray();

        var createdTracks = 0;
        createdTracks += MidiForgeNotePrimitives.InsertDerivedTrackAfterTarget(
            file,
            options.TargetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Equal Notes)",
            equalNotes);
        createdTracks += MidiForgeNotePrimitives.InsertDerivedTrackAfterTarget(
            file,
            options.TargetTrackIndex,
            sourceChunk,
            $"{targetTrack.DisplayName} (Non Equal Notes)",
            nonEqualNotes);

        var result = new MidiForgeSplitEqualNotesResult(
            validTrackIndices.Length,
            createdTracks,
            equalNotes.Length,
            nonEqualNotes.Length);
        if (createdTracks == 0)
            return EditorCommandResult<MidiForgeSplitEqualNotesResult>.UnchangedResult(result);

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);
        return EditorCommandResult<MidiForgeSplitEqualNotesResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                RebuildPreview: true));
    }
}

public sealed record SplitTracksEqualNotesCommandOptions(
    IReadOnlyList<int> TrackIndices,
    int TargetTrackIndex);
