using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands.Drum;
using MidiBard.Control.MidiControl.Editing.Commands.Track;

namespace MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;

[EditorOperation(
    "auto-edit.prepare-for-playback",
    "Prepare for Playback",
    Scope = EditorOperationScope.AutoEdit,
    MenuPath = "Forge/Auto Edit",
    SortOrder = 100)]
public sealed class PrepareForPlaybackCommand
    : EditorOperationBase, IEditorCommand<PrepareForPlaybackCommandOptions, MidiForgePrepareForPlaybackResult>
{
    private static readonly MidiForgeAutoEditResult EmptyAutoEditResult = new(0, 0, 0, 0, 0);

    private static readonly EditorRefreshHints PrepareRefreshHints = new(
        ReloadTrackList: true,
        ReloadSelectedTrack: true,
        ReloadEventList: true,
        ClearTrackSelection: true,
        ClearEventSelection: true,
        ClearSelectedTrack: true,
        RebuildPreview: true,
        RecalculateMetrics: true);

    public EditorCommandValidation Validate(EditorCommandContext context, PrepareForPlaybackCommandOptions options)
        => EditorCommandValidation.Success;

    public EditorCommandResult<MidiForgePrepareForPlaybackResult> Execute(
        EditorCommandContext context,
        PrepareForPlaybackCommandOptions commandOptions)
    {
        var file = context.File;
        var options = commandOptions.Options ?? new MidiForgePrepareForPlaybackOptions();
        var trackIndices = commandOptions.TrackIndices;
        var sourceTracks = GetPerformanceTrackIndices(file, trackIndices).Length;
        var trackNameTransposeTracks = 0;
        var trackNameTransposeChangedNotes = 0;
        var mappedInstrumentTracks = 0;
        var drumSourceTracks = 0;
        var drumTracksCreated = 0;
        var drumSourceTracksDeleted = 0;
        var drumRestTracks = 0;
        var drumAutoEditedTracks = 0;
        var drumTransposedNotes = 0;
        var autoEditResult = EmptyAutoEditResult;
        var changed = false;

        if (options.ApplyTrackNameTransposes)
        {
            var transposedTrackIndices = GetPerformanceTrackIndices(file, trackIndices)
                .Where(index => TrackInfo.GetTransposeByName(file.Tracks[index].Name) != 0)
                .ToArray();
            var transposeExecution = context.Invoker.Execute(
                new ApplyTrackNameTransposesCommand(),
                new ApplyTrackNameTransposesCommandOptions(
                    transposedTrackIndices,
                    new MidiForgeApplyTrackNameTransposeOptions(CreateNewTracks: false)));

            if (!transposeExecution.Succeeded)
                return FinishChildFailure(transposeExecution.Message);

            var transposeResult = transposeExecution.Result!.Value;
            trackNameTransposeTracks = transposeResult.SourceTracks;
            trackNameTransposeChangedNotes = transposeResult.ChangedNotes;
            changed |= transposeExecution.Changed;
        }

        if (options.MapInstruments)
        {
            var mapExecution = context.Invoker.Execute(
                new MapInstrumentsCommand(),
                new MapInstrumentsCommandOptions(
                    GetPerformanceTrackIndices(file, trackIndices),
                    new MidiForgeMapInstrumentsOptions(
                        options.MapInstrumentsMode,
                        IncludeDrumTracks: true,
                        NameSource: options.MapInstrumentsNameSource)));

            if (!mapExecution.Succeeded)
                return FinishChildFailure(mapExecution.Message);

            mappedInstrumentTracks = mapExecution.Result!.Value.RenamedTracks;
            changed |= mapExecution.Changed;
        }

        if (options.SplitDrumkits)
        {
            var drumExecution = context.Invoker.Execute(
                new SplitDrumkitTracksCommand(),
                new SplitDrumkitTracksCommandOptions(
                    GetDrumOnlyPerformanceTrackIndices(file, trackIndices),
                    new MidiForgeSplitDrumkitOptions(
                        AutoEditAfterSplit: true,
                        CreateRestTrack: true,
                        MoveSourceTracksToEnd: false,
                        TransposePreset: options.DrumTransposePreset,
                        DeleteOriginalTracks: true)));

            if (!drumExecution.Succeeded)
                return FinishChildFailure(drumExecution.Message);

            var drumResult = drumExecution.Result!.Value;
            drumSourceTracks = drumResult.SourceTracks;
            drumTracksCreated = drumResult.CreatedTracks;
            drumSourceTracksDeleted = drumResult.DeletedSourceTracks;
            drumRestTracks = drumResult.RestTracks;
            drumAutoEditedTracks = drumResult.AutoEditedTracks;
            drumTransposedNotes = drumResult.TransposedNotes;
            changed |= drumExecution.Changed;
        }

        var autoEditExecution = context.Invoker.Execute(
            new AutoEditSelectedTracksCommand(),
            new AutoEditSelectedTracksCommandOptions(
                GetNonDrumPerformanceTrackIndices(file, trackIndices),
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: options.MaxSimultaneousNotes,
                    PickStrategy: options.PickStrategy,
                    AdaptOutOfRangeNotes: true,
                    CreateNewTracks: false,
                    RangeStrategy: options.RangeStrategy,
                    RenameTracks: false,
                    ChordTimingTolerance: options.ChordTimingTolerance)));

        if (!autoEditExecution.Succeeded)
            return FinishChildFailure(autoEditExecution.Message);

        autoEditResult = autoEditExecution.Result!.Value;
        changed |= autoEditExecution.Changed;

        var result = BuildResult();
        return changed
            ? EditorCommandResult<MidiForgePrepareForPlaybackResult>.ChangedResult(
                result,
                refreshHints: PrepareRefreshHints)
            : EditorCommandResult<MidiForgePrepareForPlaybackResult>.UnchangedResult(result);

        EditorCommandResult<MidiForgePrepareForPlaybackResult> FinishChildFailure(string message)
        {
            var result = BuildResult();
            return changed
                ? EditorCommandResult<MidiForgePrepareForPlaybackResult>.ChangedResult(
                    result,
                    message,
                    PrepareRefreshHints)
                : EditorCommandResult<MidiForgePrepareForPlaybackResult>.NoChange(message);
        }

        MidiForgePrepareForPlaybackResult BuildResult()
            => new(
                sourceTracks,
                trackNameTransposeTracks,
                trackNameTransposeChangedNotes,
                mappedInstrumentTracks,
                drumSourceTracks,
                drumTracksCreated,
                drumSourceTracksDeleted,
                drumRestTracks,
                drumAutoEditedTracks,
                drumTransposedNotes,
                autoEditResult.SourceTracks,
                autoEditResult.ReplacedTracks,
                autoEditResult.PickedParts,
                autoEditResult.ChangedNotes);
    }

    private static int[] GetPerformanceTrackIndices(EditableMidiFile file, IReadOnlyList<int>? trackIndices = null)
    {
        var source = trackIndices != null && trackIndices.Count > 0
            ? trackIndices
            : Enumerable.Range(0, file.Tracks.Count);
        return MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(file, source);
    }

    private static int[] GetDrumOnlyPerformanceTrackIndices(EditableMidiFile file, IReadOnlyList<int>? trackIndices = null)
        => GetPerformanceTrackIndices(file, trackIndices)
            .Where(index => IsDrumOnlyTrack(file.Tracks[index]))
            .ToArray();

    private static int[] GetNonDrumPerformanceTrackIndices(EditableMidiFile file, IReadOnlyList<int>? trackIndices = null)
        => GetPerformanceTrackIndices(file, trackIndices)
            .Where(index => !HasDrumNotes(file.Tracks[index]))
            .ToArray();

    private static bool IsDrumOnlyTrack(EditableTrack track)
    {
        var notes = track.CloneCurrentChunk().GetNotes().ToArray();
        return notes.Length > 0 && notes.All(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel);
    }

    private static bool HasDrumNotes(EditableTrack track)
        => track.CloneCurrentChunk()
            .GetNotes()
            .Any(note => (byte)note.Channel == MidiForgeAnalysis.DrumChannel);
}

public sealed record PrepareForPlaybackCommandOptions(
    MidiForgePrepareForPlaybackOptions Options,
    IReadOnlyList<int>? TrackIndices = null);
