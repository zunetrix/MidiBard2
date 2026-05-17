using System;
using System.IO;
using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;

namespace MidiBard.Control.MidiControl.Editing.Commands.File;

public sealed record FileDocumentResult(
    string DisplayName,
    string FilePath,
    bool IsDirty,
    int TrackCount)
{
    public static FileDocumentResult FromFile(EditableMidiFile file)
        => new(file.DisplayName, file.FilePath, file.IsDirty, file.Tracks.Count);
}

public sealed record OpenLoadedMidiFileOptions(
    MidiFile MidiFile,
    string FilePath,
    bool IsDirty,
    string DisplayName = null);

public sealed record ReplaceCurrentFileOptions(
    MidiFile MidiFile,
    string FilePath,
    bool IsDirty,
    string DisplayName = null,
    bool MergeMultipleConductorTracks = false,
    bool ConsolidateTempoToConductorTrack = true,
    SanitizingSettings SanitizingSettings = null);

public sealed record SaveFileAsOptions(string FilePath);

public sealed record MergeSongOptions(
    MidiFile ImportedFile,
    bool Sequential,
    int DelayMilliseconds,
    bool IgnoreDifferentTempoMaps);

[EditorOperation(
    "file.open-loaded",
    "Open Loaded MIDI File",
    Scope = EditorOperationScope.File,
    RequiresFile = false,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class OpenLoadedMidiFileCommand
    : EditorOperationBase, IEditorCommand<OpenLoadedMidiFileOptions, FileDocumentResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, OpenLoadedMidiFileOptions options)
        => options.MidiFile is null
            ? EditorCommandValidation.Failure("Choose a MIDI file to open.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<FileDocumentResult> Execute(
        EditorCommandContext context,
        OpenLoadedMidiFileOptions options)
    {
        var result = context.Invoker.Execute(
            new ReplaceCurrentFileCommand(),
            new ReplaceCurrentFileOptions(
                options.MidiFile,
                options.FilePath,
                options.IsDirty,
                options.DisplayName,
                MergeMultipleConductorTracks: true,
                ConsolidateTempoToConductorTrack: true,
                SanitizingSettings: FileDocumentCommandHelpers.MergeSongSanitizingSettings()));

        if (!result.Succeeded)
            return EditorCommandResult<FileDocumentResult>.NoChange(result.Message);

        return EditorCommandResult<FileDocumentResult>.UnchangedResult(
            result.Result!.Value,
            refreshHints: result.Result.RefreshHints);
    }
}

[EditorOperation(
    "file.replace-current",
    "Replace Current MIDI File",
    Scope = EditorOperationScope.File,
    RequiresFile = false,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class ReplaceCurrentFileCommand
    : EditorOperationBase, IEditorCommand<ReplaceCurrentFileOptions, FileDocumentResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ReplaceCurrentFileOptions options)
        => options.MidiFile is null
            ? EditorCommandValidation.Failure("Choose a MIDI file.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<FileDocumentResult> Execute(
        EditorCommandContext context,
        ReplaceCurrentFileOptions options)
    {
        var replacement = new EditableMidiFile(options.MidiFile, options.FilePath, options.DisplayName);

        if (options.MergeMultipleConductorTracks)
            FileDocumentCommandHelpers.MergeMultipleConductorTracks(replacement);

        if (options.ConsolidateTempoToConductorTrack)
            FileDocumentCommandHelpers.ConsolidateTempoToConductorTrack(replacement);

        if (options.SanitizingSettings is not null)
            FileDocumentCommandHelpers.ApplySanitize(replacement, options.SanitizingSettings);

        replacement.SetDirtyStateForLoad(options.IsDirty);

        var previousFile = context.Session.File;
        context.Session.File = replacement;
        context.Session.IsDirty = replacement.IsDirty;
        context.Session.History.Clear();

        if (!ReferenceEquals(previousFile, replacement))
        {
            foreach (var track in previousFile?.Tracks ?? [])
                track.Dispose();
        }

        return EditorCommandResult<FileDocumentResult>.UnchangedResult(
            FileDocumentResult.FromFile(replacement),
            refreshHints: FileDocumentCommandHelpers.DocumentReplacedHints);
    }
}

[EditorOperation(
    "file.save",
    "Save MIDI File",
    Scope = EditorOperationScope.File,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class SaveFileCommand
    : EditorOperationBase, IEditorCommand<EditorOperationEmptyOptions, FileDocumentResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, EditorOperationEmptyOptions options)
        => string.IsNullOrWhiteSpace(context.File.FilePath)
            ? EditorCommandValidation.Failure("Choose a save path before saving.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<FileDocumentResult> Execute(
        EditorCommandContext context,
        EditorOperationEmptyOptions options)
    {
        context.File.Save();
        context.Session.IsDirty = context.File.IsDirty;

        return EditorCommandResult<FileDocumentResult>.UnchangedResult(
            FileDocumentResult.FromFile(context.File));
    }
}

[EditorOperation(
    "file.save-as",
    "Save MIDI File As",
    Scope = EditorOperationScope.File,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class SaveFileAsCommand
    : EditorOperationBase, IEditorCommand<SaveFileAsOptions, FileDocumentResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, SaveFileAsOptions options)
        => string.IsNullOrWhiteSpace(options.FilePath)
            ? EditorCommandValidation.Failure("Choose a save path.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<FileDocumentResult> Execute(
        EditorCommandContext context,
        SaveFileAsOptions options)
    {
        context.File.SaveAs(options.FilePath);
        context.Session.IsDirty = context.File.IsDirty;

        return EditorCommandResult<FileDocumentResult>.UnchangedResult(
            FileDocumentResult.FromFile(context.File));
    }
}

