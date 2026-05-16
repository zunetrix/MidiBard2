using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Preview;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ResolvePreviewInstrumentAssignmentsQueryTests
{
    [Fact]
    public void Execute_ResolvesTrackNameInstrumentWithoutMutatingFile()
    {
        var file = CreateEditableFile(CreateTrack("Trumpet", Timed(NoteOn(60), 0)));
        var beforeVersion = file.Version;

        var result = Execute(file, new FakePreviewSettings { DefaultInstrumentId = 0 });

        result.Succeeded.ShouldBeTrue();
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();

        var track = result.Result!.Value.Tracks.Single();
        track.TrackIndex.ShouldBe(0);
        track.TrackName.ShouldBe("Trumpet");
        track.BaseInstrumentId.ShouldBe(15u);
        track.GetResolvedInstrumentId(0).ShouldBe(15u);
        track.ResolvedChannelInstrumentIds.ShouldAllBe(instrumentId => instrumentId == 15u);
    }

    [Fact]
    public void Execute_DoesNotUseProgramFallbackForUnnamedTrack()
    {
        var file = CreateEditableFile(CreateTrack(
            string.Empty,
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)56), 0)));

        var result = Execute(file, new FakePreviewSettings { DefaultInstrumentId = 0 });

        var track = result.Result!.Value.Tracks.Single();
        track.BaseInstrumentId.ShouldBeNull();
        track.GetResolvedInstrumentId(0).ShouldBeNull();
    }

    [Fact]
    public void Execute_ForceDefaultOverridesTrackNameAndProgramTone()
    {
        var file = CreateEditableFile(CreateTrack(
            "Trumpet",
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)27), 0),
            Timed(NoteOn(60), 10)));

        var result = Execute(
            file,
            new FakePreviewSettings
            {
                DefaultInstrumentId = 2,
                ForceDefaultInstrument = true,
                GuitarToneMode = GuitarToneMode.Standard,
            });

        var track = result.Result!.Value.Tracks.Single();
        track.BaseInstrumentId.ShouldBe(2u);
        track.GetResolvedInstrumentId(0).ShouldBe(2u);
        track.ResolvedChannelInstrumentIds.ShouldAllBe(instrumentId => instrumentId == 2u);
    }

    [Fact]
    public void Execute_StandardModeTracksGuitarProgramChangesByChannelAndPosition()
    {
        var file = CreateEditableFile(CreateTrack(
            "ElectricGuitarOverdriven",
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)27), 0, channel: 0),
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)30), 480, channel: 1)));
        var settings = new FakePreviewSettings { GuitarToneMode = GuitarToneMode.Standard };

        var atStart = Execute(file, settings, positionSeconds: 0).Result!.Value.Tracks.Single();
        var afterSecondProgram = Execute(file, settings, positionSeconds: 0.5).Result!.Value.Tracks.Single();

        atStart.GetResolvedInstrumentId(0).ShouldBe(25u);
        atStart.GetResolvedInstrumentId(1).ShouldBe(24u);
        afterSecondProgram.GetResolvedInstrumentId(0).ShouldBe(25u);
        afterSecondProgram.GetResolvedInstrumentId(1).ShouldBe(27u);
    }

    [Fact]
    public void Execute_ProgramElectricGuitarModeAppliesOnlyToProgramGuitarTracks()
    {
        var file = CreateEditableFile(
            CreateTrack(
                "Program: ElectricGuitar",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)30), 0)),
            CreateTrack(
                "ElectricGuitarOverdriven",
                Timed(new ProgramChangeEvent((SevenBitNumber)(byte)30), 0)));

        var result = Execute(
            file,
            new FakePreviewSettings { GuitarToneMode = GuitarToneMode.ProgramElectricGuitarMode });

        result.Result!.Value.Tracks[0].GetResolvedInstrumentId(0).ShouldBe(27u);
        result.Result.Value.Tracks[1].GetResolvedInstrumentId(0).ShouldBe(24u);
    }

    [Theory]
    [InlineData("ElectricGuitarOverdriven", 30, 24u)]
    [InlineData("ElectricGuitarClean", 29, 25u)]
    [InlineData("ElectricGuitarMuted", 29, 26u)]
    [InlineData("ElectricGuitarPowerChords", 27, 27u)]
    [InlineData("ElectricGuitarSpecial", 29, 28u)]
    public void Execute_OverrideByTrackIgnoresProgramChangesAndUsesTrackName(
        string trackName,
        byte conflictingProgram,
        uint expectedInstrumentId)
    {
        var file = CreateEditableFile(CreateTrack(
            trackName,
            Timed(new ProgramChangeEvent((SevenBitNumber)conflictingProgram), 0)));
        var settings = new FakePreviewSettings { GuitarToneMode = GuitarToneMode.OverrideByTrack };

        var result = Execute(file, settings);

        var track = result.Result!.Value.Tracks.Single();
        track.GetResolvedInstrumentId(0).ShouldBe(expectedInstrumentId);
        track.GetResolvedInstrumentId(15).ShouldBe(expectedInstrumentId);
        track.ResolvedChannelInstrumentIds.ShouldAllBe(instrumentId => instrumentId == expectedInstrumentId);
    }

    [Theory]
    [InlineData(GuitarToneMode.Off, 24, 26, 24, 25, 2)]
    [InlineData(GuitarToneMode.Standard, 27, 25, 28, 25, 2)]
    [InlineData(GuitarToneMode.Simple, 27, 25, 28, 25, 2)]
    [InlineData(GuitarToneMode.OverrideByTrack, 24, 26, 24, 25, 2)]
    [InlineData(GuitarToneMode.ProgramElectricGuitarMode, 24, 26, 24, 25, 2)]
    public void Execute_TestTrackInstrumentFixtureResolvesToneModesLikeRuntime(
        GuitarToneMode mode,
        uint expectedFirstOverdriven,
        uint expectedMuted,
        uint expectedSecondOverdriven,
        uint expectedClean,
        uint expectedPiano)
    {
        var file = new EditableMidiFile(MidiFile.Read(TestTrackInstrumentMidiPath));

        var result = Execute(file, new FakePreviewSettings { GuitarToneMode = mode });

        result.Succeeded.ShouldBeTrue();
        var tracks = result.Result!.Value.Tracks;
        var overdrivenTracks = tracks
            .Where(track => track.TrackName == "ElectricGuitarOverdriven")
            .OrderBy(track => track.TrackIndex)
            .ToArray();
        overdrivenTracks.Length.ShouldBe(2);

        overdrivenTracks[0].GetResolvedInstrumentId(0).ShouldBe(expectedFirstOverdriven);
        Track("ElectricGuitarMuted").GetResolvedInstrumentId(1).ShouldBe(expectedMuted);
        overdrivenTracks[1].GetResolvedInstrumentId(4).ShouldBe(expectedSecondOverdriven);
        Track("ElectricGuitarClean").GetResolvedInstrumentId(2).ShouldBe(expectedClean);
        Track("Piano").GetResolvedInstrumentId(3).ShouldBe(expectedPiano);

        PreviewTrackInstrumentAssignment Track(string name)
            => tracks.Single(track => track.TrackName == name);
    }

    private static PreviewQueryExecutionResult<PreviewInstrumentAssignments> Execute(
        EditableMidiFile file,
        FakePreviewSettings settings,
        double positionSeconds = 0)
        => new PreviewQueryExecutor().Execute(
            new ResolvePreviewInstrumentAssignmentsQuery(),
            new PreviewQueryContext(
                new PreviewSessionState(),
                file,
                new EditorSelectionSnapshot(-1, [], []),
                settings,
                new FakeInstrumentCatalog(),
                default),
            new ResolvePreviewInstrumentAssignmentsOptions(positionSeconds));

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string trackName, params TimedEvent[] events)
    {
        var chunk = string.IsNullOrEmpty(trackName)
            ? new TrackChunk()
            : new TrackChunk(new SequenceTrackNameEvent(trackName));

        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in events)
            manager.Objects.Add(timedEvent);

        return chunk;
    }

    private static TimedEvent Timed(ChannelEvent midiEvent, long time, int channel = 0)
    {
        midiEvent.Channel = (FourBitNumber)(byte)channel;
        return new TimedEvent(midiEvent, time);
    }

    private static NoteOnEvent NoteOn(int noteNumber, int channel = 0)
        => new((SevenBitNumber)(byte)noteNumber, (SevenBitNumber)(byte)100)
        {
            Channel = (FourBitNumber)(byte)channel,
        };

    private static string TestTrackInstrumentMidiPath
        => Path.Combine(AppContext.BaseDirectory, "data", "test-track-guitar-tone-mode.mid");

    private sealed class FakePreviewSettings : IEditorPreviewSettings
    {
        public uint DefaultInstrumentId { get; set; } = 2;
        public bool ForceDefaultInstrument { get; set; }
        public GuitarToneMode GuitarToneMode { get; set; } = GuitarToneMode.Off;
        public AntiStackType AntiStackType { get; set; } = AntiStackType.Off;
        public int TransposeGlobal { get; set; }
        public bool AdaptNotesOOR { get; set; } = true;
    }

    private sealed class FakeInstrumentCatalog : IEditorPreviewInstrumentCatalog
    {
        public uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument)
        {
            if (forceDefaultInstrument && defaultInstrumentId > 0)
                return defaultInstrumentId;

            var defaultInstrument = defaultInstrumentId > 0 ? (ushort?)defaultInstrumentId : null;
            return TrackInfo.GetInstrumentIdByName(trackName, defaultInstrument);
        }

        public bool IsGuitar(uint instrumentId)
            => instrumentId is >= 24 and <= 28;
    }
}
