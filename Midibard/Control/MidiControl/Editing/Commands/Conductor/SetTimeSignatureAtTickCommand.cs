using System;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Conductor;

[EditorOperation(
    "conductor.set-time-signature",
    "Set Time Signature at Tick",
    Scope = EditorOperationScope.File,
    MenuPath = "Forge/Set Time Signature")]
public sealed class SetTimeSignatureAtTickCommand
    : EditorOperationBase, IEditorCommand<SetTimeSignatureOptions, SetTimeSignatureResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SetTimeSignatureOptions options)
    {
        if (context.File is null)
            return EditorCommandValidation.Failure("Open a MIDI file first.");

        if (options.Tick < 0)
            return EditorCommandValidation.Failure("Tick must be zero or positive.");

        if (options.Numerator < 1 || options.Numerator > 32)
            return EditorCommandValidation.Failure("Choose a numerator from 1 to 32.");

        if (options.Denominator <= 0 || (options.Denominator & (options.Denominator - 1)) != 0)
            return EditorCommandValidation.Failure("Choose a denominator that is a power of two (1, 2, 4, 8, 16, 32, 64, 128, 256).");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<SetTimeSignatureResult> Execute(
        EditorCommandContext context,
        SetTimeSignatureOptions options)
    {
        var (conductor, created) = ConductorCommandHelpers.FindOrCreateConductorTrack(context.File);

        var numerator = (byte)options.Numerator;
        var denominator = (byte)options.Denominator;

        int eventIndex;
        int replacedIndex;

        using (var manager = conductor.Chunk.ManageTimedEvents())
        {
            var existing = manager.Objects
                .FirstOrDefault(te => te.Event is TimeSignatureEvent && te.Time == options.Tick);

            if (existing is not null)
            {
                var tsEvent = (TimeSignatureEvent)existing.Event;
                if (tsEvent.Numerator == numerator && tsEvent.Denominator == denominator)
                    return EditorCommandResult<SetTimeSignatureResult>.UnchangedResult(
                        new SetTimeSignatureResult(false, EventIndex: -1, ReplacedEventIndex: -1));

                manager.Objects.Remove(existing);
                manager.Objects.Add(new TimedEvent(new TimeSignatureEvent(numerator, denominator), options.Tick));
                eventIndex = -1;
                replacedIndex = 1;
            }
            else
            {
                manager.Objects.Add(new TimedEvent(new TimeSignatureEvent(numerator, denominator), options.Tick));
                eventIndex = 1;
                replacedIndex = -1;
            }
        }

        if (created)
            ConductorCommandHelpers.ReloadTracksForConductorFlag(context.File);

        return EditorCommandResult<SetTimeSignatureResult>.ChangedResult(
            new SetTimeSignatureResult(created, eventIndex, replacedIndex),
            refreshHints: ConductorCommandHelpers.ConductorChangedHints(created));
    }
}

public sealed record SetTimeSignatureOptions(long Tick, int Numerator, int Denominator);

public sealed record SetTimeSignatureResult(bool CreatedConductorTrack, int EventIndex, int ReplacedEventIndex);
