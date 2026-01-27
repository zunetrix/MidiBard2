using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.MidiPreprocessor;

namespace MidiBard.Control.MidiControl.PlaybackInstance;

internal sealed class BardPlayback : Playback
{
    private Plugin Plugin { get; }
    internal MidiFileConfig MidiFileConfig { get; set; }
    internal Playback MidiPlayback { get; set; }
    internal MidiFile MidiFile { get; init; }
    internal string FilePath { get; init; }
    internal TrackChunk[] TrackChunks { get; init; }
    internal TrackInfo[] TrackInfos { get; init; }
    internal string DisplayName { get; init; }
    private static long[] Cids = new long[100];
    public static MidiFileConfig ReloadMidiFileConfig(MidiFileConfig midiFileConfig) => MidiFileConfigManager.LoadDefaultPerformer(midiFileConfig, ref Cids);

    public BardPlayback(
        IEnumerable<TimedEvent> events,
        TempoMap tempoMap,
        Plugin plugin
    ) : base(events, tempoMap)
    {
        Plugin = plugin;
    }

    public BardPlayback GetBardPlayback(
        Plugin plugin,
        MidiFile file,
        string filePath
    )
    {
        PreparePlaybackData(
            file,
            out var tempoMap,
            out var trackChunks,
            out var trackInfos,
            out var timedEventWithMetadata
        );

        MidiFileConfig midiFileConfig = ResolveMidiConfig(filePath, trackInfos);

        return new BardPlayback(timedEventWithMetadata, tempoMap, plugin)
        {
            MidiFile = file,
            FilePath = filePath,
            TrackChunks = trackChunks,
            TrackInfos = trackInfos,
            MidiFileConfig = midiFileConfig,
            DisplayName = Path.GetFileNameWithoutExtension(filePath)
        };
    }

    private bool IsMidiTracksEqualJsonConfigFileTracks(MidiFileConfig midiFileConfig, TrackInfo[] trackInfos)
    {
        if (midiFileConfig == null)
            return false;

        // check track count
        if (midiFileConfig.Tracks.Count != trackInfos.Length)
            return false;

        // check track name
        for (int i = 0; i < trackInfos.Length; i++)
        {
            DbTrack dbTrack = midiFileConfig.Tracks[i];
            TrackInfo info = trackInfos[i];

            bool isSameTrackName = string.Equals(dbTrack.Name, info.TrackName, StringComparison.OrdinalIgnoreCase);
            bool isSameTrackIndex = dbTrack.Index == info.Index;

            if (!isSameTrackName || !isSameTrackIndex)
                return false;
        }

        return true;
    }

    private MidiFileConfig ResolveMidiConfig(string filePath, TrackInfo[] trackInfos)
    {
        // dont use midiFileConfi or Default Performer when not in a party
        var ignoreDefaultPerformer = DalamudApi.PartyList.IsInParty() && Plugin.Config.lockTracks;
        if (!DalamudApi.PartyList.IsInParty() || ignoreDefaultPerformer)
        {
            DalamudApi.PluginLog.Debug($"[LoadPlayback] using config TrackStatus");
            return null;
        }

        var midiConfigFromTrack = MidiFileConfigManager.GetMidiConfigFromTrack(trackInfos);

        // use midi specific json config
        var midiFileConfig = MidiFileConfigManager.GetMidiConfigFromFile(filePath);
        var isMidiTracksEqualJsonConfigFileTracks = IsMidiTracksEqualJsonConfigFileTracks(midiFileConfig, trackInfos);
        var useMidiJsonFileConfig = midiFileConfig is not null && isMidiTracksEqualJsonConfigFileTracks;
        if (useMidiJsonFileConfig)
        {
            DalamudApi.PluginLog.Debug($"[LoadPlayback] using json midi file config");
            return LoadMidiConfigFromJson(midiFileConfig);
        }

        // if (midiFileConfig is not null)
        // {
        //     var syncedMidiFileConfig = SyncMidiTracksWithJsonConfigFileTracks(filePath, midiFileConfig, trackInfos);
        //     if (syncedMidiFileConfig is not null)
        //     {
        //         DalamudApi.PluginLog.Debug($"[LoadPlayback] using json midi file config");
        //         return LoadMidiConfigFromJson(syncedMidiFileConfig);
        //     }
        // }

        // PMD
        if (Plugin.Config.playOnMultipleDevices)
        {
            if (Plugin.Config.usingFileSharingServices)
            {
                DalamudApi.PluginLog.Debug($"[LoadPlayback] using shared default performer");
                return LoadMidiConfigFromDefaultPerformer(midiConfigFromTrack);
            }

            DalamudApi.PluginLog.Debug($"[LoadPlayback] PMD using config TrackStatus");
            return LoadMidiConfigFromTrackStatus(midiConfigFromTrack);
        }

        // default performer
        var defaultPerformerTrackMapping = MidiFileConfigManager.defaultPerformer?.TrackMappingDict ?? new();
        var useDefaultPerformer = defaultPerformerTrackMapping.Count > 0;
        if (useDefaultPerformer)
        {
            DalamudApi.PluginLog.Debug($"[LoadPlayback] using default performer");
            return LoadMidiConfigFromDefaultPerformer(midiConfigFromTrack);
        }

        // if in a party but no default perform or midi json file use Plugin.Config.TrackStatus
        // for solo bards while in party or ensemble with PMD to not lose the assigned tracks
        DalamudApi.PluginLog.Debug($"[LoadPlayback] using config TrackStatus");
        return LoadMidiConfigFromTrackStatus(midiConfigFromTrack);
    }