[EditorOperation(
    "file.merge-song",
    "Merge Song",
    Scope = EditorOperationScope.File,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class MergeSongCommand
    : EditorOperationBase, IEditorCommand<MergeSongOptions, FileDocumentResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, MergeSongOptions options)
    {
        if (options.ImportedFile is null)
            return EditorCommandValidation.Failure("Choose a MIDI file to merge.");

        if (options.DelayMilliseconds < 0)
            return EditorCommandValidation.Failure("Choose a non-negative delay.");

        return EditorCommandValidation.Success;
    }

    public EditorCommandResult<FileDocumentResult> Execute(
        EditorCommandContext context,
        MergeSongOptions options)
    {
        foreach (var track in context.File.Tracks)
            track.FlushChanges();

        var merged = options.Sequential
            ? Merger.MergeSequentially(
                new[] { context.File.Source, options.ImportedFile },
                new SequentialMergingSettings
                {
                    DelayBetweenFiles = options.DelayMilliseconds > 0
                        ? new MetricTimeSpan(options.DelayMilliseconds * 1_000L)
                        : null,
                })
            : Merger.MergeSimultaneously(
                new[] { context.File.Source, options.ImportedFile },
                new SimultaneousMergingSettings
                {
                    IgnoreDifferentTempoMaps = options.IgnoreDifferentTempoMaps,
                });

        var result = context.Invoker.Execute(
            new ReplaceCurrentFileCommand(),
            new ReplaceCurrentFileOptions(
                merged,
                context.File.FilePath,
                IsDirty: false,
                MergeMultipleConductorTracks: true,
                ConsolidateTempoToConductorTrack: true,
                SanitizingSettings: FileDocumentCommandHelpers.MergeSongSanitizingSettings()));

        if (!result.Succeeded)
            return EditorCommandResult<FileDocumentResult>.NoChange(result.Message);

        var document = result.Result!.Value with { IsDirty = true };
        return EditorCommandResult<FileDocumentResult>.ChangedResult(
            document,
            refreshHints: result.Result.RefreshHints);
    }
}

internal static class FileDocumentCommandHelpers
{
    public static EditorRefreshHints DocumentReplacedHints
        => new(
            ReloadTrackList: true,
            ReloadSelectedTrack: true,
            ReloadEventList: true,
            ClearTrackSelection: true,
            ClearEventSelection: true,
            ClearSelectedTrack: true,
            RebuildPreview: true,
            RecalculateMetrics: true);

    public static SanitizingSettings MergeSongSanitizingSettings()
        => new()
        {
            RemoveDuplicatedSetTempoEvents = true,
            RemoveDuplicatedTimeSignatureEvents = true,
            RemoveDuplicatedNotes = false,
            RemoveEmptyTrackChunks = false,
            RemoveOrphanedNoteOffEvents = false,
            Trim = false,
        };

    public static bool ConsolidateTempoToConductorTrack(EditableMidiFile file)
    {
        file.FlushAllTracks();

        var conductor = file.Tracks.FirstOrDefault(track => track.IsConductorTrack);
        if (conductor is null)
        {
            var hasTempoEvents = file.Tracks.Any(track => track.Chunk.Events.OfType<SetTempoEvent>().Any()
                                                       || track.Chunk.Events.OfType<TimeSignatureEvent>().Any()
                                                       || track.Chunk.Events.OfType<KeySignatureEvent>().Any());
            if (!hasTempoEvents)
                return false;

            conductor = new EditableTrack(new TrackChunk(), 0);
            file.Tracks.Insert(0, conductor);
            ReindexTracks(file);
        }

        var movedEvents = 0;
        using var conductorManager = conductor.Chunk.ManageTimedEvents();

        foreach (var track in file.Tracks)
        {
            if (ReferenceEquals(track, conductor))
                continue;

            using var trackManager = track.Chunk.ManageTimedEvents();
            var tempoEvents = trackManager.Objects
                .Where(timedEvent => timedEvent.Event is SetTempoEvent or TimeSignatureEvent or KeySignatureEvent)
                .ToList();

            foreach (var timedEvent in tempoEvents)
            {
                trackManager.Objects.Remove(timedEvent);
                conductorManager.Objects.Add(timedEvent);
                movedEvents++;
            }
        }

        return movedEvents > 0;
    }

    public static bool MergeMultipleConductorTracks(EditableMidiFile file)
    {
        var conductorTracks = file.Tracks.Where(track => track.IsConductorTrack).ToList();
        if (conductorTracks.Count <= 1)
            return false;

        var primary = conductorTracks[0];
        primary.FlushChanges();

        using (var manager = primary.Chunk.ManageTimedEvents())
        {
            foreach (var extra in conductorTracks.Skip(1))
            {
                extra.FlushChanges();
                foreach (var timedEvent in extra.Chunk.GetTimedEvents())
                    manager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), timedEvent.Time));
            }
        }

        foreach (var extra in conductorTracks.Skip(1))
        {
            extra.Dispose();
            file.Tracks.Remove(extra);
        }

        ReindexTracks(file);
        return true;
    }

    public static bool ApplySanitize(EditableMidiFile file, SanitizingSettings settings)
    {
        file.FlushAllTracks();
        file.RebuildSourceChunksFromTracks();

        var before = Serialize(file.Source);
        Sanitizer.Sanitize(file.Source, settings);
        var after = Serialize(file.Source);

        if (before.SequenceEqual(after))
            return false;

        file.ReloadTracksFromSource();
        return true;
    }

    private static byte[] Serialize(MidiFile midiFile)
    {
        using var stream = new MemoryStream();
        midiFile.Write(stream);
        return stream.ToArray();
    }

    private static void ReindexTracks(EditableMidiFile file)
    {
        for (var i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
    }
}
