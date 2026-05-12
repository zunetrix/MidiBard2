using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;

namespace MidiBard.Tests.Control.MidiControl.Editing;

public class MidiForgeImporterTests
{
    [Fact]
    public void Normalize_TrimUntilFirstNote_ShiftsTimedEventsToSongStart()
    {
        var conductor = CreateTrack(Timed(new SetTempoEvent(500000), 240));
        var performance = CreateTrack(
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 240),
            Timed(new PitchBendEvent(8192) { Channel = (FourBitNumber)0 }, 360),
            Note(60, 480, 120));
        var midiFile = CreateMidiFile(conductor, performance);

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(TrimStartMode: MidiForgeTrimStartMode.UntilFirstNote));

        result.TrimmedTicks.ShouldBe(480);
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Single().Time.ShouldBe(0);
        result.MidiFile.GetTrackChunks()
            .SelectMany(chunk => chunk.GetTimedEvents())
            .Single(te => te.Event is ProgramChangeEvent)
            .Time.ShouldBe(0);
        result.MidiFile.GetTrackChunks()
            .SelectMany(chunk => chunk.GetTimedEvents())
            .Single(te => te.Event is SetTempoEvent)
            .Time.ShouldBe(0);
    }

    [Fact]
    public void Normalize_TrimEmptyBars_TrimsToContainingBarStart()
    {
        var performance = CreateTrack(Note(60, 480 * 4 + 120, 240));
        var midiFile = CreateMidiFile(performance);

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(TrimStartMode: MidiForgeTrimStartMode.EmptyBars));

        result.TrimmedTicks.ShouldBe(480 * 4);
        result.MidiFile.GetTrackChunks().SelectMany(chunk => chunk.GetNotes()).Single().Time.ShouldBe(120);
    }

    [Fact]
    public void Normalize_RemoveMetadata_RemovesNonPerformanceMetadataButPreservesNamesAndTempo()
    {
        var performance = CreateTrack(
            Timed(new SequenceTrackNameEvent("Piano"), 0),
            Timed(new TextEvent("comment"), 0),
            Timed(new LyricEvent("hello"), 120),
            Timed(new CopyrightNoticeEvent("copyright"), 0),
            Timed(new MarkerEvent("marker"), 0),
            Timed(new CuePointEvent("cue"), 0),
            Timed(new DeviceNameEvent("device"), 0),
            Timed(new SequenceNumberEvent(1), 0),
            Timed(new SequencerSpecificEvent(new byte[] { 1, 2, 3 }), 0),
            Timed(new SetTempoEvent(500000), 0),
            Note(60, 240, 120));
        var midiFile = CreateMidiFile(performance);

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(RemoveMetadata: true, RemoveSequencerSpecificEvents: true));

        var events = result.MidiFile.GetTrackChunks().Single().Events;
        result.RemovedMetadataEvents.ShouldBe(7);
        result.RemovedSequencerSpecificEvents.ShouldBe(1);
        events.ShouldContain(e => e is SequenceTrackNameEvent);
        events.ShouldContain(e => e is SetTempoEvent);
        events.ShouldNotContain(e => e is TextEvent);
        events.ShouldNotContain(e => e is LyricEvent);
        events.ShouldNotContain(e => e is CopyrightNoticeEvent);
        events.ShouldNotContain(e => e is MarkerEvent);
        events.ShouldNotContain(e => e is CuePointEvent);
        events.ShouldNotContain(e => e is DeviceNameEvent);
        events.ShouldNotContain(e => e is SequenceNumberEvent);
        events.ShouldNotContain(e => e is SequencerSpecificEvent);
    }

    [Fact]
    public void Normalize_SplitTracksByChannel_CreatesSingleChannelTracksAndKeepsConductorEvents()
    {
        var sourceTrack = CreateTrack(
            Timed(new SequenceTrackNameEvent("Layer"), 0),
            Timed(new SetTempoEvent(500000), 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
            Note(60, 120, 120, channel: 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)1) { Channel = (FourBitNumber)2 }, 0),
            Note(64, 240, 120, channel: 2));
        var midiFile = CreateMidiFile(sourceTrack);

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(SplitTracksByChannel: true));

        result.SplitSourceTracks.ShouldBe(1);
        result.CreatedSplitTracks.ShouldBe(2);

        var trackChunks = result.MidiFile.GetTrackChunks().ToArray();
        trackChunks.Count(chunk => chunk.Events.OfType<SetTempoEvent>().Any()).ShouldBe(1);

        var noteTracks = trackChunks.Where(chunk => chunk.GetNotes().Any()).ToArray();
        noteTracks.Length.ShouldBe(2);
        noteTracks.Select(chunk => chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Single())
            .ShouldBe(new byte[] { 0, 2 });
        noteTracks.Select(GetTrackName).ShouldBe(new[] { "Layer Ch 1", "Layer Ch 3" });
    }

    [Fact]
    public void Normalize_SortTracks_MovesPriorityTracksFirstAndDrumsLast()
    {
        var midiFile = CreateMidiFile(
            CreateNamedNoteTrack("Backing", 60, channel: 0),
            CreateNamedNoteTrack("Drumkit", 36, channel: 9),
            CreateNamedNoteTrack("Vocal 2", 62, channel: 1),
            CreateNamedNoteTrack("Vocal 1", 64, channel: 2));

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(SortTracks: true));

        result.MidiFile.GetTrackChunks()
            .Where(chunk => chunk.GetNotes().Any())
            .Select(GetTrackName)
            .ShouldBe(new[] { "Vocal 1", "Vocal 2", "Backing", "Drumkit" });
    }

    [Fact]
    public void Normalize_OptimizeChannels_ReusesProgramChannelsAndSkipsDrumChannel()
    {
        var midiFile = CreateMidiFile(
            CreateProgramTrack(0, inputChannel: 5, note: 60),
            CreateProgramTrack(0, inputChannel: 6, note: 62),
            CreateProgramTrack(1, inputChannel: 7, note: 64),
            CreateProgramTrack(0, inputChannel: 9, note: 36));

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(OptimizeChannels: true));

        result.OptimizedTracks.ShouldBe(3);
        result.MidiFile.GetTrackChunks()
            .Where(chunk => chunk.GetNotes().Any())
            .Select(chunk => chunk.Events.OfType<ChannelEvent>().Select(e => (byte)e.Channel).Distinct().Single())
            .ShouldBe(new byte[] { 0, 0, 1, 9 });
    }

    [Fact]
    public void Normalize_OverwriteTrackNames_UsesSharedProgramNamingFallback()
    {
        var midiFile = CreateMidiFile(
            CreateTrack(
                Timed(new SequenceTrackNameEvent("Old Name"), 0),
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
                Note(60, 0, 120)));

        var result = MidiForgeImporter.Normalize(
            midiFile,
            new MidiForgeImportOptions(OverwriteTrackNames: true));

        GetTrackName(result.MidiFile.GetTrackChunks().Single()).ShouldBe("Acoustic Grand Piano");
    }

    private static MidiFile CreateMidiFile(params TrackChunk[] chunks)
        => new(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

    private static TrackChunk CreateNamedNoteTrack(string name, int note, int channel)
        => CreateTrack(
            Timed(new SequenceTrackNameEvent(name), 0),
            Note(note, 0, 120, channel));

    private static TrackChunk CreateProgramTrack(int program, int inputChannel, int note)
        => CreateTrack(
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)program) { Channel = (FourBitNumber)(byte)inputChannel }, 0),
            Note(note, 0, 120, inputChannel));

    private static TrackChunk CreateTrack(params object[] objects)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();

        foreach (var item in objects)
        {
            switch (item)
            {
                case TimedEvent timedEvent:
                    manager.Objects.Add(timedEvent);
                    break;
                case Note note:
                    manager.Objects.Add(new TimedEvent(
                        new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                        note.Time));
                    manager.Objects.Add(new TimedEvent(
                        new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                        note.EndTime));
                    break;
            }
        }

        return chunk;
    }

    private static TimedEvent Timed(MidiEvent midiEvent, long time)
        => new(midiEvent, time);

    private static Note Note(int noteNumber, long time, long length, int channel = 0)
        => new(
            (SevenBitNumber)(byte)noteNumber,
            length,
            time)
        {
            Channel = (FourBitNumber)(byte)channel,
            Velocity = (SevenBitNumber)100,
            OffVelocity = (SevenBitNumber)0,
        };

    private static string GetTrackName(TrackChunk chunk)
        => chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text ?? string.Empty;
}
