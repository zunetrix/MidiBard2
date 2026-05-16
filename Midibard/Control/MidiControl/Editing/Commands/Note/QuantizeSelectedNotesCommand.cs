using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;
using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackTransformCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Note;

[EditorOperation(
    "note.quantize-selected",
    "Quantize Selected Notes",
    Scope = EditorOperationScope.Note,
    MenuPath = "Note/Timing",
    RequiresSelectedEvents = true)]
public sealed class QuantizeSelectedNotesCommand
    : EditorOperationBase, IEditorCommand<QuantizeSelectedNotesOptions, NoteMutationResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, QuantizeSelectedNotesOptions options)
    {
        if (!IsPerformanceTrackIndex(context.File, options.TrackIndex))
            return EditorCommandValidation.Failure("Choose a performance track.");

        if (options.SelectedNotes is null || options.SelectedNotes.Count == 0)
            return EditorCommandValidation.Failure("Choose at least one note.");

        return options.Grid is null || options.Settings is null
            ? EditorCommandValidation.Failure("Choose quantize settings.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<NoteMutationResult> Execute(
        EditorCommandContext context,
        QuantizeSelectedNotesOptions options)
    {
        var selectedNotes = new HashSet<(long tick, byte noteNum, byte channel)>(options.SelectedNotes);
        var track = context.File.Tracks[options.TrackIndex];
        track.FlushChanges();
        var beforeNotes = GetNoteStateSnapshot(track.Chunk);

        var settings = new QuantizingSettings
        {
            Target = options.Settings.Target,
            QuantizingLevel = options.Settings.QuantizingLevel,
            FixOppositeEnd = options.Settings.FixOppositeEnd,
            QuantizingBeyondZeroPolicy = options.Settings.QuantizingBeyondZeroPolicy,
            QuantizingBeyondFixedEndPolicy = options.Settings.QuantizingBeyondFixedEndPolicy,
            Filter = obj => obj is Melanchall.DryWetMidi.Interaction.Note note
                            && selectedNotes.Contains((note.Time, (byte)note.NoteNumber, (byte)note.Channel)),
        };

        QuantizerUtilities.QuantizeObjects(
            track.Chunk,
            ObjectType.Note,
            options.Grid,
            context.File.TempoMap,
            settings);
        var changed = !beforeNotes.SequenceEqual(GetNoteStateSnapshot(track.Chunk));
        var result = new NoteMutationResult(changed ? selectedNotes.Count : 0);

        if (!changed)
            return EditorCommandResult<NoteMutationResult>.UnchangedResult(result);

        return EditorCommandResult<NoteMutationResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadSelectedTrack: true,
                ReloadEventList: true,
                ClearEventSelection: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}

public sealed record QuantizeSelectedNotesOptions(
    int TrackIndex,
    IReadOnlyCollection<(long tick, byte noteNum, byte channel)> SelectedNotes,
    IGrid Grid,
    QuantizingSettings Settings);

public sealed record NoteMutationResult(
    int ChangedEvents,
    int InsertedEventIndex = -1,
    IReadOnlyList<int> AffectedEventIndices = null);
