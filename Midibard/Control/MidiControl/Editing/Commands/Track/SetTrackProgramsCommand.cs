using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Track;

[EditorOperation(
    "track.set-programs",
    "Set Track MIDI Programs",
    Scope = EditorOperationScope.Track,
    MenuPath = "Track/Programs",
    RequiresSelectedTracks = true)]
public sealed class SetTrackProgramsCommand
    : EditorOperationBase, IEditorCommand<SetTrackProgramsCommandOptions, MidiForgeSetTrackProgramResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SetTrackProgramsCommandOptions options)
    {
        if (options.TrackIndices is null)
            return EditorCommandValidation.Failure("Choose at least one track.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<MidiForgeSetTrackProgramResult> Execute(
        EditorCommandContext context,
        SetTrackProgramsCommandOptions commandOptions)
    {
        var file = context.File;
        var validTrackIndices = MidiForgeTrackNamePrimitives.GetValidPerformanceTrackIndices(
            file,
            commandOptions.TrackIndices);
        var options = commandOptions.Options;
        var programNumber = (SevenBitNumber)(byte)Math.Clamp(options.ProgramNumber, 0, 127);
        var changedTracks = 0;
        var addedProgramChanges = 0;
        var updatedProgramChanges = 0;
        var renamedTracks = 0;

        foreach (var (trackIndex, fallbackIndex) in validTrackIndices.Select((index, order) => (index, order + 1)))
        {
            var track = file.Tracks[trackIndex];
            var sourceChunk = track.CloneCurrentChunk();
            var trackChanged = false;

            using (var manager = sourceChunk.ManageTimedEvents())
            {
                var timedProgramChanges = manager.Objects
                    .Where(timedEvent => timedEvent.Event is ProgramChangeEvent)
                    .OrderBy(timedEvent => timedEvent.Time)
                    .ToArray();

                if (timedProgramChanges.Length == 0)
                {
                    manager.Objects.Add(new TimedEvent(
                        new ProgramChangeEvent(programNumber)
                        {
                            Channel = (FourBitNumber)(byte)Math.Clamp(track.Channel, 0, 15),
                        },
                        0));
                    addedProgramChanges++;
                    trackChanged = true;
                }
                else
                {
                    var changesToUpdate = options.ReplaceAllProgramChanges
                        ? timedProgramChanges
                        : timedProgramChanges.Take(1);

                    foreach (var timedProgramChange in changesToUpdate)
                    {
                        var programChange = (ProgramChangeEvent)timedProgramChange.Event;
                        if (programChange.ProgramNumber == programNumber)
                            continue;

                        programChange.ProgramNumber = programNumber;
                        updatedProgramChanges++;
                        trackChanged = true;
                    }
                }
            }

            var replacementTrack = trackChanged
                ? new EditableTrack(sourceChunk, trackIndex)
                : track;

            if (options.RenameTracks)
            {
                var trackName = MidiForgeTrackNaming.GetTrackNameForProgram(
                    programNumber,
                    options.RenameMode,
                    fallbackIndex);
                if (MidiForgeTrackNamePrimitives.SetEditableTrackName(replacementTrack, trackName))
                {
                    renamedTracks++;
                    trackChanged = true;
                }
            }

            if (!trackChanged)
                continue;

            if (!ReferenceEquals(replacementTrack, track))
            {
                track.Dispose();
                file.Tracks[trackIndex] = replacementTrack;
            }

            changedTracks++;
        }

        var result = new MidiForgeSetTrackProgramResult(
            validTrackIndices.Length,
            changedTracks,
            addedProgramChanges,
            updatedProgramChanges,
            renamedTracks);

        if (changedTracks == 0)
            return EditorCommandResult<MidiForgeSetTrackProgramResult>.UnchangedResult(result);

        return EditorCommandResult<MidiForgeSetTrackProgramResult>.ChangedResult(
            result,
            refreshHints: new EditorRefreshHints(
                ReloadTrackList: true,
                ReloadSelectedTrack: true,
                ReloadEventList: true,
                ClearTrackSelection: true,
                ClearEventSelection: true,
                RebuildPreview: true));
    }
}

public sealed record SetTrackProgramsCommandOptions(
    IReadOnlyList<int> TrackIndices,
    MidiForgeSetTrackProgramOptions Options);