    private MidiFileConfig LoadMidiConfigFromJson(MidiFileConfig midiFileConfig)
    {
        MidiFileConfigManager.UsingDefaultPerformer = false;
        for (int i = 0; i < midiFileConfig.Tracks.Count; i++)
            Cids[i] = MidiFileConfig.GetFirstCidInParty(midiFileConfig.Tracks[i]);

        return midiFileConfig;
    }

    private MidiFileConfig LoadMidiConfigFromTrackStatus(MidiFileConfig midiConfigFromTrack)
    {
        MidiFileConfigManager.UsingDefaultPerformer = false;
        Cids = new long[100];

        var bardCid = (long)DalamudApi.Player.ContentId;
        for (int i = 0; i < midiConfigFromTrack.Tracks.Count; i++)
        {
            if (Plugin.Config.TrackStatus[i].Enabled)
            {
                midiConfigFromTrack.Tracks[i].Enabled = true;
                midiConfigFromTrack.Tracks[i].AssignedCids.Add(bardCid);
                Cids[i] = bardCid;
            }
        }

        return midiConfigFromTrack;
    }

    private MidiFileConfig LoadMidiConfigFromDefaultPerformer(MidiFileConfig midiConfigFromTrack)
    {
        // PMD
        if (Plugin.Config.usingFileSharingServices)
            MidiFileConfigManager.LoadDefaultPerformer();

        return MidiFileConfigManager.LoadDefaultPerformer(midiConfigFromTrack, ref Cids);
    }

    private BardPlayback(IEnumerable<TimedEventWithMetadata> timedObjects, TempoMap tempoMap)
    : base(timedObjects, tempoMap, new PlaybackSettings { ClockSettings = new MidiClockSettings { CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator() } })
    {
    }

    protected override bool TryPlayEvent(MidiEvent midiEvent, object metadata)
    {
        // Place your logic here
        // Return true if event played (sent to plug-in); false otherwise
        Plugin.BardPlayDevice.SendEventWithMetadata(midiEvent, metadata);
        return true;
    }

    private void PreparePlaybackData(MidiFile file, out TempoMap tempoMap, out TrackChunk[] trackChunks, out TrackInfo[] trackInfos, out TimedEventWithMetadata[] timedEventWithMetadata)
    {
        if (Plugin.Config.AntiStackType != AntiStackType.Off)
            file = MidiPreprocessor.RemoveStackedNotes(file, Plugin.Config.AntiStackType);
        if (Plugin.Config.AlignMidi)
            file = MidiPreprocessor.RealignMidiFile(file, Plugin.Config.AlignMidiStartOffset);

        tempoMap = TryGetTempoMap(file);
        var map = tempoMap;
        trackChunks = MidiPreprocessor.ProcessTracks(GetNoteTracks(file).ToArray(), map);
        trackInfos = trackChunks.Select((chunk, index) => GetTrackInfos(chunk, index, map)).ToArray();
        timedEventWithMetadata = GetTimedEventWithMetadata(trackChunks).ToArray();
    }

