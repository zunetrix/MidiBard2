using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.DryWetMidi;

namespace MidiBard.Control.MidiControl.Editing;

public enum MidiForgeTrimStartMode
{
    Off,
    UntilFirstNote,
    EmptyBars,
}

public sealed record MidiForgeImportOptions(
    bool SplitTracksByChannel = false,
    bool SortTracks = false,
    bool OverwriteTrackNames = false,
    bool RemoveMetadata = false,
    bool RemoveSequencerSpecificEvents = false,
    bool OptimizeChannels = false,
    MidiForgeTrimStartMode TrimStartMode = MidiForgeTrimStartMode.Off);

public sealed record MidiForgeImportResult(
    MidiFile MidiFile,
    int RemovedEmptyTracks,
    int RemovedMetadataEvents,
    int RemovedSequencerSpecificEvents,
    int SplitSourceTracks,
    int CreatedSplitTracks,
    int RenamedTracks,
    int OptimizedTracks,
    long TrimmedTicks);

public static class MidiForgeImporter
{
    private static readonly Regex PriorityTrackNameRegex = new("(melody|vocal|voice)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s\s+", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new("[^0-9]", RegexOptions.Compiled);

    public static MidiForgeImportResult Normalize(MidiFile source, MidiForgeImportOptions options)
    {
        var midi = CloneMidiFile(source);

        var removedEmptyTracks = RemoveEmptyNonConductorTracks(midi);
        var removedMetadataEvents = options.RemoveMetadata ? RemoveMetadataEvents(midi) : 0;
        var removedSpecificEvents = options.RemoveSequencerSpecificEvents ? RemoveSequencerSpecificEvents(midi) : 0;
        var splitResult = options.SplitTracksByChannel
            ? SplitTracksByChannel(midi)
            : (SourceTracks: 0, CreatedTracks: 0);
        var renamedTracks = NormalizeTrackNames(midi, options.OverwriteTrackNames);

        if (options.SortTracks)
            SortTracks(midi);

        var optimizedTracks = options.OptimizeChannels ? OptimizeTrackChannels(midi) : 0;
        var trimmedTicks = TrimStartTime(midi, options.TrimStartMode);

        return new MidiForgeImportResult(
            midi,
            removedEmptyTracks,
            removedMetadataEvents,
            removedSpecificEvents,
            splitResult.SourceTracks,
            splitResult.CreatedTracks,
            renamedTracks,
            optimizedTracks,
            trimmedTicks);
    }

    private static MidiFile CloneMidiFile(MidiFile source)
    {
        var clone = new MidiFile
        {
            TimeDivision = source.TimeDivision,
        };

        foreach (var chunk in source.Chunks)
            clone.Chunks.Add(chunk.Clone());

        return clone;
    }

    private static int RemoveEmptyNonConductorTracks(MidiFile midi)
    {
        var toRemove = midi.GetTrackChunks()
            .Where(chunk => !chunk.GetNotes().Any() && !IsConductorTrack(chunk))
            .ToArray();

        foreach (var chunk in toRemove)
            midi.Chunks.Remove(chunk);

        return toRemove.Length;
    }

    private static int RemoveMetadataEvents(MidiFile midi)
        => RemoveEvents(midi, IsImportMetadataEvent);

    private static int RemoveSequencerSpecificEvents(MidiFile midi)
        => RemoveEvents(midi, midiEvent => midiEvent is SequencerSpecificEvent);

    private static int RemoveEvents(MidiFile midi, Func<MidiEvent, bool> predicate)
    {
        var removed = 0;
        foreach (var chunk in midi.GetTrackChunks())
        {
            removed += chunk.Events.Count(predicate);
            chunk.Events.RemoveAll(e => predicate(e));
        }

        return removed;
    }

    private static bool IsImportMetadataEvent(MidiEvent midiEvent)
        => midiEvent is TextEvent
            or CopyrightNoticeEvent
            or MarkerEvent
            or CuePointEvent
            or DeviceNameEvent
            or SequenceNumberEvent;

    private static (int SourceTracks, int CreatedTracks) SplitTracksByChannel(MidiFile midi)
    {
        var chunks = midi.Chunks.ToList();
        var outputChunks = new List<MidiChunk>(chunks.Count);
        var sourceTracks = 0;
        var createdTracks = 0;

        foreach (var chunk in chunks)
        {
            if (chunk is not TrackChunk trackChunk)
            {
                outputChunks.Add(chunk);
                continue;
            }

            using var manager = trackChunk.ManageTimedEvents();
            var timedEvents = manager.Objects.ToArray();
            var channelGroups = timedEvents
                .Where(te => te.Event is ChannelEvent)
                .GroupBy(te => (byte)((ChannelEvent)te.Event).Channel)
                .OrderBy(g => g.Key)
                .ToArray();

            if (channelGroups.Length <= 1)
            {
                outputChunks.Add(trackChunk);
                continue;
            }

            sourceTracks++;

            var trackName = GetTrackName(trackChunk);
            var nonNameMetadata = timedEvents
                .Where(te => te.Event is not ChannelEvent and not SequenceTrackNameEvent)
                .ToArray();

            if (nonNameMetadata.Length > 0)
                outputChunks.Add(CreateTrackChunk(nonNameMetadata));

            foreach (var group in channelGroups)
            {
                var splitTimedEvents = new List<TimedEvent>();
                if (!string.IsNullOrWhiteSpace(trackName))
                    splitTimedEvents.Add(new TimedEvent(new SequenceTrackNameEvent($"{trackName} Ch {group.Key + 1}"), 0));

                splitTimedEvents.AddRange(group.Select(te => new TimedEvent(te.Event.Clone(), te.Time)));
                outputChunks.Add(CreateTrackChunk(splitTimedEvents));
                createdTracks++;
            }
        }

        midi.Chunks.Clear();
        foreach (var chunk in outputChunks)
            midi.Chunks.Add(chunk);

        return (sourceTracks, createdTracks);
    }

    private static TrackChunk CreateTrackChunk(IEnumerable<TimedEvent> timedEvents)
    {
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in timedEvents)
            manager.Objects.Add(new TimedEvent(timedEvent.Event.Clone(), Math.Max(0, timedEvent.Time)));
        return chunk;
    }

