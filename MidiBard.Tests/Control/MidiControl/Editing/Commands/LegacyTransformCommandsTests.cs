using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class LegacyTransformCommandsTests
{
    [Fact]
    public void TransposeTracks_TransposesNotesAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new TransposeTracksCommand(),
            EditorCommandContext.Create(session),
            new TransposeTracksOptions(new[] { 0 }, Semitones: 12));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        result.Result.Value.ChangedNotes.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)72);
        session.History.UndoCount.ShouldBe(1);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();
        session.PendingRefreshHints.ClearTrackSelection.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Single().NoteNumber.ShouldBe((SevenBitNumber)60);
    }

    [Fact]
    public void TransposeTracks_ZeroSemitonesDoesNotDirty()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorCommandExecutor().Execute(
            new TransposeTracksCommand(),
            EditorCommandContext.Create(session),
            new TransposeTracksOptions(new[] { 0 }, Semitones: 0));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.SourceTracks.ShouldBe(1);
        file.Version.ShouldBe(beforeVersion);
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void MergeTracks_CreatesMergedTrackAndSupportsUndo()
    {
        var file = CreateEditableFile(
            CreateTrack("Piano", Note(60, 0, 120)),
            CreateTrack("Flute", Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new MergeTracksCommand(),
            EditorCommandContext.Create(session),
            new MergeTracksOptions(
                TargetTrackIndex: 0,
                TrackIndices: new[] { 0, 1 },
                IncludeProgramChanges: true,
                IncludePitchBends: true,
                IncludeControlChanges: true,
                ToleranceMilliseconds: 0,
                RemoveEqualNotes: true,
                DeleteOriginalTracks: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.SourceTracks.ShouldBe(2);
        result.Result.Value.CreatedTrackIndices.ShouldBe(new[] { 1 });
        file.Tracks.Count.ShouldBe(3);
        file.Tracks[1].Name.ShouldBe("Piano (merged)");
        file.Tracks[1].Chunk.GetNotes().Select(note => (int)(byte)note.NoteNumber)
            .OrderBy(note => note)
            .ShouldBe(new[] { 60, 72 });
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks.Count.ShouldBe(2);
    }

    [Fact]
    public void QuantizeTracks_QuantizesTrackTimingAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack("Piano", Note(60, 37, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new QuantizeTracksCommand(),
            EditorCommandContext.Create(session),
            new QuantizeTracksOptions(
                new[] { 0 },
                QuarterGrid(),
                DefaultQuantizingSettings(),
                CreateNewTracks: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedTracks.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes().Single().Time.ShouldBe(0);
        session.PendingRefreshHints.ReloadEventList.ShouldBeTrue();

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Single().Time.ShouldBe(37);
    }

    [Fact]
    public void QuantizeSelectedNotes_OnlyQuantizesSelectedKeys()
    {
        var file = CreateEditableFile(CreateTrack(
            "Piano",
            Note(60, 37, 120),
            Note(64, 53, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new QuantizeSelectedNotesCommand(),
            EditorCommandContext.Create(session),
            new QuantizeSelectedNotesOptions(
                0,
                new[] { (tick: 37L, noteNum: (byte)60, channel: (byte)0) },
                QuarterGrid(),
                DefaultQuantizingSettings()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.ChangedEvents.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes()
            .OrderBy(note => (byte)note.NoteNumber)
            .Select(note => note.Time)
            .ShouldBe(new[] { 0L, 53L });
        session.PendingRefreshHints.ClearEventSelection.ShouldBeTrue();
    }

    [Fact]
    public void SanitizeFile_RemovesDuplicateNotesAndSupportsUndo()
    {
        var file = CreateEditableFile(CreateTrack(
            "Piano",
            Note(60, 0, 120),
            Note(60, 0, 120)));
        var session = new MidiEditorSessionState { File = file };

        var result = new EditorCommandExecutor().Execute(
            new SanitizeFileCommand(),
            EditorCommandContext.Create(session),
            new SanitizeFileOptions(new SanitizingSettings
            {
                RemoveDuplicatedNotes = true,
            }));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.TrackCount.ShouldBe(1);
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(1);
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(1);

        session.History.Undo(file).ShouldBeTrue();
        file.Tracks[0].Chunk.GetNotes().Count().ShouldBe(2);
    }

    private static IGrid QuarterGrid()
        => new SteppedGrid(MusicalTimeSpan.Quarter);

    private static QuantizingSettings DefaultQuantizingSettings()
        => new()
        {
            Target = QuantizerTarget.Start,
            QuantizingLevel = 1,
            FixOppositeEnd = true,
            QuantizingBeyondZeroPolicy = QuantizingBeyondZeroPolicy.FixAtZero,
            QuantizingBeyondFixedEndPolicy = QuantizingBeyondFixedEndPolicy.CollapseAndFix,
        };

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(new MidiFile(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        });

    private static TrackChunk CreateTrack(string name, params Note[] notes)
    {
        var chunk = string.IsNullOrEmpty(name)
            ? new TrackChunk()
            : new TrackChunk(new SequenceTrackNameEvent(name));
        using var manager = chunk.ManageNotes();

        foreach (var note in notes)
            manager.Objects.Add(note);

        return chunk;
    }

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
