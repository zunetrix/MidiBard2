using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class SanitizeFileDebugTests
{
    [Fact]
    public void Debug_ConductorChunkAfterSanitize_IsEmptyAndRemoved()
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SetTempoEvent(500000), 0));
            manager.Objects.Add(new TimedEvent(new SetTempoEvent(500000), 0));
        }

        var midi = new MidiFile(chunk) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };

        var before = midi.GetTrackChunks().ToList();
        before.Count.ShouldBe(1);
        var originalChunk = before[0];
        originalChunk.Events.Count.ShouldBe(2);

        Sanitizer.Sanitize(midi, new SanitizingSettings
        {
            RemoveDuplicatedSetTempoEvents = true
        });

        var afterChunks = midi.GetTrackChunks().ToList();
        var originalStillPresent = afterChunks.Any(c => ReferenceEquals(c, originalChunk));
        originalStillPresent.ShouldBeFalse("Sanitizer removed the entire chunk from source");
        originalChunk.Events.Count.ShouldBe(0, "Sanitizer cleared events from original chunk before removing it");
    }

    [Fact]
    public void Debug_ApplySanitizeProtectsAndMarksConductorTrack()
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            manager.Objects.Add(new TimedEvent(new SetTempoEvent(500000), 0));
            manager.Objects.Add(new TimedEvent(new SetTempoEvent(500000), 0));
        }

        var midi = new MidiFile(chunk) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        var file = new EditableMidiFile(midi);

        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].IsConductorTrack.ShouldBeTrue();

        var changed = FileDocumentCommandHelpers.ApplySanitize(
            file,
            new SanitizingSettings { RemoveDuplicatedSetTempoEvents = true });

        changed.ShouldBeTrue("ApplySanitize should detect duplicate tempo removal");
        file.Tracks.Count.ShouldBe(1, "Conductor track must survive sanitize");
        file.Tracks[0].IsConductorTrack.ShouldBeTrue("Restored track should be marked as conductor");
        // One tempo event is restored so the track stays detectable as conductor.
        file.Tracks[0].Chunk.Events.OfType<SetTempoEvent>().Count().ShouldBe(1);
    }
}
