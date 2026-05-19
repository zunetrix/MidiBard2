using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class PrepareForPlaybackCommandTests
{
    [Fact]
    public void Execute_OrchestratesWholeFileCleanupThroughChildCommandsAndSupportsSingleUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)),
            CreateTrack(
                string.Empty,
                Timed(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }, 0),
                Note(60, 0, 120),
                Note(100, 0, 120)),
            CreateTrack("Flute+1", Note(60, 240, 120)),
            CreateTrack("Drumkit",
                Note(36, 0, 120, channel: 9),
                Note(38, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            EditorCommandContext.Create(session),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(3);
        result.Result.Value.TrackNameTransposeTracks.ShouldBe(1);
        result.Result.Value.TrackNameTransposeChangedNotes.ShouldBe(1);
        result.Result.Value.MappedInstrumentTracks.ShouldBe(1);
        result.Result.Value.DrumSourceTracks.ShouldBe(1);
        result.Result.Value.DrumTracksCreated.ShouldBe(2);
        result.Result.Value.DrumSourceTracksDeleted.ShouldBe(1);
        result.Result.Value.DrumRestTracks.ShouldBe(0);
        result.Result.Value.DrumAutoEditedTracks.ShouldBe(0);
        result.Result.Value.DrumTransposedNotes.ShouldBe(2);
        result.Result.Value.AutoEditedTracks.ShouldBe(2);
        result.Result.Value.AutoEditedReplacedTracks.ShouldBe(2);
        result.Result.Value.AutoEditPickedParts.ShouldBe(3);
        result.Result.Value.AutoEditChangedNotes.ShouldBe(1);

        file.Tracks[0].IsConductorTrack.ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldContain("Flute");
        file.Tracks.Select(track => track.Name).ShouldContain("BassDrum");
        file.Tracks.Select(track => track.Name).ShouldContain("SnareDrum");
        file.Tracks.Select(track => track.Name).ShouldNotContain("Drumkit");
        file.Tracks.Single(track => track.Name == "Flute")
            .Chunk.GetNotes()
            .Single()
            .NoteNumber
            .ShouldBe((SevenBitNumber)72);

        var unnamedSourceReplacement = file.Tracks
            .Where(track => !track.IsConductorTrack)
            .Where(track => track.Name is not "Flute" and not "BassDrum" and not "SnareDrum")
            .Single();
        unnamedSourceReplacement.Name.ShouldNotBeEmpty();
        unnamedSourceReplacement.Chunk.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .ShouldBe(new[] { 60, 76 });
        file.IsDirty.ShouldBeTrue();

        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
        session.PendingRefreshHints.ReloadSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();
        session.PendingRefreshHints.RebuildPreview.ShouldBeTrue();
        session.PendingRefreshHints.RecalculateMetrics.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Conductor",
            string.Empty,
            "Flute+1",
            "Drumkit",
        });
    }

    [Fact]
    public void Execute_RespectsDisabledOptionalPreparationSteps()
    {
        var file = CreateEditableFile(
            CreateTrack("Flute+1", Note(60, 0, 120)),
            CreateTrack("Drumkit", Note(36, 120, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            EditorCommandContext.Create(session),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions(
                ApplyTrackNameTransposes: false,
                MapInstruments: false,
                SplitDrumkits: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.TrackNameTransposeTracks.ShouldBe(0);
        result.Result.Value.DrumSourceTracks.ShouldBe(0);
        result.Result.Value.DrumTracksCreated.ShouldBe(0);
        result.Result.Value.AutoEditedTracks.ShouldBe(1);
        result.Result.Value.AutoEditedReplacedTracks.ShouldBe(1);
        file.Tracks.Select(track => track.Name).ShouldBe(new[]
        {
            "Flute+1",
            "Drumkit",
        });
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
        file.Tracks[1].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)36);
        session.History.UndoCount.ShouldBe(1);
    }

    [Fact]
    public void Execute_DefaultAutoEditOptionsKeepThreeDriftedChordNotesAndUseBestOctaveFit()
    {
        var file = CreateEditableFile(CreateTrack("Piano",
            Note(36, 0, 120),
            Note(40, 4, 120),
            Note(44, 8, 120),
            Note(60, 12, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            EditorCommandContext.Create(session),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions(
                ApplyTrackNameTransposes: false,
                MapInstruments: false,
                SplitDrumkits: false)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.AutoEditPickedParts.ShouldBe(3);
        result.Result.Value.AutoEditChangedNotes.ShouldBe(3);
        result.Result.Value.AutoEditedReplacedTracks.ShouldBe(1);
        file.Tracks.Single().Name.ShouldBe("Piano");
        file.Tracks.Single().Chunk.GetNotes()
            .Select(note => ((int)(byte)note.NoteNumber, note.Time))
            .ShouldBe(new[] { (52, 4L), (56, 8L), (72, 12L) });
    }

    [Fact]
    public void Execute_NoChangeWhenFileHasNoPerformanceTracks()
    {
        var file = CreateEditableFile(CreateTrack("Conductor", Timed(new SetTempoEvent(500000), 0)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            EditorCommandContext.Create(session),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(0);
        result.Result.Value.TrackNameTransposeTracks.ShouldBe(0);
        result.Result.Value.DrumSourceTracks.ShouldBe(0);
        result.Result.Value.AutoEditedTracks.ShouldBe(0);
        file.IsDirty.ShouldBeFalse();
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void Execute_MapsGenericInstrumentNamesWhenPrepareMapStepIsEnabled()
    {
        var file = CreateEditableFile(CreateTrack(
            "Choir Aahs",
            Timed(new ProgramChangeEvent((SevenBitNumber)52), 0),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            EditorCommandContext.Create(session),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions(
                ApplyTrackNameTransposes: false,
                MapInstruments: true,
                SplitDrumkits: false,
                MaxSimultaneousNotes: 1,
                RangeStrategy: MidiForgeRangeFitStrategy.FitNotesIndividually)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.MappedInstrumentTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Panpipes");
    }

    [Fact]
    public void Execute_CanMapEmptyTrackNamesFromMidiProgramNames()
    {
        var file = CreateEditableFile(CreateTrack(
            string.Empty,
            Timed(new ProgramChangeEvent((SevenBitNumber)0), 0),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            EditorCommandContext.Create(session),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions(
                ApplyTrackNameTransposes: false,
                MapInstruments: true,
                MapInstrumentsMode: MidiForgeMapInstrumentsMode.EmptyNamesOnly,
                MapInstrumentsNameSource: MidiForgeTrackNameFillMode.Midi,
                SplitDrumkits: false,
                MaxSimultaneousNotes: 1,
                RangeStrategy: MidiForgeRangeFitStrategy.FitNotesIndividually)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.MappedInstrumentTracks.ShouldBe(1);
        file.Tracks[0].Name.ShouldBe("Acoustic Grand Piano");
    }

    [Fact]
    public void Execute_UsesPersistedDrumSourceAndTransposeMaps()
    {
        var settings = MidiForgeMapDefaults.CreateDefaultSettings();
        settings.DrumkitSourceMaps.Single(map => map.TrackName == "Bongo").SourceNotes.Add(64);
        settings.DrumTransposePresets
            .Single(preset => preset.Preset == MidiForgeDrumTransposePreset.Default)
            .Entries
            .Add(new MidiForgeDrumTransposeMapEntry
            {
                Category = "Bongo",
                DrumkitInstrument = "Low Conga",
                InputNote = 64,
                OutputNote = 70,
            });

        var file = CreateEditableFile(CreateTrack("Drumkit", Note(64, 0, 120, channel: 9)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new PrepareForPlaybackCommand(),
            CreateContext(session, settings),
            new PrepareForPlaybackCommandOptions(new MidiForgePrepareForPlaybackOptions(
                ApplyTrackNameTransposes: false,
                MapInstruments: true,
                SplitDrumkits: true,
                RangeStrategy: MidiForgeRangeFitStrategy.FitNotesIndividually)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.MappedInstrumentTracks.ShouldBe(1);
        result.Result.Value.DrumTracksCreated.ShouldBe(1);
        result.Result.Value.DrumRestTracks.ShouldBe(0);
        file.Tracks.Single(track => track.Name == "Bongo")
            .Chunk
            .GetNotes()
            .Single()
            .NoteNumber
            .ShouldBe((SevenBitNumber)70);
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static EditorCommandContext CreateContext(
        MidiEditorSessionState session,
        MidiForgeMapSettings settings)
        => EditorCommandContext.Create(
            session,
            new EditorCommandServices
            {
                MidiMapProvider = new ConfigurationEditorMidiMapProvider(settings),
            });

    private static TrackChunk CreateTrack(string name, params object[] objects)
    {
        var chunk = new TrackChunk(new SequenceTrackNameEvent(name));
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
