using System;
using System.Collections.Generic;
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

            RemoveDuplicatedControlChangeEvents = true,
            RemoveDuplicatedSequenceTrackNameEvents = true,
            RemoveDuplicatedPitchBendEvents = true,
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
            conductor.MarkAsConductorTrack();
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
                .Where(timedEvent => timedEvent.Event is SetTempoEvent or TimeSignatureEvent or KeySignatureEvent or PortPrefixEvent)
                .ToList();

            foreach (var timedEvent in tempoEvents)
            {
                trackManager.Objects.Remove(timedEvent);
                conductorManager.Objects.Add(timedEvent);
                movedEvents++;
            }
        }

        if (movedEvents > 0)
            CleanConductorTrack(conductorManager);

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

            CleanConductorTrack(manager);
        }

        foreach (var extra in conductorTracks.Skip(1))
        {
            extra.Dispose();
            file.Tracks.Remove(extra);
        }

        ReindexTracks(file);
        return true;
    }

    private static void CleanConductorTrack(TimedObjectsManager<TimedEvent> manager)
    {
        var trackNames = manager.Objects.Where(te => te.Event is SequenceTrackNameEvent).ToList();
        foreach (var te in trackNames)
            manager.Objects.Remove(te);

        var keySignatures = manager.Objects.Where(te => te.Event is KeySignatureEvent).ToList();
        var keysToKeep = keySignatures
            .GroupBy(te => new { te.Time, ((KeySignatureEvent)te.Event).Key, ((KeySignatureEvent)te.Event).Scale })
            .Select(g => g.First())
            .ToHashSet();
        foreach (var te in keySignatures.Where(te => !keysToKeep.Contains(te)))
            manager.Objects.Remove(te);

        var portPrefixes = manager.Objects.Where(te => te.Event is PortPrefixEvent).ToList();
        var portsToKeep = portPrefixes
            .GroupBy(te => new { te.Time, ((PortPrefixEvent)te.Event).Port })
            .Select(g => g.First())
            .ToHashSet();
        foreach (var te in portPrefixes.Where(te => !portsToKeep.Contains(te)))
            manager.Objects.Remove(te);
    }

    public static bool TrimSilenceAtStart(EditableMidiFile file)
    {
        file.FlushAllTracks();

        var trimTick = long.MaxValue;
        foreach (var track in file.Tracks)
        {
            if (track.IsConductorTrack) continue;
            var firstNote = track.Chunk.GetNotes().FirstOrDefault();
            if (firstNote != null && firstNote.Time < trimTick)
                trimTick = firstNote.Time;
        }

        if (trimTick <= 0 || trimTick == long.MaxValue)
            return false;

        foreach (var track in file.Tracks)
        {
            // Snapshot events with their absolute times.
            var snapshot = track.Chunk.GetTimedEvents()
                .Select(te => new { te, time = te.Time })
                .ToList();

            var newEvents = new List<TimedEvent>();

            if (track.IsConductorTrack)
            {
                // Collect conductor-type events at/before trimTick so we can
                // keep only the last occurrence of each type at the new tick 0.
                var conductorCandidates = new List<(long time, MidiEvent ev)>();
                foreach (var item in snapshot)
                {
                    if (item.time <= trimTick)
                    {
                        if (item.te.Event is SetTempoEvent
                            or TimeSignatureEvent
                            or KeySignatureEvent)
                        {
                            conductorCandidates.Add((item.time, item.te.Event));
                            continue;
                        }
                    }
                    newEvents.Add(new TimedEvent(
                        item.te.Event.Clone(), Math.Max(0, item.time - trimTick)));
                }

                // Of the candidates, keep only the last one per type at tick 0.
                var keptTypes = new HashSet<Type>();
                foreach (var candidate in conductorCandidates
                    .OrderByDescending(c => c.time))
                {
                    if (keptTypes.Add(candidate.ev.GetType()))
                        newEvents.Add(new TimedEvent(candidate.ev.Clone(), 0));
                }
            }
            else
            {
                foreach (var item in snapshot)
                {
                    newEvents.Add(new TimedEvent(
                        item.te.Event.Clone(), Math.Max(0, item.time - trimTick)));
                }
            }

            // Replace the chunk's raw events with the new delta-time sequence.
            newEvents = newEvents.OrderBy(te => te.Time).ToList();
            long runningTick = 0;
            track.Chunk.Events.Clear();
            foreach (var te in newEvents)
            {
                long delta = te.Time - runningTick;
                te.Event.DeltaTime = delta;
                track.Chunk.Events.Add(te.Event);
                runningTick = te.Time;
            }
        }

        file.FlushAllTracks();
        file.RebuildSourceChunksFromTracks();
        file.ReloadTracksFromSource();
        return true;
    }

    public static bool ApplySanitize(EditableMidiFile file, SanitizingSettings settings)
    {
        file.FlushAllTracks();
        file.RebuildSourceChunksFromTracks();

        // Custom trim silence at start - handles conductor-aware trimming that
        // DryWetMidi's Sanitizer.Trim does not support (it only looks at the
        // first raw delta time per chunk, missing gaps behind initial meta events).
        bool trimChanged = false;
        if (settings.Trim)
        {
            trimChanged = TrimSilenceAtStart(file);
            if (trimChanged)
            {
                file.FlushAllTracks();
                file.RebuildSourceChunksFromTracks();
            }
        }

        // Identify conductor chunks before the sanitizer so we can protect them
        // from being removed or emptied.
        var conductorChunks = file.Tracks
            .Where(t => t.IsConductorTrack)
            .Select(t => t.Chunk)
            .ToHashSet();

        // Back up conductor event data for restoration after sanitizer.
        var conductorBackup = conductorChunks
            .ToDictionary(c => c, c => c.Events.ToList());

        var beforeCounts = file.Source.GetTrackChunks().Select(c => c.Events.Count).ToList();
        var beforeFirstTick = file.Source.GetTrackChunks()
            .Select(c => c.Events.FirstOrDefault()?.DeltaTime ?? -1)
            .ToList();

        // DryWetMidi sanitizer - Trim is handled by our custom method above.
        // Use the user's RemoveEmptyTrackChunks setting; conductor chunks that get
        // removed or emptied are handled by the protection code below.
        Sanitizer.Sanitize(file.Source, new SanitizingSettings
        {
            RemoveDuplicatedNotes = settings.RemoveDuplicatedNotes,
            RemoveEmptyTrackChunks = settings.RemoveEmptyTrackChunks,
            RemoveOrphanedNoteOffEvents = settings.RemoveOrphanedNoteOffEvents,
            OrphanedNoteOnEventsPolicy = settings.OrphanedNoteOnEventsPolicy,
            RemoveDuplicatedSetTempoEvents = settings.RemoveDuplicatedSetTempoEvents,
            RemoveDuplicatedTimeSignatureEvents = settings.RemoveDuplicatedTimeSignatureEvents,
            RemoveDuplicatedControlChangeEvents = settings.RemoveDuplicatedControlChangeEvents,
            RemoveDuplicatedSequenceTrackNameEvents = settings.RemoveDuplicatedSequenceTrackNameEvents,
            RemoveDuplicatedPitchBendEvents = settings.RemoveDuplicatedPitchBendEvents,
            Trim = false,
        });

        // Re-insert any conductor chunk that was removed by the sanitizer but
        // that we still hold a reference to. This is needed because the sanitizer
        // may remove a chunk that had events but became empty after dedup.
        foreach (var chunk in conductorChunks)
        {
            if (!file.Source.GetTrackChunks().Any(c => ReferenceEquals(c, chunk)))
                file.Source.Chunks.Add(chunk);
        }

        // Restore conductor events. If the sanitizer emptied a conductor chunk
        // (e.g. removed all 120-BPM tempos as "default"), reseed it with one
        // event per conductor type so the track stays detectable as a conductor.
        foreach (var (chunk, savedEvents) in conductorBackup)
        {
            if (savedEvents.Count == 0)
                continue;

            if (chunk.Events.OfType<SetTempoEvent>().Any()
                || chunk.Events.OfType<TimeSignatureEvent>().Any()
                || chunk.Events.OfType<KeySignatureEvent>().Any())
                continue;

            var restored = new HashSet<Type>();
            long lastDelta = 0;
            foreach (var ev in savedEvents)
            {
                long delta = ev.DeltaTime;
                var isConductorEvent = ev is SetTempoEvent or TimeSignatureEvent or KeySignatureEvent;
                if (isConductorEvent && restored.Add(ev.GetType()))
                {
                    var clone = ev.Clone();
                    clone.DeltaTime = lastDelta;
                    chunk.Events.Add(clone);
                }
                lastDelta = delta;
            }
        }

        var afterCounts = file.Source.GetTrackChunks().Select(c => c.Events.Count).ToList();
        var afterFirstTick = file.Source.GetTrackChunks()
            .Select(c => c.Events.FirstOrDefault()?.DeltaTime ?? -1)
            .ToList();

        var sanitizeChanged = !beforeCounts.SequenceEqual(afterCounts)
                           || !beforeFirstTick.SequenceEqual(afterFirstTick);

        if (!trimChanged && !sanitizeChanged)
            return false;

        file.ReloadTracksFromSource();
        file.MarkChanged();

        // After reload, re-mark conductor tracks whose chunks match the
        // backed-up conductor chunk references. This is needed for conductor
        // tracks that were empty at rest (explicitly marked via MarkAsConductorTrack)
        // and therefore cannot be auto-detected by IsConductorChunk.
        foreach (var track in file.Tracks)
        {
            if (!track.IsConductorTrack && conductorChunks.Contains(track.Chunk))
                track.MarkAsConductorTrack();
        }

        return true;
    }

    private static void ReindexTracks(EditableMidiFile file)
    {
        for (var i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
    }
}