    private static int NormalizeTrackNames(MidiFile midi, bool overwriteTrackNames)
    {
        var renamed = 0;
        var trackNumber = 1;

        foreach (var chunk in midi.GetTrackChunks().Where(chunk => chunk.GetNotes().Any()))
        {
            var originalName = GetTrackName(chunk);
            var normalizedName = MultiWhitespaceRegex.Replace(originalName.Trim(), " ");
            var shouldReplace = overwriteTrackNames || string.IsNullOrWhiteSpace(normalizedName);
            var finalName = shouldReplace ? GetDefaultTrackName(chunk, trackNumber) : normalizedName;

            if (finalName != originalName)
            {
                SetTrackName(chunk, finalName);
                renamed++;
            }

            trackNumber++;
        }

        return renamed;
    }

    private static void SortTracks(MidiFile midi)
    {
        var chunks = midi.Chunks.ToList();
        var conductorChunks = chunks.OfType<TrackChunk>().Where(IsConductorTrack).Cast<MidiChunk>().ToList();
        var nonTrackChunks = chunks.Where(chunk => chunk is not TrackChunk).ToList();
        var performanceTracks = chunks
            .OfType<TrackChunk>()
            .Where(chunk => !IsConductorTrack(chunk))
            .Select((chunk, index) => new
            {
                Chunk = chunk,
                OriginalIndex = index,
                Name = GetTrackName(chunk),
                IsPriority = PriorityTrackNameRegex.IsMatch(GetTrackName(chunk)),
                PriorityNumber = GetPriorityTrackNumber(GetTrackName(chunk)),
                IsDrum = IsDrumTrack(chunk),
            })
            .OrderByDescending(track => track.IsPriority)
            .ThenBy(track => track.IsPriority ? track.PriorityNumber : int.MaxValue)
            .ThenBy(track => track.IsDrum)
            .ThenBy(track => track.OriginalIndex)
            .Select(track => (MidiChunk)track.Chunk)
            .ToList();

        midi.Chunks.Clear();
        foreach (var chunk in nonTrackChunks.Concat(conductorChunks).Concat(performanceTracks))
            midi.Chunks.Add(chunk);
    }

