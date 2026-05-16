using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Editing.Commands.AutoEdit;
using MidiBard.Control.MidiControl.Editing.Commands.File;
using MidiBard.Control.MidiControl.Editing.Commands.Track;
using MidiBard.Control.MidiControl.Editing.State;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class FileDocumentCommandsTests
{
    [Fact]
    public void OpenLoadedMidiFile_ReplacesCurrentDocumentAndClearsHistory()
    {
        var oldFile = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        var newMidi = CreateMidiFile(CreateTrack(Note(72, 240, 120)));
        var session = new MidiEditorSessionState { File = oldFile };
        session.History.Capture(oldFile);

        var result = new EditorCommandExecutor().Execute(
            new OpenLoadedMidiFileCommand(),
            EditorCommandContext.Create(session, requireFile: false),
            new OpenLoadedMidiFileOptions(newMidi, "/tmp/new.mid", IsDirty: false));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        session.File.ShouldNotBeSameAs(oldFile);
        session.File.FilePath.ShouldBe("/tmp/new.mid");
        session.File.IsDirty.ShouldBeFalse();
        session.History.UndoCount.ShouldBe(0);
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
    }

    [Fact]
    public void OpenLoadedMidiFile_CanOpenDirtyImportedDocumentWithoutDirtyingAgain()
    {
        var session = new MidiEditorSessionState();
        var midi = CreateMidiFile(CreateTrack(Note(60, 0, 120)));

        var result = new EditorCommandExecutor().Execute(
            new OpenLoadedMidiFileCommand(),
            EditorCommandContext.Create(session, requireFile: false),
            new OpenLoadedMidiFileOptions(midi, null, IsDirty: true, DisplayName: "Imported.mid"));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        session.File.DisplayName.ShouldBe("Imported.mid");
        session.File.IsDirty.ShouldBeTrue();
        session.IsDirty.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void OpenNormalizedMidiFile_NormalizesLoadedMidiAndReportsSummary()
    {
        var session = new MidiEditorSessionState();
        var midi = CreateMidiFile(
            CreateTrack(),
            CreateTrack(Note(60, 0, 120)));

        var result = new EditorCommandExecutor().Execute(
            new OpenNormalizedMidiFileCommand(),
            EditorCommandContext.Create(session, requireFile: false),
            new OpenNormalizedMidiFileOptions(
                midi,
                "/tmp/import.mid",
                new MidiForgeImportOptions()));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeFalse();
        result.Result!.Value.ImportResult.RemovedEmptyTracks.ShouldBe(1);
        result.Result.Value.Summary.ShouldContain("removed 1 empty track(s)");
        session.File.FilePath.ShouldBe("/tmp/import.mid");
        session.File.IsDirty.ShouldBeTrue();
        session.History.UndoCount.ShouldBe(0);
        session.PendingRefreshHints.ReloadTrackList.ShouldBeTrue();
    }

    [Fact]
    public void ExportLrcMetadata_FlushesLoadedEventEditsBeforeWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.lrc");
        try
        {
            var file = CreateEditableFile(CreateTrack(
                Timed(new LyricEvent("hello"), 120),
                Note(60, 0, 480)));
            file.Tracks[0].LoadEvents(file.TempoMap);
            var lyricEvent = file.Tracks[0].Events!.Single(item => item.Source.Event is LyricEvent);
            lyricEvent.Tick = 240;
            var session = new MidiEditorSessionState { File = file };

            var result = new EditorCommandExecutor().Execute(
                new ExportLrcMetadataCommand(),
                EditorCommandContext.Create(session),
                new ExportLrcMetadataOptions(path, "Song"));

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            result.Result!.Value.HasLyrics.ShouldBeTrue();
            result.Result.Value.LineCount.ShouldBe(1);
            var content = System.IO.File.ReadAllText(path);
            content.ShouldContain("[00:00.25]hello");
            session.History.UndoCount.ShouldBe(0);
            file.IsDirty.ShouldBeFalse();
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ExportLrcMetadata_WritesBlankTemplateWhenNoLyricsExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.lrc");
        try
        {
            var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
            var session = new MidiEditorSessionState { File = file };

            var result = new EditorCommandExecutor().Execute(
                new ExportLrcMetadataCommand(),
                EditorCommandContext.Create(session),
                new ExportLrcMetadataOptions(path, "Instrumental"));

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            result.Result!.Value.HasLyrics.ShouldBeFalse();
            System.IO.File.ReadAllText(path).ShouldContain("[00:00.00]");
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_WritesExistingPathAndMarksDocumentClean()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
            file.FilePath = path;
            file.MarkChanged();
            var session = new MidiEditorSessionState { File = file };

            var result = new EditorCommandExecutor().Execute(
                new SaveFileCommand(),
                EditorCommandContext.Create(session),
                new EditorOperationEmptyOptions());

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            System.IO.File.Exists(path).ShouldBeTrue();
            file.IsDirty.ShouldBeFalse();
            session.IsDirty.ShouldBeFalse();
            session.History.UndoCount.ShouldBe(0);
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_FlushesLoadedEventEditsBeforeWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
            file.FilePath = path;
            file.Tracks[0].LoadEvents(file.TempoMap);
            var editableEvent = file.Tracks[0].Events!.Single(item => item.NoteOffSource != null);
            editableEvent.EditTick = 240;
            editableEvent.EditValue1 = 64;
            editableEvent.EditValue2 = 90;
            editableEvent.EditDuration = 360;
            editableEvent.ApplyEditValues();
            file.MarkChanged();
            var session = new MidiEditorSessionState { File = file };

            var result = new EditorCommandExecutor().Execute(
                new SaveFileCommand(),
                EditorCommandContext.Create(session),
                new EditorOperationEmptyOptions());

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            var savedNote = MidiFile.Read(path).GetNotes().Single();
            savedNote.Time.ShouldBe(240);
            savedNote.Length.ShouldBe(360);
            savedNote.NoteNumber.ShouldBe((SevenBitNumber)64);
            savedNote.Velocity.ShouldBe((SevenBitNumber)90);
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_WritesCurrentStateAfterUndoAndNewChange()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var (file, session) = CreateFileAfterMixedCommandUndoAndNewChange();
            file.FilePath = path;

            var result = new EditorCommandExecutor().Execute(
                new SaveFileCommand(),
                EditorCommandContext.Create(session),
                new EditorOperationEmptyOptions());

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            AssertSavedCurrentState(path);
            file.IsDirty.ShouldBeFalse();
            session.IsDirty.ShouldBeFalse();
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SaveFileAs_UpdatesPathDisplayNameAndMarksDocumentClean()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
            file.MarkChanged();
            var session = new MidiEditorSessionState { File = file };

            var result = new EditorCommandExecutor().Execute(
                new SaveFileAsCommand(),
                EditorCommandContext.Create(session),
                new SaveFileAsOptions(path));

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            file.FilePath.ShouldBe(path);
            file.DisplayName.ShouldBe(Path.GetFileName(path));
            file.IsDirty.ShouldBeFalse();
            session.IsDirty.ShouldBeFalse();
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SaveFileAs_FlushesLoadedEventEditsBeforeWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var file = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
            file.Tracks[0].LoadEvents(file.TempoMap);
            var editableEvent = file.Tracks[0].Events!.Single(item => item.NoteOffSource != null);
            editableEvent.EditTick = 120;
            editableEvent.EditValue1 = 67;
            editableEvent.EditValue2 = 91;
            editableEvent.EditDuration = 240;
            editableEvent.ApplyEditValues();
            file.MarkChanged();
            var session = new MidiEditorSessionState { File = file };

            var result = new EditorCommandExecutor().Execute(
                new SaveFileAsCommand(),
                EditorCommandContext.Create(session),
                new SaveFileAsOptions(path));

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            var savedNote = MidiFile.Read(path).GetNotes().Single();
            savedNote.Time.ShouldBe(120);
            savedNote.Length.ShouldBe(240);
            savedNote.NoteNumber.ShouldBe((SevenBitNumber)67);
            savedNote.Velocity.ShouldBe((SevenBitNumber)91);
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SaveFileAs_WritesCurrentStateAfterUndoAndNewChange()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var (file, session) = CreateFileAfterMixedCommandUndoAndNewChange();

            var result = new EditorCommandExecutor().Execute(
                new SaveFileAsCommand(),
                EditorCommandContext.Create(session),
                new SaveFileAsOptions(path));

            result.Succeeded.ShouldBeTrue();
            result.Changed.ShouldBeFalse();
            AssertSavedCurrentState(path);
            file.FilePath.ShouldBe(path);
            file.DisplayName.ShouldBe(Path.GetFileName(path));
            file.IsDirty.ShouldBeFalse();
            session.IsDirty.ShouldBeFalse();
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void MergeSong_ReplacesDocumentMarksDirtyAndClearsHistory()
    {
        var original = CreateEditableFile(CreateTrack(Note(60, 0, 120)));
        original.FilePath = "/tmp/original.mid";
        var imported = CreateMidiFile(CreateTrack(Note(72, 120, 120)));
        var session = new MidiEditorSessionState { File = original };
        session.History.Capture(original);

        var result = new EditorCommandExecutor().Execute(
            new MergeSongCommand(),
            EditorCommandContext.Create(session),
            new MergeSongOptions(
                imported,
                Sequential: false,
                DelayMilliseconds: 0,
                IgnoreDifferentTempoMaps: true));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        session.File.ShouldNotBeSameAs(original);
        session.File.FilePath.ShouldBe("/tmp/original.mid");
        session.File.IsDirty.ShouldBeTrue();
        session.File.Tracks.SelectMany(track => track.Chunk.GetNotes())
            .Select(note => (int)(byte)note.NoteNumber)
            .OrderBy(noteNumber => noteNumber)
            .ShouldBe(new[] { 60, 72 });
        session.History.UndoCount.ShouldBe(0);
        session.PendingRefreshHints.ClearSelectedTrack.ShouldBeTrue();
    }

    [Fact]
    public void MergeSong_SequentialPlacesImportedFileAfterCurrentFileAndDelay()
    {
        var original = CreateEditableFile(CreateTrack(Note(60, 0, 480)));
        var imported = CreateMidiFile(CreateTrack(Note(72, 0, 240)));
        var session = new MidiEditorSessionState { File = original };

        var result = new EditorCommandExecutor().Execute(
            new MergeSongCommand(),
            EditorCommandContext.Create(session),
            new MergeSongOptions(
                imported,
                Sequential: true,
                DelayMilliseconds: 500,
                IgnoreDifferentTempoMaps: true));

        result.Succeeded.ShouldBeTrue();
        result.Changed.ShouldBeTrue();
        session.File.Tracks.SelectMany(track => track.Chunk.GetNotes())
            .Select(note => ((int)(byte)note.NoteNumber, note.Time, note.Length))
            .OrderBy(note => note.Time)
            .ShouldBe(new[]
            {
                (60, 0L, 480L),
                (72, 960L, 240L)
            });
        session.History.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void ReplaceCurrentFile_RejectsMissingMidiFile()
    {
        var session = new MidiEditorSessionState();

        var result = new EditorCommandExecutor().Execute(
            new ReplaceCurrentFileCommand(),
            EditorCommandContext.Create(session, requireFile: false),
            new ReplaceCurrentFileOptions(null, null, IsDirty: false));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Choose a MIDI file.");
        session.File.ShouldBeNull();
    }

    [Fact]
    public void ReplaceCurrentFile_CanMergeMultipleConductorTracks()
    {
        var midi = CreateMidiFile(
            CreateTrack(Timed(new SetTempoEvent(500000), 0), Timed(new TimeSignatureEvent(4, 2), 0)),
            CreateTrack(Timed(new SetTempoEvent(600000), 240)),
            CreateTrack(Note(60, 0, 120)));
        var session = new MidiEditorSessionState();

        var result = new EditorCommandExecutor().Execute(
            new ReplaceCurrentFileCommand(),
            EditorCommandContext.Create(session, requireFile: false),
            new ReplaceCurrentFileOptions(
                midi,
                null,
                IsDirty: false,
                MergeMultipleConductorTracks: true,
                ConsolidateTempoToConductorTrack: false));

        result.Succeeded.ShouldBeTrue();
        session.File.Tracks.Count(track => track.IsConductorTrack).ShouldBe(1);
        session.File.Tracks.Count.ShouldBe(2);
        session.File.Tracks[0].Chunk.GetTimedEvents()
            .Where(timedEvent => timedEvent.Event is SetTempoEvent)
            .Select(timedEvent => timedEvent.Time)
            .OrderBy(time => time)
            .ShouldBe(new long[] { 0, 240 });
        session.File.Tracks[0].Chunk.Events.OfType<TimeSignatureEvent>().Count().ShouldBe(1);
        session.File.IsDirty.ShouldBeFalse();
    }

    private static EditableMidiFile CreateEditableFile(params TrackChunk[] chunks)
        => new(CreateMidiFile(chunks));

    private static (EditableMidiFile File, MidiEditorSessionState Session) CreateFileAfterMixedCommandUndoAndNewChange()
    {
        var file = CreateEditableFile(CreateTrack(
            Note(60, 0, 120),
            Note(64, 0, 120),
            Note(67, 0, 120),
            Note(100, 240, 120)));
        var session = new MidiEditorSessionState { File = file };
        var executor = new EditorCommandExecutor();
        var context = EditorCommandContext.Create(session);

        executor.Execute(
            new RenameTrackCommand(),
            context,
            new RenameTrackOptions(0, "Intermediate"))
            .Succeeded.ShouldBeTrue();

        executor.Execute(
            new TransposeTracksCommand(),
            context,
            new TransposeTracksOptions(new[] { 0 }, Semitones: 12))
            .Succeeded.ShouldBeTrue();

        executor.Execute(
            new AutoEditSelectedTracksCommand(),
            context,
            new AutoEditSelectedTracksCommandOptions(
                new[] { 0 },
                new MidiForgeAutoEditOptions(
                    MaxSimultaneousNotes: 1,
                    AdaptOutOfRangeNotes: true,
                    CreateNewTracks: false)))
            .Succeeded.ShouldBeTrue();

        session.History.UndoCount.ShouldBe(3);
        while (session.History.CanUndo)
            session.History.Undo(file).ShouldBeTrue();

        file.IsDirty.ShouldBeFalse();
        file.Tracks.Count.ShouldBe(1);
        file.Tracks[0].Name.ShouldBeEmpty();
        file.Tracks[0].Chunk.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .OrderBy(noteNumber => noteNumber)
            .ShouldBe(new[] { 60, 64, 67, 100 });

        executor.Execute(
            new RenameTrackCommand(),
            context,
            new RenameTrackOptions(0, "Final Lead"))
            .Succeeded.ShouldBeTrue();

        executor.Execute(
            new TransposeTracksCommand(),
            context,
            new TransposeTracksOptions(new[] { 0 }, Semitones: 2))
            .Succeeded.ShouldBeTrue();

        file.IsDirty.ShouldBeTrue();
        session.History.RedoCount.ShouldBe(0);
        return (file, session);
    }

    private static void AssertSavedCurrentState(string path)
    {
        var saved = MidiFile.Read(path);

        saved.GetTrackChunks()
            .SelectMany(track => track.Events.OfType<SequenceTrackNameEvent>())
            .Select(trackName => trackName.Text)
            .ShouldBe(new[] { "Final Lead" });

        saved.GetNotes()
            .Select(note => (int)(byte)note.NoteNumber)
            .OrderBy(noteNumber => noteNumber)
            .ShouldBe(new[] { 62, 66, 69, 102 });
    }

    private static MidiFile CreateMidiFile(params TrackChunk[] chunks)
        => new(chunks)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

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
}
