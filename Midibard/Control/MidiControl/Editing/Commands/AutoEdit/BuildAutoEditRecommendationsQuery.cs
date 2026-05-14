using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;

public sealed record MidiForgeAutoEditTrackRecommendation(
    int TrackIndex,
    string TrackName,
    bool HasNotes,
    bool WillEdit,
    int SourceTracks,
    int CreatedTracks,
    int ReplacedTracks,
    int PickedParts,
    int ChangedNotes,
    string Reason);

public sealed record MidiForgeAutoEditRecommendationsResult(
    int SelectedTracks,
    int SourceTracks,
    int TracksToEdit,
    int CreatedTracks,
    int ReplacedTracks,
    int PickedParts,
    int ChangedNotes,
    bool WillCreateNewTracks,
    bool WillAdaptOutOfRangeNotes,
    IReadOnlyList<MidiForgeAutoEditTrackRecommendation> Tracks);

[EditorOperation(
    "auto-edit.build-recommendations",
    "Build Auto-Edit Recommendations",
    Kind = EditorOperationKind.Query,
    Scope = EditorOperationScope.AutoEdit,
    MenuPath = "Forge/Auto Edit",
    RequiresSelectedTracks = true,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class BuildAutoEditRecommendationsQuery
    : EditorOperationBase, IEditorQuery<BuildAutoEditRecommendationsQueryOptions, MidiForgeAutoEditRecommendationsResult>
{
    public EditorCommandValidation Validate(EditorQueryContext context, BuildAutoEditRecommendationsQueryOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorQueryResult<MidiForgeAutoEditRecommendationsResult> Execute(
        EditorQueryContext context,
        BuildAutoEditRecommendationsQueryOptions queryOptions)
    {
        var file = context.File;
        var options = queryOptions.Options ?? new MidiForgeAutoEditOptions();
        var validTrackIndices = queryOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        var trackRecommendations = new List<MidiForgeAutoEditTrackRecommendation>();

        foreach (var trackIndex in validTrackIndices)
        {
            var track = file.Tracks[trackIndex];
            var notes = track.CloneCurrentChunk().GetNotes().ToArray();
            if (notes.Length == 0)
            {
                trackRecommendations.Add(new MidiForgeAutoEditTrackRecommendation(
                    trackIndex,
                    track.DisplayName,
                    HasNotes: false,
                    WillEdit: false,
                    SourceTracks: 0,
                    CreatedTracks: 0,
                    ReplacedTracks: 0,
                    PickedParts: 0,
                    ChangedNotes: 0,
                    Reason: "Track has no notes."));
                continue;
            }

            var simulatedFile = CloneForSimulation(file);
            var simulatedResult = MidiForgeOperations.AutoEditTracks(
                simulatedFile,
                new[] { trackIndex },
                options);
            var willEdit = simulatedResult.CreatedTracks > 0 || simulatedResult.ReplacedTracks > 0;

            trackRecommendations.Add(new MidiForgeAutoEditTrackRecommendation(
                trackIndex,
                track.DisplayName,
                HasNotes: true,
                willEdit,
                simulatedResult.SourceTracks,
                simulatedResult.CreatedTracks,
                simulatedResult.ReplacedTracks,
                simulatedResult.PickedParts,
                simulatedResult.ChangedNotes,
                GetReason(willEdit, options.AdaptOutOfRangeNotes, simulatedResult.ChangedNotes)));
        }

        var result = new MidiForgeAutoEditRecommendationsResult(
            validTrackIndices.Length,
            trackRecommendations.Sum(track => track.SourceTracks),
            trackRecommendations.Count(track => track.WillEdit),
            trackRecommendations.Sum(track => track.CreatedTracks),
            trackRecommendations.Sum(track => track.ReplacedTracks),
            trackRecommendations.Sum(track => track.PickedParts),
            trackRecommendations.Sum(track => track.ChangedNotes),
            options.CreateNewTracks,
            options.AdaptOutOfRangeNotes,
            trackRecommendations);

        return new EditorQueryResult<MidiForgeAutoEditRecommendationsResult>(result);
    }

    private static EditableMidiFile CloneForSimulation(EditableMidiFile file)
    {
        var clone = new MidiFile(file.CloneTrackChunksForSnapshot())
        {
            TimeDivision = file.Source.TimeDivision,
        };

        return new EditableMidiFile(clone, file.FilePath, file.DisplayName);
    }

    private static string GetReason(bool willEdit, bool adaptOutOfRangeNotes, int changedNotes)
    {
        if (!willEdit)
            return "No chord lines matched the auto-edit settings.";

        if (!adaptOutOfRangeNotes)
            return "Will pick chord lines without range adaptation.";

        return changedNotes > 0
            ? $"Will pick chord lines and adapt {changedNotes} out-of-range note(s)."
            : "Will pick chord lines; picked notes already fit C3-C6.";
    }
}

public sealed record BuildAutoEditRecommendationsQueryOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeAutoEditOptions Options);