    public float GetPlaybackProgress()
    {
        var currentTime = GetCurrentTime<MetricTimeSpan>();
        var duration = GetDuration<MetricTimeSpan>();
        float progress = Util.Extensions.SafeDivideMetricTimeSpan(currentTime, duration);
        return progress;
    }

    private TempoMap TryGetTempoMap(MidiFile midiFile)
    {
        try
        {
            return midiFile.GetTempoMap();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, "[LoadPlayback] error when getting file TempoMap, using default TempoMap instead.");
            return TempoMap.Default;
        }
    }

    private IEnumerable<TrackChunk> GetNoteTracks(MidiFile midifile)
    {
        try
        {
            return midifile.GetTrackChunks().Where(i => i.Events.Any(j => j is NoteOnEvent));
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, $"[LoadPlayback] error when parsing tracks, falling back to generated NoteEvent playback.");
            try
            {
                DalamudApi.PluginLog.Debug($"[LoadPlayback] file.Chunks.Count {midifile.Chunks.Count}");
                var trackChunks = midifile.GetTrackChunks().ToArray();
                DalamudApi.PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.Count {trackChunks.Length}");
                DalamudApi.PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.First {trackChunks.First()}");
                DalamudApi.PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.Events.Count {trackChunks.First().Events.Count}");
                DalamudApi.PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.Events.OfType<NoteEvent>.Count {trackChunks.First().Events.OfType<NoteEvent>().Count()}");

                return trackChunks.Where(i => i.Events.Any(j => j is NoteOnEvent))
                    .Select((i) =>
                    {
                        var noteEvents = i.Events.Where(midiEvent => midiEvent is NoteEvent or ProgramChangeEvent or TextEvent);
                        return new TrackChunk(noteEvents);
                    });
            }
            catch (Exception exception2)
            {
                DalamudApi.PluginLog.Error(exception2, "[LoadPlayback] still errors? check your file");
                throw;
            }
        }
    }

    private TrackInfo GetTrackInfos(TrackChunk i, int index, TempoMap tempoMap)
    {
        var notes = i.GetNotes();
        var eventsCollection = i.Events;
        var TrackNameEventsText = eventsCollection.OfType<SequenceTrackNameEvent>().Select(j => j.Text.Replace("\0", string.Empty).Trim()).Distinct().ToArray();
        var TrackName = TrackNameEventsText.FirstOrDefault() ?? "Untitled";
        var IsProgramControlled = Regex.IsMatch(TrackName, @"^Program:.+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var timedNoteOffEvent = notes.LastOrDefault()?.GetTimedNoteOffEvent();

        return new TrackInfo
        {
            //TextEventsText = eventsCollection.OfType<TextEvent>().Select(j => j.Text.Replace("\0", string.Empty).Trim()).Distinct().ToArray(),
            ProgramChangeEventsText = eventsCollection.OfType<ProgramChangeEvent>().Select(j => $"channel {j.Channel}, {j.GetGMProgramName()}").Distinct().ToArray(),
            TrackNameEventsText = TrackNameEventsText,
            HighestNote = notes.MaxElement(j => (int)j.NoteNumber),
            LowestNote = notes.MinElement(j => (int)j.NoteNumber),
            NoteCount = notes.Count,
            DurationMetric = timedNoteOffEvent?.TimeAs<MetricTimeSpan>(tempoMap) ?? new MetricTimeSpan(),
            DurationMidi = timedNoteOffEvent?.Time ?? 0,
            TrackName = TrackName,
            IsProgramControlled = IsProgramControlled,
            Index = index,
            IsProgramElectricGuitar = TrackName.ToLower().Replace(":", "").StartsWith("programelectricguitar")
            //Channels = i.Events.OfType<ProgramChangeEvent>().Select(j => j.Channel).Distinct().Union(notes.Select(note => note.Channel).Distinct()).ToArray()
        };
    }

    private readonly Dictionary<long, Dictionary<SevenBitNumber, int>> timeEventsDictionary =
        new Dictionary<long, Dictionary<SevenBitNumber, int>>();

    internal static List<Dictionary<long, Dictionary<int, int>>> TrimDict;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private IEnumerable<TimedEventWithMetadata> GetTimedEventWithMetadata(IEnumerable<TrackChunk> tracks)
    {
        var timedEvents = tracks
            .SelectMany((track, index) => track.GetTimedEvents()
                    .Where(i => i.Event.EventType is not MidiEventType.ControlChange and not MidiEventType.PitchBend and not MidiEventType.UnknownMeta)
                    .Select(timedEvent => new TimedEventWithMetadata(timedEvent.Event, timedEvent.Time, GetMetadataForEvent(timedEvent.Event, timedEvent.Time, index))))
            .OrderBy(e => e.Time)
            .ThenBy(i => ((BardPlayDevice.MidiPlaybackMetaData)i.Metadata).EventValue);
        return timedEvents;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    BardPlayDevice.MidiPlaybackMetaData GetMetadataForEvent(MidiEvent midiEvent, long time, int trackIndex)
    {
        var compareValue = midiEvent switch
        {
            //order chords so they always play from low to high
            NoteEvent noteEvent => noteEvent.NoteNumber,
            //order program change events so they always get processed before notes
            ProgramChangeEvent => -2,
            //keep other unimportant events order
            _ => -1
        };
        return new BardPlayDevice.MidiPlaybackMetaData(trackIndex, time, compareValue);
    }

    public uint GetInstrumentId()
    {
        // find instrument from config file
        uint? configInstrumentId = MidiFileConfig?.Tracks?
            .FirstOrDefault(t => t.Enabled && MidiFileConfig.IsCidOnTrack((long)DalamudApi.Player.ContentId, t))
            ?.Instrument;

        // find instrument from first enabled track
        uint? trackInstrumentId = TrackInfos?
            .FirstOrDefault(i => i.IsEnabled)
            ?.InstrumentIDFromTrackName;

        uint defaultInstrumentId = 0;
        return (configInstrumentId ?? trackInstrumentId) ?? defaultInstrumentId;
    }

    internal void ApplyTransposeToTracks()
    {
        foreach (var trackInfo in TrackInfos)
        {
            var transposePerTrack = trackInfo.TransposeFromTrackName;
            if (transposePerTrack != 0)
            {
                DalamudApi.PluginLog.Information($"applying transpose {transposePerTrack:+#;-#;0} for track [{trackInfo.Index + 1}] {trackInfo.TrackName}");
            }

            Plugin.Config.TrackStatus[trackInfo.Index].Transpose = transposePerTrack;
        }

        Plugin.Config.TransposeGlobal = 0;
    }

    internal void UpdateGuitarToneByConfig()
    {
        var playback = Plugin.CurrentBardPlayback;
        if (playback == null) return;

        foreach (var (trackInfo, index) in playback.TrackInfos.Select((info, i) => (info, i)))
        {
            var trackInstrumentId = trackInfo.InstrumentIDFromTrackName;
            if (trackInstrumentId is uint instrumentId && Plugin.Instruments[instrumentId].IsGuitar)
            {
                Plugin.Config.TrackStatus[index].Tone = Plugin.Instruments[instrumentId].GuitarTone;
            }
        }
    }

    internal void SyncTrackStatusWithMidiFileConfig()
    {
        if (MidiFileConfig == null)
            return;

        // if (MidiFileConfig == null || MidiFileConfigManager.UsingDefaultPerformer)
        //     return;

        var tracks = MidiFileConfig.Tracks;
        Plugin.Config.ResetTrackStatus();
        DalamudApi.PluginLog.Debug($"[LoadPlayback] SyncTrackStatusWithMidiFileConfig");
        for (var trackIndex = 0; trackIndex < MidiFileConfig.Tracks.Count; trackIndex++)
        {
            try
            {
                var isBardAssignedToTrack = MidiFileConfig.GetFirstCidInParty(tracks[trackIndex]) == (long)DalamudApi.Player.ContentId;
                Plugin.Config.TrackStatus[trackIndex].Enabled = tracks[trackIndex].Enabled && isBardAssignedToTrack;
                Plugin.Config.TrackStatus[trackIndex].Transpose = tracks[trackIndex].Transpose;
                Plugin.Config.TrackStatus[trackIndex].Tone = InstrumentHelper.GetGuitarTone(tracks[trackIndex].Instrument);
                Plugin.Config.SoloedTrack = null;
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, $"error when updating track {trackIndex}");
            }
        }
    }
}
