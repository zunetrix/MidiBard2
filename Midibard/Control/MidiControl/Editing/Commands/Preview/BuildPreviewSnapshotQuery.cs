using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Time;

namespace MidiBard.Control.MidiControl.Editing.Commands.Preview;

public sealed record PreviewSnapshot(
    IReadOnlyList<PreviewTrackSnapshot> Tracks,
    double MaxTimeSeconds);

public sealed record PreviewTrackSnapshot(
    int TrackIndex,
    bool IsConductorTrack,
    IReadOnlyList<PreviewNoteSnapshot> Notes);

public readonly record struct PreviewNoteSnapshot(
    double StartSeconds,
    double EndSeconds,
    int NoteNumber);

[EditorOperation(
    "preview.build-snapshot",
    "Build Preview Snapshot",
    Kind = EditorOperationKind.PreviewQuery,
    Scope = EditorOperationScope.Preview,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class BuildPreviewSnapshotQuery
    : EditorOperationBase, IPreviewQuery<EditorOperationEmptyOptions, PreviewSnapshot>
{
    public EditorCommandValidation Validate(PreviewQueryContext context, EditorOperationEmptyOptions options)
        => EditorCommandValidation.Success;

    public PreviewQueryResult<PreviewSnapshot> Execute(
        PreviewQueryContext context,
        EditorOperationEmptyOptions options)
    {
        var file = context.File;
        var tempoMap = file.TempoMap;
        var tracks = new List<PreviewTrackSnapshot>(file.Tracks.Count);
        var maxTimeSeconds = 0.0;

        for (var i = 0; i < file.Tracks.Count; i++)
        {
            var track = file.Tracks[i];
            var notes = track.Chunk.GetNotes()
                .Select(note =>
                {
                    var startSeconds = note.TimeAs<MetricTimeSpan>(tempoMap).GetTotalSeconds();
                    var endSeconds = note.EndTimeAs<MetricTimeSpan>(tempoMap).GetTotalSeconds();
                    if (endSeconds > maxTimeSeconds)
                        maxTimeSeconds = endSeconds;

                    return new PreviewNoteSnapshot(
                        startSeconds,
                        endSeconds,
                        (int)note.NoteNumber);
                })
                .ToArray();

            tracks.Add(new PreviewTrackSnapshot(
                i,
                track.IsConductorTrack,
                notes));
        }

        if (maxTimeSeconds <= 0)
            maxTimeSeconds = 10;

        return new PreviewQueryResult<PreviewSnapshot>(
            new PreviewSnapshot(tracks, maxTimeSeconds));
    }
}
