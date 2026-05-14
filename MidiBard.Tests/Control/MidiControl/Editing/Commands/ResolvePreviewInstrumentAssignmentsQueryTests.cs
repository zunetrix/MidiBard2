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
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0),
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
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0, channel: 0),
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

    [Fact]
    public void Execute_OverrideByTrackUsesConfiguredToneForEveryChannel()
    {
        var file = CreateEditableFile(CreateTrack(
            "ElectricGuitarOverdriven",
            Timed(new ProgramChangeEvent((SevenBitNumber)(byte)24), 0)));
        var settings = new FakePreviewSettings { GuitarToneMode = GuitarToneMode.OverrideByTrack };
        settings.TrackStatus[0].Tone = 3;

        var result = Execute(file, settings);

        var track = result.Result!.Value.Tracks.Single();
        track.GetResolvedInstrumentId(0).ShouldBe(27u);
        track.GetResolvedInstrumentId(15).ShouldBe(27u);
        track.ResolvedChannelInstrumentIds.ShouldAllBe(instrumentId => instrumentId == 27u);
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

    private sealed class FakePreviewSettings : IEditorPreviewSettings
    {
        public uint DefaultInstrumentId { get; set; } = 2;
        public bool ForceDefaultInstrument { get; set; }
        public GuitarToneMode GuitarToneMode { get; set; } = GuitarToneMode.Off;
        public AntiStackType AntiStackType { get; set; } = AntiStackType.Off;
        public int TransposeGlobal { get; set; }
        public bool AdaptNotesOOR { get; set; } = true;
        public IReadOnlyList<TrackStatus> TrackStatus { get; } =
            Enumerable.Range(0, 100).Select(_ => new TrackStatus()).ToArray();
    }

    private sealed class FakeInstrumentCatalog : IEditorPreviewInstrumentCatalog
    {
        private readonly Dictionary<byte, uint> programInstruments = new()
        {
            [24] = 25,
            [25] = 25,
            [26] = 25,
            [27] = 25,
            [28] = 26,
            [29] = 24,
            [30] = 27,
            [31] = 28,
            [56] = 15,
        };

        public uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument)
        {
            if (forceDefaultInstrument && defaultInstrumentId > 0)
                return defaultInstrumentId;

            var defaultInstrument = defaultInstrumentId > 0 ? (ushort?)defaultInstrumentId : null;
            return TrackInfo.GetInstrumentIdByName(trackName, defaultInstrument);
        }

        public bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId)
            => programInstruments.TryGetValue((byte)program, out instrumentId);

        public bool IsGuitar(uint instrumentId)
            => instrumentId is >= 24 and <= 28;
    }
}