    private static int OptimizeTrackChannels(MidiFile midi)
    {
        const int maxChannel = 15;
        var usedPrograms = new Dictionary<int, byte>();
        var nextNormalChannel = 0;
        var changedTracks = 0;

        foreach (var chunk in midi.GetTrackChunks().Where(chunk => chunk.GetNotes().Any()))
        {
            if (IsDrumTrack(chunk))
                continue;

            var program = GetFirstProgramNumber(chunk);
            byte outputChannel;

            if (program.HasValue && usedPrograms.TryGetValue(program.Value, out var existingChannel))
            {
                outputChannel = existingChannel;
            }
            else
            {
                outputChannel = (byte)nextNormalChannel;
                if (program.HasValue)
                    usedPrograms[program.Value] = outputChannel;

                nextNormalChannel = nextNormalChannel + 1 == MidiForgeAnalysis.DrumChannel
                    ? nextNormalChannel + 2
                    : nextNormalChannel + 1;

                if (nextNormalChannel > maxChannel)
                    nextNormalChannel = 0;
            }

            if (SetTrackChannel(chunk, outputChannel))
                changedTracks++;
        }

        return changedTracks;
    }

    private static long TrimStartTime(MidiFile midi, MidiForgeTrimStartMode mode)
    {
        if (mode == MidiForgeTrimStartMode.Off)
            return 0;

        var firstNoteTick = midi.GetTrackChunks()
            .SelectMany(chunk => chunk.GetNotes())
            .Select(note => note.Time)
            .DefaultIfEmpty(-1)
            .Min();

        if (firstNoteTick <= 0)
            return 0;

        var tempoMap = midi.GetTempoMap();
        var ticksToRemove = mode switch
        {
            MidiForgeTrimStartMode.UntilFirstNote => firstNoteTick,
            MidiForgeTrimStartMode.EmptyBars => GetFirstBarStartTick(firstNoteTick, tempoMap),
            _ => 0,
        };

        if (ticksToRemove <= 0)
            return 0;

        foreach (var chunk in midi.GetTrackChunks())
        {
            using var manager = chunk.ManageTimedEvents();
            foreach (var timedEvent in manager.Objects)
                timedEvent.Time = Math.Max(0, timedEvent.Time - ticksToRemove);
        }

        return ticksToRemove;
    }

    private static long GetFirstBarStartTick(long tick, TempoMap tempoMap)
    {
        var barBeat = TimeConverter.ConvertTo<BarBeatTicksTimeSpan>(tick, tempoMap);
        if (barBeat.Bars <= 0)
            return 0;

        return TimeConverter.ConvertFrom(new BarBeatTicksTimeSpan(barBeat.Bars, 0), tempoMap);
    }

    private static string GetDefaultTrackName(TrackChunk chunk, int trackNumber)
    {
        if (IsDrumTrack(chunk))
            return "Drumkit";

        var program = GetFirstProgramNumber(chunk);
        if (program.HasValue)
        {
            var programName = DryWetMidiExtensions.GetGMProgramName((byte)program.Value);
            if (!string.IsNullOrWhiteSpace(programName))
                return programName;
        }

        return $"Track {trackNumber:00}";
    }

    private static int? GetFirstProgramNumber(TrackChunk chunk)
        => chunk.Events.OfType<ProgramChangeEvent>()
            .Select(program => (int)(byte)program.ProgramNumber)
            .FirstOrDefault(-1) is var program && program >= 0
                ? program
                : null;

    private static int GetPriorityTrackNumber(string name)
    {
        var numberText = NumberRegex.Replace(name, string.Empty);
        return int.TryParse(numberText, out var number) ? number : 999;
    }

    private static bool SetTrackChannel(TrackChunk chunk, byte channel)
    {
        var changed = false;
        var channelNumber = (FourBitNumber)channel;

        foreach (var midiEvent in chunk.Events.OfType<ChannelEvent>())
        {
            if ((byte)midiEvent.Channel == channel)
                continue;

            midiEvent.Channel = channelNumber;
            changed = true;
        }

        return changed;
    }

    private static string GetTrackName(TrackChunk chunk)
        => chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text ?? string.Empty;

    private static void SetTrackName(TrackChunk chunk, string name)
    {
        chunk.Events.RemoveAll(e => e is SequenceTrackNameEvent);
        if (!string.IsNullOrWhiteSpace(name))
            chunk.Events.Insert(0, new SequenceTrackNameEvent(name));
    }

    private static bool IsConductorTrack(TrackChunk chunk)
        => chunk.Events.Count > 0
            && !chunk.Events.OfType<ChannelEvent>().Any()
            && (chunk.Events.OfType<SetTempoEvent>().Any()
                || chunk.Events.OfType<TimeSignatureEvent>().Any()
                || chunk.Events.OfType<KeySignatureEvent>().Any());

    private static bool IsDrumTrack(TrackChunk chunk)
        => chunk.Events.OfType<ChannelEvent>().Any(e => (byte)e.Channel == MidiForgeAnalysis.DrumChannel);
}
