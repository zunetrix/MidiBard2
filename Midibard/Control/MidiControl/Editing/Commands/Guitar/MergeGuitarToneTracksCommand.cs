using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;

using MidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace MidiBard.Control.MidiControl.Editing.Commands.Guitar;

[EditorOperation(
    "guitar.merge-tone-tracks",
    "Merge Guitar Tone Tracks",
    Scope = EditorOperationScope.Guitar,
    MenuPath = "Guitar/Tone",
    RequiresSelectedTracks = true)]
public sealed class MergeGuitarToneTracksCommand
    : EditorOperationBase, IEditorCommand<MergeGuitarToneTracksCommandOptions, MidiForgeMergeGuitarToneTracksResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, MergeGuitarToneTracksCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeMergeGuitarToneTracksResult> Execute(
        EditorCommandContext context,
        MergeGuitarToneTracksCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = commandOptions.TrackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

        var options = commandOptions.Options;
        var sourceTracks = new List<GuitarToneMergeSource>();
        var skippedTracks = 0;

        foreach (var trackIndex in validTrackIndices)
        {
            if (sourceTracks.Count >= MidiForgeGuitarTonePrimitives.MaximumMergeTracks ||
                options.ToneByTrackIndex == null ||
                !options.ToneByTrackIndex.TryGetValue(trackIndex, out var tone) ||
                !MidiForgeGuitarTonePrimitives.TryResolveProgramForTone(tone, out var programNumber))
            {
                skippedTracks++;
                continue;
            }

            var sourceChunk = file.Tracks[trackIndex].CloneCurrentChunk();
            var notes = sourceChunk.GetNotes().ToArray();
            if (notes.Length == 0)
            {
                skippedTracks++;
                continue;
            }

            sourceTracks.Add(new GuitarToneMergeSource(
                trackIndex,
                file.Tracks[trackIndex],
                sourceChunk,
                notes,
                (FourBitNumber)(byte)MidiForgeGuitarTonePrimitives.GetMergeOutputChannel(sourceTracks.Count),
                programNumber));
        }

        if (sourceTracks.Count == 0)
        {
            return EditorCommandResult<MidiForgeMergeGuitarToneTracksResult>.UnchangedResult(
                new MidiForgeMergeGuitarToneTracksResult(
                    0,
                    0,
                    0,
                    skippedTracks,
                    0,
                    0,
                    0));
        }

        var mergedChunk = new TrackChunk();
        var generatedProgramChanges = 0;
        var mergedNotes = 0;
        var mergedChannelEvents = 0;
        var mergedTrackName = string.IsNullOrWhiteSpace(options.TrackName)
            ? "ProgramElectricGuitar"
            : options.TrackName.Trim();

        using (var manager = mergedChunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(mergedTrackName), 0));

            foreach (var source in sourceTracks)
            {
                manager.Objects.Add(new TimedEvent(
                    new ProgramChangeEvent(source.ProgramNumber) { Channel = source.OutputChannel },
                    0));
                generatedProgramChanges++;
            }

            foreach (var source in sourceTracks)
            {
                foreach (var note in source.Notes.OrderBy(note => note.Time).ThenBy(note => (byte)note.NoteNumber))
                {
                    manager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = source.OutputChannel },
                        note.Time));
                    manager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = source.OutputChannel },
                        note.EndTime));
                    mergedNotes++;
                }

                foreach (var timedEvent in source.Chunk.GetTimedEvents()
                    .Where(timedEvent => MidiForgeGuitarTonePrimitives.ShouldMergeChannelEvent(timedEvent.Event, options)))
                {
                    var channelEvent = (ChannelEvent)timedEvent.Event.Clone();
                    channelEvent.Channel = source.OutputChannel;
                    manager.Objects.Add(new TimedEvent(channelEvent, timedEvent.Time));
                    mergedChannelEvents++;
                }
            }
        }

        var mergedTrack = new EditableTrack(mergedChunk, 0);
        var deletedSourceTracks = 0;

        if (options.DeleteOriginalTracks)
        {
            var insertIndex = sourceTracks.Min(source => source.TrackIndex);
            foreach (var source in sourceTracks.OrderByDescending(source => source.TrackIndex))
            {
                source.Track.Dispose();
                file.Tracks.RemoveAt(source.TrackIndex);
                deletedSourceTracks++;
            }

            file.Tracks.Insert(insertIndex, mergedTrack);
        }
        else
        {
            file.Tracks.Insert(sourceTracks.Max(source => source.TrackIndex) + 1, mergedTrack);
        }

        MidiForgeNotePrimitives.RefreshTrackIndexes(file);

        return EditorCommandResult<MidiForgeMergeGuitarToneTracksResult>.ChangedResult(
            new MidiForgeMergeGuitarToneTracksResult(
                sourceTracks.Count,
                1,
                deletedSourceTracks,
                skippedTracks,
                generatedProgramChanges,
                mergedNotes,
                mergedChannelEvents),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true));
    }

    private sealed record GuitarToneMergeSource(
        int TrackIndex,
        EditableTrack Track,
        TrackChunk Chunk,
        MidiNote[] Notes,
        FourBitNumber OutputChannel,
        SevenBitNumber ProgramNumber);
}

public sealed record MergeGuitarToneTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeMergeGuitarToneTracksOptions Options);
