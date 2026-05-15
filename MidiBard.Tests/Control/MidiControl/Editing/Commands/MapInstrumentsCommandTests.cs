using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class MapInstrumentsCommandTests
{
    [Fact]
    public void Execute_RenamesEmptyAndGenericNamesFromConfiguredInstrumentMap()
    {
        var file = CreateEditableFile(
            CreateTrack(string.Empty, Timed(new ProgramChangeEvent((SevenBitNumber)52), 0), Note(60, 0, 120)),
            CreateTrack("Choir Aahs", Timed(new ProgramChangeEvent((SevenBitNumber)52), 0), Note(64, 120, 120)),
            CreateTrack("Lead Melody", Timed(new ProgramChangeEvent((SevenBitNumber)52), 0), Note(67, 240, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MapInstrumentsCommand(),
            EditorCommandContext.Create(session),
            new MapInstrumentsCommandOptions(
                new[] { 0, 1, 2 },
                new MidiForgeMapInstrumentsOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(3);
        result.Result.Value.RenamedTracks.ShouldBe(2);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Panpipes",
            "Panpipes",
            "Lead Melody",
        });

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            string.Empty,
            "Choir Aahs",
            "Lead Melody",
        });
    }

    [Fact]
    public void Execute_ReplaceModeOverwritesMeaningfulNames()
    {
        var file = CreateEditableFile(CreateTrack(
            "Lead Melody",
            Timed(new ProgramChangeEvent((SevenBitNumber)52), 0),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MapInstrumentsCommand(),
            EditorCommandContext.Create(session),
            new MapInstrumentsCommandOptions(
                new[] { 0 },
                new MidiForgeMapInstrumentsOptions(MidiForgeMapInstrumentsMode.ReplaceSelectedNames)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Panpipes");
    }

    [Fact]
    public void Execute_RenamesDrumTrackFromCustomSourceMapWhenAllMappedNotesShareTarget()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Cymbal").SourceNotes.AddRange([42, 46]);

        var file = CreateEditableFile(CreateTrack(
            "Track 01",
            Note(42, 0, 120, channel: 9),
            Note(46, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MapInstrumentsCommand(),
            CreateContext(session, settings),
            new MapInstrumentsCommandOptions(
                new[] { 0 },
                new MidiForgeMapInstrumentsOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        file.Tracks[0].Name.ShouldBe("Cymbal");
    }

    private static EditorCommandContext CreateContext(
        MidiEditorSessionState session,
        MidiForgeMapSettings settings)
        => EditorCommandContext.Create(
            session,
            new EditorCommandServices
            {
                MidiMapProvider = new ConfigurationEditorMidiMapProvider(settings),
            });

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string name, params object[] objects)
    {
        var chunk = new TrackChunk();
        if (!string.IsNullOrEmpty(name))
            chunk.Events.Add(new SequenceTrackNameEvent(name));

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
}
