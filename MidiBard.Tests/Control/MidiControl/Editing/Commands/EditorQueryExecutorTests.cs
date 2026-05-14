using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class EditorQueryExecutorTests
{
    [Fact]
    public void Execute_ReturnsQueryResultWithoutMutatingFile()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var beforeVersion = file.Version;

        var result = new EditorQueryExecutor().Execute(
            new CountTracksQuery(),
            EditorQueryContext.Create(session),
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeTrue();
        result.Result!.Value.TrackCount.ShouldBe(1);
        file.Version.ShouldBe(beforeVersion);
        file.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void CreateQueryContext_CapturesSelectionSnapshot()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        session.Selection.SelectedTrackIndex = 0;
        session.Selection.SelectedTrackIndices.Add(0);

        var context = EditorQueryContext.Create(session);

        session.Selection.SelectedTrackIndex = -1;
        session.Selection.SelectedTrackIndices.Clear();

        context.Selection.SelectedTrackIndex.ShouldBe(0);
        context.Selection.SelectedTrackIndices.ShouldBe(new[] { 0 });
    }

    [Fact]
    public void Execute_RejectedQueryDoesNotRun()
    {
        var file = CreateEditableFile(Note(60, 0, 120));
        var session = new MidiEditorSessionState { File = file };
        var query = new RejectingQuery();

        var result = new EditorQueryExecutor().Execute(
            query,
            EditorQueryContext.Create(session),
            new EditorOperationEmptyOptions());

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Rejected for test.");
        query.ExecuteCount.ShouldBe(0);
    }

    private static EditableMidiFile CreateEditableFile(params Note[] notes)
    {
        var chunk = new TrackChunk();
        using (var manager = chunk.ManageTimedEvents())
        {
            foreach (var note in notes)
            {
                manager.Objects.Add(new TimedEvent(
                    new NoteOnEvent(note.NoteNumber, note.Velocity) { Channel = note.Channel },
                    note.Time));
                manager.Objects.Add(new TimedEvent(
                    new NoteOffEvent(note.NoteNumber, note.OffVelocity) { Channel = note.Channel },
                    note.EndTime));
            }
        }

        return new EditableMidiFile(new MidiFile(chunk));
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

    private sealed record CountTracksResult(int TrackCount);

    [EditorOperation(
        "test.count-tracks",
        "Count Tracks",
        Kind = EditorOperationKind.Query,
        HistoryPolicy = HistoryPolicy.None)]
    private sealed class CountTracksQuery
        : EditorOperationBase, IEditorQuery<EditorOperationEmptyOptions, CountTracksResult>
    {
        public EditorCommandValidation Validate(EditorQueryContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Success;

        public EditorQueryResult<CountTracksResult> Execute(
            EditorQueryContext context,
            EditorOperationEmptyOptions options)
            => new(new CountTracksResult(context.File.Tracks.Count));
    }

    [EditorOperation(
        "test.rejecting-query",
        "Rejecting Query",
        Kind = EditorOperationKind.Query,
        HistoryPolicy = HistoryPolicy.None)]
    private sealed class RejectingQuery
        : EditorOperationBase, IEditorQuery<EditorOperationEmptyOptions, CountTracksResult>
    {
        public int ExecuteCount { get; private set; }

        public EditorCommandValidation Validate(EditorQueryContext context, EditorOperationEmptyOptions options)
            => EditorCommandValidation.Failure("Rejected for test.");

        public EditorQueryResult<CountTracksResult> Execute(
            EditorQueryContext context,
            EditorOperationEmptyOptions options)
        {
            ExecuteCount++;
            return new EditorQueryResult<CountTracksResult>(new CountTracksResult(0));
        }
    }
}
