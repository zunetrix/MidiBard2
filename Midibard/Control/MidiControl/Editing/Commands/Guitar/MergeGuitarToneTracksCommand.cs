using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

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

        if (options.Options is null)
            return EditorCommandValidation.Failure("Choose merge options.");

        if (options.Options.ChannelLayout == MidiForgeGuitarToneMergeChannelLayout.SingleChannelToneSwitches)
        {
            var sources = CollectMergeSources(context.File, options.TrackIndices, options.Options).Sources;
            if (TryFindDifferentToneOverlap(sources, out var overlap))
            {
                return EditorCommandValidation.Failure(
                    $"Single channel tone switches need non-overlapping guitar tones. " +
                    $"{overlap.Left.Track.DisplayName} overlaps {overlap.Right.Track.DisplayName}; " +
                    "use separate channels for overlapping tone tracks.");
            }
        }

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeMergeGuitarToneTracksResult> Execute(
        EditorCommandContext context,
        MergeGuitarToneTracksCommandOptions commandOptions)
    {
        var options = commandOptions.Options;
        var file = context.File;
        var sourcesResult = CollectMergeSources(file, commandOptions.TrackIndices, options);
        var sourceTracks = sourcesResult.Sources;
        var skippedTracks = sourcesResult.SkippedTracks;

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

        var mergedTrackName = string.IsNullOrWhiteSpace(options.TrackName)
            ? "ProgramElectricGuitar"
            : options.TrackName.Trim();
        var mergedResult = options.ChannelLayout == MidiForgeGuitarToneMergeChannelLayout.SingleChannelToneSwitches
            ? BuildSingleChannelMergedTrack(sourceTracks, options, mergedTrackName)
            : BuildSeparateChannelMergedTrack(sourceTracks, options, mergedTrackName);

        var mergedTrack = new EditableTrack(mergedResult.Chunk, 0);
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
                mergedResult.GeneratedProgramChanges,
                mergedResult.MergedNotes,
                mergedResult.MergedChannelEvents),
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                ClearSelectedTrack: true,
                RebuildPreview: true));
    }

    private static GuitarToneMergeSourcesResult CollectMergeSources(
        EditableMidiFile file,
        IReadOnlyList<int> trackIndices,
        MidiForgeMergeGuitarToneTracksOptions options)
    {
        var validTrackIndices = trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        var sourceTracks = new List<GuitarToneMergeSource>();
        var skippedTracks = 0;
        var limitByOutputChannels = options.ChannelLayout == MidiForgeGuitarToneMergeChannelLayout.SeparateChannels;

        foreach (var trackIndex in validTrackIndices)
        {
            if ((limitByOutputChannels && sourceTracks.Count >= MidiForgeGuitarTonePrimitives.MaximumMergeTracks) ||
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
                tone,
                limitByOutputChannels
                    ? (FourBitNumber)(byte)MidiForgeGuitarTonePrimitives.GetMergeOutputChannel(sourceTracks.Count)
                    : (FourBitNumber)0,
                programNumber));
        }

        return new GuitarToneMergeSourcesResult(sourceTracks, skippedTracks);
    }

    private static GuitarToneMergedTrackResult BuildSeparateChannelMergedTrack(
        IReadOnlyList<GuitarToneMergeSource> sourceTracks,
        MidiForgeMergeGuitarToneTracksOptions options,
        string mergedTrackName)
    {
        var mergedChunk = new TrackChunk();
        var generatedProgramChanges = 0;
        var mergedNotes = 0;
        var mergedChannelEvents = 0;

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

                mergedChannelEvents += AddMergedChannelEvents(manager, source, source.OutputChannel, options);
            }
        }

        return new GuitarToneMergedTrackResult(
            mergedChunk,
            generatedProgramChanges,
            mergedNotes,
            mergedChannelEvents);
    }

    private static GuitarToneMergedTrackResult BuildSingleChannelMergedTrack(
        IReadOnlyList<GuitarToneMergeSource> sourceTracks,
        MidiForgeMergeGuitarToneTracksOptions options,
        string mergedTrackName)
    {
        var mergedChunk = new TrackChunk();
        var generatedProgramChanges = 0;
        var mergedNotes = 0;
        var mergedChannelEvents = 0;
        var currentProgram = -1;
        var outputChannel = (FourBitNumber)0;
        var noteEntries = sourceTracks
            .SelectMany(source => source.Notes.Select(note => new GuitarToneMergeNote(source, note)))
            .OrderBy(entry => entry.Note.Time)
            .ThenBy(entry => (byte)entry.Note.NoteNumber)
            .ThenBy(entry => entry.Source.TrackIndex)
            .ToArray();

        using (var manager = mergedChunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(mergedTrackName), 0));

            foreach (var entry in noteEntries)
            {
                var programNumber = (int)(byte)entry.Source.ProgramNumber;
                if (currentProgram != programNumber)
                {
                    manager.Objects.Add(new TimedEvent(
                        new ProgramChangeEvent(entry.Source.ProgramNumber) { Channel = outputChannel },
                        entry.Note.Time));
                    generatedProgramChanges++;
                    currentProgram = programNumber;
                }

                manager.Objects.Add(new TimedEvent(
                    new NoteOnEvent(entry.Note.NoteNumber, entry.Note.Velocity) { Channel = outputChannel },
                    entry.Note.Time));
                manager.Objects.Add(new TimedEvent(
                    new NoteOffEvent(entry.Note.NoteNumber, entry.Note.OffVelocity) { Channel = outputChannel },
                    entry.Note.EndTime));
                mergedNotes++;
            }

            foreach (var source in sourceTracks)
                mergedChannelEvents += AddMergedChannelEvents(manager, source, outputChannel, options);
        }

        return new GuitarToneMergedTrackResult(
            mergedChunk,
            generatedProgramChanges,
            mergedNotes,
            mergedChannelEvents);
    }

    private static int AddMergedChannelEvents(
        TimedObjectsManager<TimedEvent> manager,
        GuitarToneMergeSource source,
        FourBitNumber outputChannel,
        MidiForgeMergeGuitarToneTracksOptions options)
    {
        var mergedChannelEvents = 0;
        foreach (var timedEvent in source.Chunk.GetTimedEvents()
            .Where(timedEvent => MidiForgeGuitarTonePrimitives.ShouldMergeChannelEvent(timedEvent.Event, options)))
        {
            var channelEvent = (ChannelEvent)timedEvent.Event.Clone();
            channelEvent.Channel = outputChannel;
            manager.Objects.Add(new TimedEvent(channelEvent, timedEvent.Time));
            mergedChannelEvents++;
        }

        return mergedChannelEvents;
    }

    private static bool TryFindDifferentToneOverlap(
        IReadOnlyList<GuitarToneMergeSource> sources,
        out GuitarToneMergeOverlap overlap)
    {
        var notes = sources
            .SelectMany(source => source.Notes.Select(note => new GuitarToneMergeNote(source, note)))
            .OrderBy(entry => entry.Note.Time)
            .ThenBy(entry => entry.Note.EndTime)
            .ToArray();

        for (var i = 0; i < notes.Length; i++)
        {
            for (var j = i + 1; j < notes.Length && notes[j].Note.Time < notes[i].Note.EndTime; j++)
            {
                if (notes[i].Source.Tone == notes[j].Source.Tone)
                    continue;

                overlap = new GuitarToneMergeOverlap(
                    notes[i].Source,
                    notes[i].Note,
                    notes[j].Source,
                    notes[j].Note);
                return true;
            }
        }

        overlap = null;
        return false;
    }

    private sealed record GuitarToneMergeSourcesResult(
        IReadOnlyList<GuitarToneMergeSource> Sources,
        int SkippedTracks);

    private sealed record GuitarToneMergedTrackResult(
        TrackChunk Chunk,
        int GeneratedProgramChanges,
        int MergedNotes,
        int MergedChannelEvents);

    private sealed record GuitarToneMergeSource(
        int TrackIndex,
        EditableTrack Track,
        TrackChunk Chunk,
        MidiNote[] Notes,
        int Tone,
        FourBitNumber OutputChannel,
        SevenBitNumber ProgramNumber);

    private sealed record GuitarToneMergeNote(
        GuitarToneMergeSource Source,
        MidiNote Note);

    private sealed record GuitarToneMergeOverlap(
        GuitarToneMergeSource Left,
        MidiNote LeftNote,
        GuitarToneMergeSource Right,
        MidiNote RightNote);
}

public sealed record MergeGuitarToneTracksCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeMergeGuitarToneTracksOptions Options);
