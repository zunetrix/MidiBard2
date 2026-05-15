using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackCrudCommandHelpers;
using static MidiBard.Control.MidiControl.Editing.Commands.Track.TrackTransformCommandHelpers;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

public sealed record TrackTransformResult(
    int SourceTracks,
    int ChangedTracks,
    int ChangedNotes,
    IReadOnlyList<int> CreatedTrackIndices,
    IReadOnlyList<int> RemovedTrackIndices);

[EditorOperation(
    "track.transpose",
    "Transpose Tracks",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Transform",
    RequiresSelectedTracks = true)]
public sealed class TransposeTracksCommand
    : EditorOperationBase, IEditorCommand<TransposeTracksOptions, TrackTransformResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, TransposeTracksOptions options)
        => ValidatePerformanceTrackSelection(context, options.TrackIndices);

    public EditorCommandResult<TrackTransformResult> Execute(
        EditorCommandContext context,
        TransposeTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        if (options.Semitones == 0)
        {
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(
                EmptyResult(validTrackIndices.Length));
        }

        var beforeCount = file.Tracks.Count;
        var changedNotes = TransposeTracks(
            file,
            validTrackIndices,
            options);

        var createdTrackIndices = options.CreateNewTracks
            ? EstimateInsertedTrackIndices(validTrackIndices, beforeCount, file.Tracks.Count)
            : Array.Empty<int>();
        var changedTracks = options.CreateNewTracks
            ? createdTrackIndices.Length
            : changedNotes > 0 ? validTrackIndices.Length : 0;
        var result = new TrackTransformResult(
            validTrackIndices.Length,
            changedTracks,
            changedNotes,
            createdTrackIndices,
            Array.Empty<int>());

        if (changedTracks == 0 && changedNotes == 0)
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(result);

        return EditorCommandResult<TrackTransformResult>.ChangedResult(
            result,
            refreshHints: TrackTransformHints(options.CreateNewTracks));
    }

    private static TrackTransformResult EmptyResult(int sourceTracks)
        => new(sourceTracks, 0, 0, Array.Empty<int>(), Array.Empty<int>());
}

public sealed record TransposeTracksOptions(
    IReadOnlyList<int> TrackIndices,
    int Semitones,
    int MinimumNoteNumber = 0,
    int MaximumNoteNumber = 127,
    bool CreateNewTracks = false);

[EditorOperation(
    "track.merge",
    "Merge Tracks",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Transform",
    RequiresSelectedTracks = true)]
public sealed class MergeTracksCommand
    : EditorOperationBase, IEditorCommand<MergeTracksOptions, TrackTransformResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, MergeTracksOptions options)
    {
        var selectionValidation = ValidatePerformanceTrackSelection(context, options.TrackIndices);
        if (!selectionValidation.IsValid)
            return selectionValidation;

        var validTrackIndices = GetPerformanceTrackIndices(context.File, options.TrackIndices);
        if (validTrackIndices.Length < 2)
            return EditorCommandValidation.Failure("Choose at least two performance tracks to merge.");

        if (!validTrackIndices.Contains(options.TargetTrackIndex))
            return EditorCommandValidation.Failure("Choose one selected performance track as the merge target.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackTransformResult> Execute(
        EditorCommandContext context,
        MergeTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        var newTrackIndex = MergeTracks(file, options.TargetTrackIndex, validTrackIndices, options);

        var result = new TrackTransformResult(
            validTrackIndices.Length,
            newTrackIndex >= 0 ? 1 : 0,
            0,
            newTrackIndex >= 0 ? new[] { newTrackIndex } : Array.Empty<int>(),
            options.DeleteOriginalTracks && newTrackIndex >= 0
                ? validTrackIndices
                : Array.Empty<int>());

        if (newTrackIndex < 0)
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(result);

        return EditorCommandResult<TrackTransformResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true,
                RecalculateMetrics: true));
    }
}

public sealed record MergeTracksOptions(
    int TargetTrackIndex,
    IReadOnlyList<int> TrackIndices,
    bool IncludeProgramChanges,
    bool IncludePitchBends,
    bool IncludeControlChanges,
    int ToleranceMilliseconds,
    bool RemoveEqualNotes,
    bool DeleteOriginalTracks);

[EditorOperation(
    "track.quantize",
    "Quantize Tracks",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Transform",
    RequiresSelectedTracks = true)]
public sealed class QuantizeTracksCommand
    : EditorOperationBase, IEditorCommand<QuantizeTracksOptions, TrackTransformResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, QuantizeTracksOptions options)
    {
        var selectionValidation = ValidatePerformanceTrackSelection(context, options.TrackIndices);
        if (!selectionValidation.IsValid)
            return selectionValidation;

        return options.Grid is null || options.Settings is null
            ? EditorCommandValidation.Failure("Choose quantize settings.")
            : EditorCommandValidation.Success;
    }

    public EditorCommandResult<TrackTransformResult> Execute(
        EditorCommandContext context,
        QuantizeTracksOptions options)
    {
        var file = context.File;
        var validTrackIndices = GetPerformanceTrackIndices(file, options.TrackIndices);
        var beforeCount = file.Tracks.Count;
        var changedTracks = QuantizeTracks(file, validTrackIndices, options);
        var createdTrackIndices = options.CreateNewTracks
            ? EstimateInsertedTrackIndices(validTrackIndices, beforeCount, file.Tracks.Count)
            : Array.Empty<int>();
        var result = new TrackTransformResult(
            validTrackIndices.Length,
            changedTracks,
            0,
            createdTrackIndices,
            Array.Empty<int>());

        if (changedTracks == 0)
            return EditorCommandResult<TrackTransformResult>.UnchangedResult(result);

        return EditorCommandResult<TrackTransformResult>.ChangedResult(
            result,
            refreshHints: TrackTransformHints(options.CreateNewTracks));
    }
}

public sealed record QuantizeTracksOptions(
    IReadOnlyList<int> TrackIndices,
    IGrid Grid,
    QuantizingSettings Settings,
    bool CreateNewTracks);

internal static class TrackTransformCommandHelpers
{
    private static readonly Regex TransposedTrackNamePattern = new(
        @"\s*\(Transposed (-?\d+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int TransposeTracks(
        EditableMidiFile file,
        IReadOnlyList<int> trackIndices,
        TransposeTracksOptions options)
    {
        var changedNotes = 0;
        var minimumNoteNumber = Math.Clamp(options.MinimumNoteNumber, 0, 127);
        var maximumNoteNumber = Math.Clamp(options.MaximumNoteNumber, 0, 127);
        if (minimumNoteNumber > maximumNoteNumber)
            (minimumNoteNumber, maximumNoteNumber) = (maximumNoteNumber, minimumNoteNumber);

        foreach (var trackIndex in trackIndices.OrderByDescending(index => index))
        {
            if (!IsPerformanceTrackIndex(file, trackIndex))
                continue;

            var sourceTrack = file.Tracks[trackIndex];
            sourceTrack.FlushChanges();

            var targetChunk = options.CreateNewTracks
                ? new TrackChunk(sourceTrack.Chunk.Events.Select(midiEvent => midiEvent.Clone()))
                : sourceTrack.Chunk;
            changedNotes += TransposeChunkNotes(
                targetChunk,
                options.Semitones,
                minimumNoteNumber,
                maximumNoteNumber);

            if (!options.CreateNewTracks)
                continue;

            var newTrack = new EditableTrack(targetChunk, trackIndex + 1)
            {
                Name = GetTransposedTrackName(sourceTrack.DisplayName, options.Semitones),
            };
            newTrack.MarkNameDirty();
            file.Tracks.Insert(trackIndex + 1, newTrack);
        }

        if (changedNotes > 0 || options.CreateNewTracks)
            ReindexTracks(file);

        return changedNotes;
    }

