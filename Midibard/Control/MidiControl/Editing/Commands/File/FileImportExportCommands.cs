using System.Collections.Generic;
using System.IO;
using System.Text;

using Melanchall.DryWetMidi.Core;

namespace MidiBard.Control.MidiControl.Editing.Commands.File;

public sealed record OpenNormalizedMidiFileOptions(
    MidiFile MidiFile,
    string FilePath,
    MidiForgeImportOptions ImportOptions);

public sealed record OpenNormalizedMidiFileResult(
    FileDocumentResult Document,
    MidiForgeImportResult ImportResult,
    string Summary);

public sealed record ExportLrcMetadataOptions(
    string FilePath,
    string SongTitle);

public sealed record ExportLrcMetadataResult(
    string FilePath,
    int LineCount,
    bool HasLyrics);

[EditorOperation(
    "file.open-normalized",
    "Open MIDI File With Options",
    Scope = EditorOperationScope.File,
    RequiresFile = false,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class OpenNormalizedMidiFileCommand
    : EditorOperationBase, IEditorCommand<OpenNormalizedMidiFileOptions, OpenNormalizedMidiFileResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, OpenNormalizedMidiFileOptions options)
        => options.MidiFile is null
            ? EditorCommandValidation.Failure("Choose a MIDI file to open.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<OpenNormalizedMidiFileResult> Execute(
        EditorCommandContext context,
        OpenNormalizedMidiFileOptions options)
    {
        var importResult = MidiForgeImporter.Normalize(options.MidiFile, options.ImportOptions);
        var openResult = context.Invoker.Execute(
            new OpenLoadedMidiFileCommand(),
            new OpenLoadedMidiFileOptions(
                importResult.MidiFile,
                options.FilePath,
                ImportResultHasChanges(importResult)));

        if (!openResult.Succeeded)
            return EditorCommandResult<OpenNormalizedMidiFileResult>.NoChange(openResult.Message);

        return EditorCommandResult<OpenNormalizedMidiFileResult>.UnchangedResult(
            new OpenNormalizedMidiFileResult(
                openResult.Result!.Value,
                importResult,
                BuildImportSummary(importResult)),
            refreshHints: openResult.Result.RefreshHints);
    }

    public static bool ImportResultHasChanges(MidiForgeImportResult result)
        => result.RemovedEmptyTracks > 0
           || result.RemovedNonLyricMetadataEvents > 0
           || result.RemovedLyricTextEvents > 0
           || result.RemovedSequencerSpecificEvents > 0
           || result.SplitSourceTracks > 0
           || result.CreatedSplitTracks > 0
           || result.RenamedTracks > 0
           || result.OptimizedTracks > 0
           || result.TrimmedTicks > 0;

    public static string BuildImportSummary(MidiForgeImportResult result)
    {
        var changes = new List<string>();
        if (result.RemovedEmptyTracks > 0)
            changes.Add($"removed {result.RemovedEmptyTracks} empty track(s)");
        if (result.RemovedNonLyricMetadataEvents > 0)
            changes.Add($"removed {result.RemovedNonLyricMetadataEvents} non-lyric metadata event(s)");
        if (result.RemovedLyricTextEvents > 0)
            changes.Add($"removed {result.RemovedLyricTextEvents} lyric/text event(s)");
        if (result.RemovedSequencerSpecificEvents > 0)
            changes.Add($"removed {result.RemovedSequencerSpecificEvents} sequencer event(s)");
        if (result.CreatedSplitTracks > 0)
            changes.Add($"split {result.SplitSourceTracks} source track(s) into {result.CreatedSplitTracks} channel track(s)");
        if (result.RenamedTracks > 0)
            changes.Add($"renamed {result.RenamedTracks} track(s)");
        if (result.OptimizedTracks > 0)
            changes.Add($"optimized {result.OptimizedTracks} track channel(s)");
        if (result.TrimmedTicks > 0)
            changes.Add($"trimmed {result.TrimmedTicks} tick(s)");

        return changes.Count == 0
            ? "Opened MIDI with import options; no normalization changes were needed."
            : $"Opened MIDI with import options: {string.Join(", ", changes)}.";
    }
}

[EditorOperation(
    "file.export-lrc-metadata",
    "Export LRC From MIDI Metadata",
    Scope = EditorOperationScope.File,
    HistoryPolicy = HistoryPolicy.None)]
public sealed class ExportLrcMetadataCommand
    : EditorOperationBase, IEditorCommand<ExportLrcMetadataOptions, ExportLrcMetadataResult>
{
    public EditorCommandValidation Validate(EditorCommandContext context, ExportLrcMetadataOptions options)
        => string.IsNullOrWhiteSpace(options.FilePath)
            ? EditorCommandValidation.Failure("Choose an LRC export path.")
            : EditorCommandValidation.Success;

    public EditorCommandResult<ExportLrcMetadataResult> Execute(
        EditorCommandContext context,
        ExportLrcMetadataOptions options)
    {
        context.File.FlushAllTracks();
        context.File.RebuildSourceChunksFromTracks();

        var exportResult = MidiForgeLyricsExporter.Export(context.File.Source, options.SongTitle);
        System.IO.File.WriteAllText(options.FilePath, exportResult.Content, Encoding.UTF8);

        return EditorCommandResult<ExportLrcMetadataResult>.UnchangedResult(
            new ExportLrcMetadataResult(
                options.FilePath,
                exportResult.Lines.Count,
                exportResult.HasLyrics));
    }
}
