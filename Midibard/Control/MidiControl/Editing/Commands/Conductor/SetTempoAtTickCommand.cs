using System;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Conductor;

[EditorOperation(
    "conductor.set-tempo",
    "Set Tempo at Tick",
    Scope = EditorOperationScope.File,
    MenuPath = "Forge/Set Tempo")]
public sealed class SetTempoAtTickCommand
    : EditorOperationBase, IEditorCommand<SetTempoOptions, SetTempoResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SetTempoOptions options)
    {
        if (context.File is null)
            return EditorCommandValidation.Failure("Open a MIDI file first.");

        if (options.Tick < 0)
            return EditorCommandValidation.Failure("Tick must be zero or positive.");

        if (options.Bpm < 1 || options.Bpm > 300)
            return EditorCommandValidation.Failure("Choose a BPM from 1 to 300.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<SetTempoResult> Execute(
        EditorCommandContext context,
        SetTempoOptions options)
    {
        var (conductor, created) = ConductorCommandHelpers.FindOrCreateConductorTrack(context.File);

        var microseconds = (long)(60_000_000.0 / options.Bpm);

        int eventIndex;
        int replacedIndex;

        using (var manager = conductor.Chunk.ManageTimedEvents())
        {
            var existing = manager.Objects
                .FirstOrDefault(te => te.Event is SetTempoEvent && te.Time == options.Tick);

            if (existing is not null)
            {
                var tempoEvent = (SetTempoEvent)existing.Event;
                if (tempoEvent.MicrosecondsPerQuarterNote == microseconds)
                    return EditorCommandResult<SetTempoResult>.UnchangedResult(
                        new SetTempoResult(false, EventIndex: -1, ReplacedEventIndex: -1));

                tempoEvent.MicrosecondsPerQuarterNote = microseconds;
                eventIndex = -1;
                replacedIndex = 1;
            }
            else
            {
                manager.Objects.Add(new TimedEvent(new SetTempoEvent(microseconds), options.Tick));
                eventIndex = 1;
                replacedIndex = -1;
            }
        }

        context.File.RefreshTempoMap();

        return EditorCommandResult<SetTempoResult>.ChangedResult(
            new SetTempoResult(created, eventIndex, replacedIndex),
            refreshHints: ConductorCommandHelpers.ConductorChangedHints(created));
    }
}

public sealed record SetTempoOptions(long Tick, int Bpm);

public sealed record SetTempoResult(bool CreatedConductorTrack, int EventIndex, int ReplacedEventIndex);