    public static int MergeTracks(
        EditableMidiFile file,
        int targetTrackIndex,
        IReadOnlyList<int> selectedTrackIndices,
        MergeTracksOptions options)
    {
        if (!IsPerformanceTrackIndex(file, targetTrackIndex))
            return -1;

        var target = file.Tracks[targetTrackIndex];
        target.FlushChanges();

        var cloneChunk = new TrackChunk(target.Chunk.Events.Select(midiEvent => midiEvent.Clone()));
        var existingNoteKeys = cloneChunk.GetNotes()
            .Select(note => ((byte)note.NoteNumber, note.Time))
            .ToHashSet();

        using (var cloneManager = cloneChunk.ManageTimedEvents())
        {
            foreach (var sourceTrackIndex in selectedTrackIndices.Where(index => index != targetTrackIndex))
            {
                var source = file.Tracks[sourceTrackIndex];
                source.FlushChanges();

                foreach (var note in source.Chunk.GetNotes())
                {
                    if (options.RemoveEqualNotes && !existingNoteKeys.Add(((byte)note.NoteNumber, note.Time)))
                        continue;

                    cloneManager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                        note.Time));
                    cloneManager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                        note.EndTime));
                }

                foreach (var timedEvent in source.Chunk.GetTimedEvents())
                {
                    if (timedEvent.Event is NoteOnEvent or NoteOffEvent)
                        continue;
                    if (timedEvent.Event is not ChannelEvent)
                        continue;
                    if (timedEvent.Event is ProgramChangeEvent && !options.IncludeProgramChanges)
                        continue;
                    if (timedEvent.Event is PitchBendEvent && !options.IncludePitchBends)
                        continue;
                    if (timedEvent.Event is ControlChangeEvent && !options.IncludeControlChanges)
                        continue;

                    cloneManager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), timedEvent.Time));
                }
            }
        }

        if (options.ToleranceMilliseconds > 0)
        {
            Merger.MergeObjects(
                cloneChunk,
                ObjectType.Note,
                file.TempoMap,
                new ObjectsMergingSettings
                {
                    Tolerance = new MetricTimeSpan(options.ToleranceMilliseconds * 1_000L),
                });
        }

        var newTrack = new EditableTrack(cloneChunk, targetTrackIndex + 1)
        {
            Name = $"{target.DisplayName} (merged)",
        };
        newTrack.MarkNameDirty();

        if (options.DeleteOriginalTracks)
        {
            var insertIndex = selectedTrackIndices.Min();
            foreach (var trackIndex in selectedTrackIndices.OrderByDescending(index => index))
            {
                file.Tracks[trackIndex].Dispose();
                file.Tracks.RemoveAt(trackIndex);
            }

            file.Tracks.Insert(insertIndex, newTrack);
        }
        else
        {
            file.Tracks.Insert(targetTrackIndex + 1, newTrack);
        }

        ReindexTracks(file);
        return newTrack.Index;
    }

    public static int QuantizeTracks(
        EditableMidiFile file,
        IReadOnlyList<int> trackIndices,
        QuantizeTracksOptions options)
    {
        var changedTracks = 0;
        foreach (var trackIndex in trackIndices.OrderByDescending(index => index))
        {
            if (!IsPerformanceTrackIndex(file, trackIndex))
                continue;

            var sourceTrack = file.Tracks[trackIndex];
            sourceTrack.FlushChanges();

            var targetChunk = options.CreateNewTracks
                ? new TrackChunk(sourceTrack.Chunk.Events.Select(midiEvent => midiEvent.Clone()))
                : sourceTrack.Chunk;
            var beforeNotes = GetNoteStateSnapshot(targetChunk);

            QuantizerUtilities.QuantizeObjects(
                targetChunk,
                ObjectType.Note,
                options.Grid,
                file.TempoMap,
                options.Settings);
            var notesChanged = !beforeNotes.SequenceEqual(GetNoteStateSnapshot(targetChunk));

            if (options.CreateNewTracks)
            {
                var newTrack = new EditableTrack(targetChunk, trackIndex + 1)
                {
                    Name = $"{sourceTrack.DisplayName} (quantized)",
                };
                newTrack.MarkNameDirty();
                file.Tracks.Insert(trackIndex + 1, newTrack);
                changedTracks++;
            }
            else if (notesChanged)
            {
                changedTracks++;
            }
        }

        if (changedTracks > 0)
            ReindexTracks(file);

        return changedTracks;
    }

    public static NoteStateSnapshot[] GetNoteStateSnapshot(TrackChunk chunk)
        => chunk.GetNotes()
            .Select(note => new NoteStateSnapshot(
                (byte)note.NoteNumber,
                (byte)note.Channel,
                note.Time,
                note.Length))
            .OrderBy(note => note.Time)
            .ThenBy(note => note.NoteNumber)
            .ThenBy(note => note.Channel)
            .ThenBy(note => note.Length)
            .ToArray();

    private static int TransposeChunkNotes(
        TrackChunk chunk,
        int semitones,
        int minimumNoteNumber,
        int maximumNoteNumber)
    {
        var changedNotes = chunk.GetNotes()
            .Count(note => (byte)note.NoteNumber >= minimumNoteNumber
                           && (byte)note.NoteNumber <= maximumNoteNumber);

        foreach (var midiEvent in chunk.Events)
        {
            if (midiEvent is NoteOnEvent noteOn)
                TransposeNoteEvent(noteOn, semitones, minimumNoteNumber, maximumNoteNumber);
            else if (midiEvent is NoteOffEvent noteOff)
                TransposeNoteEvent(noteOff, semitones, minimumNoteNumber, maximumNoteNumber);
        }

        return changedNotes;
    }

    private static void TransposeNoteEvent(
        NoteEvent noteEvent,
        int semitones,
        int minimumNoteNumber,
        int maximumNoteNumber)
    {
        var noteNumber = (byte)noteEvent.NoteNumber;
        if (noteNumber < minimumNoteNumber || noteNumber > maximumNoteNumber)
            return;

        noteEvent.NoteNumber = (SevenBitNumber)(byte)Math.Clamp(noteNumber + semitones, 0, 127);
    }

    private static string GetTransposedTrackName(string trackName, int semitones)
    {
        var match = TransposedTrackNamePattern.Match(trackName);
        var previousTranspose = match.Success && int.TryParse(match.Groups[1].Value, out var parsed)
            ? parsed
            : 0;
        var baseName = TransposedTrackNamePattern.Replace(trackName, string.Empty).Trim();
        return $"{baseName} (Transposed {previousTranspose + semitones})";
    }

    public static int[] EstimateInsertedTrackIndices(
        IReadOnlyList<int> sourceTrackIndices,
        int beforeTrackCount,
        int afterTrackCount)
    {
        var createdCount = Math.Max(0, afterTrackCount - beforeTrackCount);
        if (createdCount == 0)
            return Array.Empty<int>();

        var createdTrackIndices = new List<int>(createdCount);
        foreach (var sourceTrackIndex in sourceTrackIndices
                     .Distinct()
                     .OrderByDescending(index => index)
                     .Take(createdCount))
        {
            var insertedIndex = sourceTrackIndex + 1;
            for (var i = 0; i < createdTrackIndices.Count; i++)
            {
                if (createdTrackIndices[i] >= insertedIndex)
                    createdTrackIndices[i]++;
            }

            createdTrackIndices.Add(insertedIndex);
        }

        return createdTrackIndices.OrderBy(index => index).ToArray();
    }

    public static EditorRefreshHints TrackTransformHints(bool createdNewTracks)
        => new(
            ReloadTrackList: true,
            ReloadSelectedTrack: !createdNewTracks,
            ReloadEventList: !createdNewTracks,
            ClearTrackSelection: true,
            ClearEventSelection: !createdNewTracks,
            RebuildPreview: true,
            RecalculateMetrics: true);

    public readonly record struct NoteStateSnapshot(
        byte NoteNumber,
        byte Channel,
        long Time,
        long Length);
}
