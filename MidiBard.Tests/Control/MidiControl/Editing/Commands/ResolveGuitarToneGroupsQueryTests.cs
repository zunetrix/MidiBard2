using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Guitar;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ResolveGuitarToneGroupsQueryTests
{
    [Fact]
    public void Execute_ResolvesTonesByOverrideJsonTrackNameAndProgramPrecedence()
    {
        var file = CreateEditableFile("/tmp/song.mid",
            CreateTrack("ElectricGuitarClean", Note(60, 0, 120)),
            CreateTrack("Json Guitar", Note(62, 120, 120)),
            CreateTrack("ElectricGuitarPowerChords", Note(64, 240, 120)),
            CreateTrack("AuxiliaryCarrier",
                Timed(new ProgramChangeEvent((SevenBitNumber)31), 0),
                Note(65, 360, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new ResolveGuitarToneGroupsQuery(),
            EditorQueryContext.Create(session),
            new ResolveGuitarToneGroupsQueryOptions(
                new[] { 0, 1, 2, 3 },
                new MidiForgeGuitarToneOverrideSnapshot(
                    GuitarToneMode.OverrideByTrack,
                    "/tmp/song.mid",
                    new Dictionary<int, int> { [0] = 4 }),
                new MidiForgeGuitarToneJsonConfigSnapshot(new[]
                {
                    new MidiForgeGuitarToneJsonTrack(0, "ElectricGuitarClean", 24),
                    new MidiForgeGuitarToneJsonTrack(1, "Json Guitar", 26),
                })));

        result.Succeeded.ShouldBeTrue();
        MidiForgeGuitarTonePrimitives.TryResolveToneFromProgram((SevenBitNumber)31, out var programTone).ShouldBeTrue();
        result.Result!.Value.SelectedTracks.ShouldBe(4);
        result.Result.Value.ResolvedTracks.ShouldBe(4);
        result.Result.Value.MergeableTracks.ShouldBe(4);
        result.Result.Value.ToneByTrackIndex.ShouldBe(new Dictionary<int, int>
        {
            [0] = 4,
            [1] = 2,
            [2] = 3,
            [3] = programTone,
        });
        result.Result.Value.Tracks.Select(track => track.Source).ShouldBe(new[]
        {
            MidiForgeGuitarToneResolutionSource.CurrentOverride,
            MidiForgeGuitarToneResolutionSource.JsonFile,
            MidiForgeGuitarToneResolutionSource.TrackName,
            MidiForgeGuitarToneResolutionSource.ProgramChange,
        });
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Execute_IgnoresOverrideWhenModeOrPathDoesNotMatch()
    {
        var file = CreateEditableFile("/tmp/song.mid",
            CreateTrack("ElectricGuitarClean", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new ResolveGuitarToneGroupsQuery(),
            EditorQueryContext.Create(session),
            new ResolveGuitarToneGroupsQueryOptions(
                new[] { 0 },
                new MidiForgeGuitarToneOverrideSnapshot(
                    GuitarToneMode.Standard,
                    "/tmp/song.mid",
                    new Dictionary<int, int> { [0] = 4 })));

        result.Succeeded.ShouldBeTrue();
        var track = result.Result!.Value.Tracks.Single();
        track.Source.ShouldBe(MidiForgeGuitarToneResolutionSource.TrackName);
        track.Tone.ShouldBe(1);
    }

    [Fact]
    public void Execute_IgnoresJsonSnapshotWhenAnyJsonTrackDoesNotMatchTheFile()
    {
        var file = CreateEditableFile("/tmp/song.mid",
            CreateTrack("Unknown", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new ResolveGuitarToneGroupsQuery(),
            EditorQueryContext.Create(session),
            new ResolveGuitarToneGroupsQueryOptions(
                new[] { 0 },
                JsonConfig: new MidiForgeGuitarToneJsonConfigSnapshot(new[]
                {
                    new MidiForgeGuitarToneJsonTrack(0, "Different", 25),
                })));

        result.Succeeded.ShouldBeTrue();
        var track = result.Result!.Value.Tracks.Single();
        track.IsResolved.ShouldBeFalse();
        track.Source.ShouldBe(MidiForgeGuitarToneResolutionSource.None);
        result.Result.Value.ToneByTrackIndex.ShouldBeEmpty();
    }

    [Fact]
    public void Execute_ReportsResolvedEmptyTracksAsNotMergeable()
    {
        var file = CreateEditableFile("/tmp/song.mid",
            CreateTrack("ElectricGuitarClean", Timed(new ProgramChangeEvent((SevenBitNumber)0), 0)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new ResolveGuitarToneGroupsQuery(),
            EditorQueryContext.Create(session),
            new ResolveGuitarToneGroupsQueryOptions(new[] { 0 }));

        result.Succeeded.ShouldBeTrue();
        var track = result.Result!.Value.Tracks.Single();
        track.IsResolved.ShouldBeTrue();
        track.HasNotes.ShouldBeFalse();
        track.IsMergeable.ShouldBeFalse();
        result.Result.Value.ResolvedTracks.ShouldBe(1);
        result.Result.Value.MergeableTracks.ShouldBe(0);
    }

    [Fact]
    public void Execute_ReportsWhenResolvedTrackCountExceedsMergeChannelLimit()
    {
        var sourceCount = MidiForgeGuitarTonePrimitives.MaximumMergeTracks + 1;
        var file = CreateEditableFile("/tmp/song.mid",
            Enumerable.Range(0, sourceCount)
                .Select(index => CreateTrack($"ElectricGuitarClean {index}", Note(60, index * 120, 60)))
                .ToArray());
        var tones = Enumerable.Range(0, sourceCount).ToDictionary(index => index, _ => 1);
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorQueryExecutor().Execute(
            new ResolveGuitarToneGroupsQuery(),
            EditorQueryContext.Create(session),
            new ResolveGuitarToneGroupsQueryOptions(
                Enumerable.Range(0, sourceCount).ToArray(),
                new MidiForgeGuitarToneOverrideSnapshot(
                    GuitarToneMode.OverrideByTrack,
                    "/tmp/song.mid",
                    tones)));

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.ResolvedTracks.ShouldBe(sourceCount);
        result.Result.Value.MergeableTracks.ShouldBe(sourceCount);
        result.Result.Value.MaximumMergeableTracks.ShouldBe(MidiForgeGuitarTonePrimitives.MaximumMergeTracks);
        result.Result.Value.ExceedsMaximumResolvedTracks.ShouldBeTrue();
    }

    private static EditableMidiFile CreateEditableFile(string filePath, params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        }, filePath);

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
