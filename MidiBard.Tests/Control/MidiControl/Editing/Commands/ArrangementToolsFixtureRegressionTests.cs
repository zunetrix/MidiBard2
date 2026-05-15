using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.Note;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class ArrangementToolsFixtureRegressionTests
{
    [Fact]
    public void MisalignedChordsFixture_TolerantGroupingFindsLargerChordClusterThanExactGrouping()
    {
        var file = LoadEditableFile("misaligned-chords.mid");
        var timingToleranceTicks = MidiForgeNotePrimitives.ResolveChordTimingToleranceTicks(
            file,
            new MidiForgeChordTimingToleranceOptions(MidiForgeChordTimingToleranceMode.OneOver128Note));

        var tolerantMisalignedGroups = file.Tracks
            .SelectMany(track => MidiForgeNotePrimitives.BuildChordNoteGroups(
                track.Chunk.GetNotes(),
                MidiForgeChordSplitStrategy.SameStartTick,
                timingToleranceTicks))
            .Where(group => group.Count >= 2 && group.Max(note => note.Time) > group.Min(note => note.Time))
            .ToArray();

        tolerantMisalignedGroups.ShouldNotBeEmpty();
        tolerantMisalignedGroups
            .Select(group => group.Max(note => note.Time) - group.Min(note => note.Time))
            .Max()
            .ShouldBeLessThanOrEqualTo(timingToleranceTicks);
    }

    [Fact]
    public void LimitNumberNotesFixture_ActiveOverlapLimiterHandlesOverlapsBeyondSameStartCount()
    {
        var file = LoadEditableFile("limit-number-notes.mid");
        var performanceTrackIndices = GetPerformanceTrackIndices(file);

        file.Tracks.Select(track => MaxSameStart(track.Chunk.GetNotes())).Max().ShouldBe(4);
        file.Tracks.Select(track => MaxActiveOverlap(track.Chunk.GetNotes())).Max().ShouldBe(7);

        var session = new MidiEditorSessionState { File = file };
        var result = new EditorCommandExecutor().Execute(
            new LimitSimultaneousNotesCommand(),
            EditorCommandContext.Create(session),
            new LimitSimultaneousNotesCommandOptions(
                performanceTrackIndices,
                new MidiForgeLimitSimultaneousNotesOptions(
                    CreateNewTracks: false,
                    LimitMode: MidiForgeSimultaneousLimitMode.ActiveOverlaps,
                    MaximumActiveNotes: 1,
                    KeepPolicy: MidiForgeNoteKeepPolicy.Highest)));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        result.Result!.Value.RemovedNotes.ShouldBeGreaterThan(0);
        result.Result.UserMessage.ShouldContain("removed");
        file.Tracks.Select(track => MaxActiveOverlap(track.Chunk.GetNotes())).Max().ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void AutoAdaptNightmareFixture_PhraseAwareRangeFitProducesPlayableOutput()
    {
        var phraseAware = ExecuteAdaptRange("auto-adapt-nightmare.mid", MidiForgeRangeFitStrategy.PhraseAwareOctaveFit);

        phraseAware.Result.Succeeded.ShouldBeTrue();
        phraseAware.Result.Result!.Value.ChangedNotes.ShouldBeGreaterThan(0);
        phraseAware.Result.Result.Value.OctaveShiftedTracks.ShouldBeGreaterThan(0);
        phraseAware.File.Tracks.SelectMany(track => track.Chunk.GetNotes()).ShouldAllBe(note =>
            (byte)note.NoteNumber >= MidiForgeAnalysis.PlayableLowestMidiNote &&
            (byte)note.NoteNumber <= MidiForgeAnalysis.PlayableHighestMidiNote);
    }

    private static EditorCommandExecutionResult<MidiForgeSplitChordsResult> ExecuteSplitChords(
        string fileName,
        MidiForgeSplitChordsOptions options)
    {
        var file = LoadEditableFile(fileName);
        var session = new MidiEditorSessionState { File = file };
        return new EditorCommandExecutor().Execute(
            new SplitTracksChordsCommand(),
            EditorCommandContext.Create(session),
            new SplitTracksChordsCommandOptions(GetPerformanceTrackIndices(file), options));
    }

    private static (EditableMidiFile File, EditorCommandExecutionResult<MidiForgeAdaptToRangeResult> Result) ExecuteAdaptRange(
        string fileName,
        MidiForgeRangeFitStrategy strategy)
    {
        var file = LoadEditableFile(fileName);
        var session = new MidiEditorSessionState { File = file };
        var result = new EditorCommandExecutor().Execute(
            new AdaptTracksToPlayableRangeCommand(),
            EditorCommandContext.Create(session),
            new AdaptTracksToPlayableRangeCommandOptions(
                GetPerformanceTrackIndices(file),
                new MidiForgeAdaptToRangeOptions(
                    CreateNewTracks: false,
                    RangeStrategy: strategy,
                    RenameTracks: false)));

        return (file, result);
    }

    private static EditableMidiFile LoadEditableFile(string fileName)
        => new(MidiFile.Read(FindDataFile(fileName)));

    private static int[] GetPerformanceTrackIndices(EditableMidiFile file)
        => file.Tracks
            .Select((track, index) => new { Track = track, Index = index })
            .Where(item => !item.Track.IsConductorTrack && item.Track.Chunk.GetNotes().Any())
            .Select(item => item.Index)
            .ToArray();

    private static int MaxSameStart(IEnumerable<Note> notes)
        => notes
            .GroupBy(note => note.Time)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();

    private static int MaxActiveOverlap(IEnumerable<Note> source)
    {
        var notes = source.Where(note => note.Length > 0).ToArray();
        return notes
            .SelectMany(note => new[] { note.Time, note.EndTime })
            .Distinct()
            .Select(tick => notes.Count(note => note.Time <= tick && tick < note.EndTime))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string FindDataFile(string fileName)
    {
        var outputCandidate = Path.Combine(AppContext.BaseDirectory, "data", fileName);
        if (File.Exists(outputCandidate))
            return outputCandidate;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "MidiBard.Tests",
                "Data",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find test data file {fileName}.");
    }
}
