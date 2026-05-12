using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeLyricsExporterTests
{
    [Fact]
    public void Export_UsesLyricAndTextEventsOrdersByTimeAndSanitizes()
    {
        var midi = CreateMidiFile(CreateTrack(
            Timed(new LyricEvent("/World"), 960),
            Timed(new TextEvent("Hel"), 240),
            Timed(new LyricEvent("lo_"), 480),
            Timed(new TextEvent("[Verse]"), 1200)));

        var result = MidiForgeLyricsExporter.Export(midi, "My Song");

        result.HasLyrics.ShouldBeTrue();
        result.Lines.Select(line => line.Text).ShouldBe(new[] { "Hello", "World" });
        result.Lines.Select(line => line.Time).ShouldBe(new[]
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1),
        });
        result.Content.ShouldContain("[ti:My Song]");
        result.Content.ShouldContain("[00:00.25]Hello");
        result.Content.ShouldContain("[00:01.00]World");
        result.Content.ShouldNotContain("[Verse]");
    }

    [Fact]
    public void Export_HandlesTempoChanges()
    {
        var midi = CreateMidiFile(
            CreateTrack(
                Timed(new SetTempoEvent(500000), 0),
                Timed(new SetTempoEvent(1000000), 480)),
            CreateTrack(Timed(new LyricEvent("Slow"), 960)));

        var result = MidiForgeLyricsExporter.Export(midi, "Tempo");

        result.Lines.Single().Time.ShouldBe(TimeSpan.FromMilliseconds(1500));
        result.Content.ShouldContain("[00:01.50]Slow");
    }

    [Fact]
    public void Export_NoLyricsReturnsBlankLrcTemplate()
    {
        var midi = CreateMidiFile(CreateTrack(Timed(new SetTempoEvent(500000), 0)));

        var result = MidiForgeLyricsExporter.Export(midi, "Instrumental");

        result.HasLyrics.ShouldBeFalse();
        result.Content.ShouldContain("[ti:Instrumental]");
        result.Content.ShouldContain("[00:00.00]");
    }

    [Fact]
    public void ExtractLyrics_SkipsZeroTickEvents()
    {
        var midi = CreateMidiFile(CreateTrack(
            Timed(new LyricEvent("Intro"), 0),
            Timed(new LyricEvent("First"), 240)));

        var result = MidiForgeLyricsExporter.ExtractLyrics(midi);

        result.Single().Text.ShouldBe("First");
        result.Single().Time.ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    private static MidiFile CreateMidiFile(params TrackChunk[] chunks)
        => new(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

    private static TrackChunk CreateTrack(params TimedEvent[] timedEvents)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();

        foreach (var timedEvent in timedEvents)
            manager.Objects.Add(timedEvent);

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);
}
