using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Extensions.Enumerable;
using MidiBard.Extensions.General;
using MidiBard.Extensions.Time;
using MidiBard.Managers;
using MidiBard.Util;
using MidiBard.Util.MidiPreprocessor;

namespace MidiBard.Control.MidiControl.PlaybackInstance;

internal sealed class BardPlayback : IDisposable
{
    private readonly Plugin Plugin;
    private Playback _playback;
    internal MidiFileConfig MidiFileConfig { get; set; }
    internal MidiFile MidiFile { get; init; }
    internal string FilePath { get; init; }
    internal TrackChunk[] TrackChunks { get; init; }
    internal TrackInfo[] TrackInfos { get; init; }
    internal string DisplayName { get; init; }
    private static long[] Cids = new long[100];
    public MidiFileConfig ReloadMidiFileConfig(MidiFileConfig midiFileConfig) => Plugin.MidiFileConfigManager.LoadDefaultPerformer(midiFileConfig, ref Cids);

    public BardPlayback(Plugin plugin)
    {
        Plugin = plugin;
    }

    public void Dispose()
    {
        try
        {
            _playback?.Dispose();
            _playback = null;
        }
        catch
        {
            // ignored
        }
    }

    public BardPlayback CreatePlayback(MidiFile file, string filePath)
    {
        PreparePlaybackData(
            file,
            out var tempoMap,
            out var trackChunks,
            out var trackInfos,
            out var timedEvents
        );

        var midiFileConfig = ResolveMidiConfig(filePath, trackInfos);

        // create an internal Playback which delegates TryPlayEvent to our plugin device
        var playbackSettings = new PlaybackSettings
        {
            ClockSettings = new MidiClockSettings
            {
                CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator()
            },
        };

        var internalPlayback = new InternalPlayback(
            timedEvents,
            tempoMap,
            playbackSettings,
            SendMidiEvent)
        {
            InterruptNotesOnStop = true,
            TrackNotes = true,
            TrackProgram = true,
            Speed = Plugin.Config.PlaySpeed,
        };

        var wrapper = new BardPlayback(Plugin)
        {
            _playback = internalPlayback,
            MidiFile = file,
            FilePath = filePath,
            TrackChunks = trackChunks,
            TrackInfos = trackInfos,
            MidiFileConfig = midiFileConfig,
            DisplayName = Path.GetFileNameWithoutExtension(filePath)
        };

        return wrapper;
    }

    bool SendMidiEvent(MidiEvent midiEvent, object metadata)
    {
        try
        {
            Plugin.BardPlayDevice?.SendEventWithMetadata(midiEvent, metadata);
            return true;
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error sending event via BardPlayDevice");
            return false;
        }
    }

    // Internal Playback subclass which delegates TryPlayEvent to provided callback
    private sealed class InternalPlayback : Playback
    {
        private readonly Func<MidiEvent, object, bool> _tryPlayCallback;

        public InternalPlayback(IEnumerable<TimedEventWithMetadata> timedObjects, TempoMap tempoMap, PlaybackSettings settings, Func<MidiEvent, object, bool> tryPlayCallback)
            : base(timedObjects, tempoMap, settings)
        {
            _tryPlayCallback = tryPlayCallback;
        }

        protected override bool TryPlayEvent(MidiEvent midiEvent, object metadata)
        {
            if (_tryPlayCallback != null)
                return _tryPlayCallback(midiEvent, metadata);

            return base.TryPlayEvent(midiEvent, metadata);
        }
    }

    public double GetBpm()
    {
        Tempo bpm = null;
        var currentTime = GetCurrentTime(TimeSpanType.Midi);
        if (currentTime != null)
        {
            bpm = TempoMap?.GetTempoAtTime(currentTime);
        }

        if (bpm != null) return bpm.BeatsPerMinute;

        return 0;
    }

    public string GetBpmLabel()
    {
        Tempo bpm = null;
        var currentTime = GetCurrentTime(TimeSpanType.Midi);
        if (currentTime != null)
        {
            bpm = TempoMap?.GetTempoAtTime(currentTime);
        }

        var label = $" {Plugin.Config.PlaySpeed:F2}";

        if (bpm != null) label += $" ({bpm.BeatsPerMinute * Plugin.Config.PlaySpeed:F1} bpm)";
        return label;
    }

    public void SetSpeed(float playSpeed)
    {
        _ = playSpeed.Clamp(0.1f, 10f);
        var currenttime = GetCurrentTime(TimeSpanType.Midi);
        if (currenttime == null) return;

        Speed = playSpeed;
        MoveToTime(currenttime);

        if (DalamudApi.PartyList.IsPartyLeader())
            Plugin.IpcProvider.PlaybackSpeed(playSpeed);
    }

    // Delegate common Playback members to the internal playback instance
    public bool IsRunning => _playback?.IsRunning == true;
    public bool IsLoaded => _playback != null;
    public double Speed
    {
        get => _playback?.Speed ?? 1;
        set { if (_playback != null) _playback.Speed = value; }
    }
    public TempoMap TempoMap { get => _playback?.TempoMap; }
    public void Start() => _playback?.Start();
    public void Stop() => _playback?.Stop();
    public void MoveToStart() => _playback?.MoveToStart();
    public void MoveToTime(ITimeSpan time) => _playback?.MoveToTime(time);
    public T GetCurrentTime<T>() where T : ITimeSpan, new()
    {
        return _playback != null
            ? _playback.GetCurrentTime<T>()
            : new T();
    }
    public ITimeSpan GetCurrentTime(TimeSpanType timeType)
    {
        return _playback != null
            ? _playback.GetCurrentTime(timeType)
            : null;
    }
    public TimeSpan GetCurrentTimeSpan()
    {
        return GetCurrentTime<MetricTimeSpan>()?.GetTimeSpan() ?? TimeSpan.Zero;
    }
    public T GetDuration<T>() where T : ITimeSpan => _playback != null ? _playback.GetDuration<T>() : default;
    public ITimeSpan PlaybackStart { get => _playback?.PlaybackStart; set { if (_playback != null) _playback.PlaybackStart = value; } }
    public ITimeSpan PlaybackEnd { get => _playback?.PlaybackEnd; set { if (_playback != null) _playback.PlaybackEnd = value; } }
    public event EventHandler Started { add { if (_playback != null) _playback.Started += value; } remove { if (_playback != null) _playback.Started -= value; } }
    public event EventHandler Stopped { add { if (_playback != null) _playback.Stopped += value; } remove { if (_playback != null) _playback.Stopped -= value; } }
    public event EventHandler Finished { add { if (_playback != null) _playback.Finished += value; } remove { if (_playback != null) _playback.Finished -= value; } }
    public event EventHandler RepeatStarted { add { if (_playback != null) _playback.RepeatStarted += value; } remove { if (_playback != null) _playback.RepeatStarted -= value; } }
    public event EventHandler<NotesEventArgs> NotesPlaybackStarted { add { if (_playback != null) _playback.NotesPlaybackStarted += value; } remove { if (_playback != null) _playback.NotesPlaybackStarted -= value; } }
    public event EventHandler<NotesEventArgs> NotesPlaybackFinished { add { if (_playback != null) _playback.NotesPlaybackFinished += value; } remove { if (_playback != null) _playback.NotesPlaybackFinished -= value; } }
    public event EventHandler<MidiEventPlayedEventArgs> EventPlayed { add { if (_playback != null) _playback.EventPlayed += value; } remove { if (_playback != null) _playback.EventPlayed -= value; } }
    public event EventHandler<ErrorOccurredEventArgs> DeviceErrorOccurred { add { if (_playback != null) _playback.DeviceErrorOccurred += value; } remove { if (_playback != null) _playback.DeviceErrorOccurred -= value; } }

    private static bool IsMidiTracksEqualJsonConfigFileTracks(MidiFileConfig midiFileConfig, TrackInfo[] trackInfos)
    {
        if (midiFileConfig == null)
            return false;

        // check track count
        if (midiFileConfig.Tracks.Count != trackInfos.Length)
        {
            var message = $"""
            The number of tracks in the JSON file doesn't match the MIDI file
                JSON: {midiFileConfig.Tracks.Count}
                MIDI: {trackInfos.Length}
            """;

            DalamudApi.ChatGui.PrintError(message, "MidiBard JSON Rest", Style.Colors.SeYellow);

            return false;
        }

        // check track name
        for (int i = 0; i < trackInfos.Length; i++)
        {
            DbTrack dbTrack = midiFileConfig.Tracks[i];
            TrackInfo info = trackInfos[i];

            bool isSameTrackName = string.Equals(dbTrack.Name, info.TrackName, StringComparison.OrdinalIgnoreCase);
            bool isSameTrackIndex = dbTrack.Index == info.Index;

            if (!isSameTrackName || !isSameTrackIndex)
            {
                var message = $"""
                Track {i + 1} name mismatch:
                  JSON: {dbTrack.Name}
                  MIDI: {info.TrackName}
                """;

                DalamudApi.ChatGui.PrintError(message, "MidiBard JSON Rest", Style.Colors.SeYellow);
                return false;
            }
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

        var midiConfigFromTrack = Plugin.MidiFileConfigManager.GetMidiConfigFromTrack(trackInfos);

        // use midi specific json config
        var midiFileConfig = Plugin.MidiFileConfigManager.GetMidiConfigFromFile(filePath);
        var isMidiTracksEqualJsonConfigFileTracks = IsMidiTracksEqualJsonConfigFileTracks(midiFileConfig, trackInfos);
        var useMidiJsonFileConfig = midiFileConfig is not null && isMidiTracksEqualJsonConfigFileTracks;
        if (useMidiJsonFileConfig)
        {
            DalamudApi.PluginLog.Debug($"[LoadPlayback] using json midi file config");
            return LoadMidiConfigFromJson(midiFileConfig);
        }

        // Track assignment rules
        if (Plugin.Config.TrackAssignment.Enabled &&
            Plugin.Config.EnsembleMemberConfigs.Any(m => m.TrackAssignmentEnabled && m.TrackRules?.Count > 0))
        {
            DalamudApi.PluginLog.Debug($"[LoadPlayback] using track assignment rules");
            return LoadMidiConfigFromTrackAssignmentRules(midiConfigFromTrack);
        }

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
        var defaultPerformerTrackMapping = Plugin.MidiFileConfigManager.defaultPerformer?.TrackMappingDict ?? new();
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
        Plugin.MidiFileConfigManager.UsingDefaultPerformer = false;
        for (int i = 0; i < midiFileConfig.Tracks.Count; i++)
            Cids[i] = MidiFileConfig.GetFirstCidInParty(midiFileConfig.Tracks[i], Plugin.Config.EnsembleMemberConfigs);

        return midiFileConfig;
    }

    private MidiFileConfig LoadMidiConfigFromTrackStatus(MidiFileConfig midiConfigFromTrack)
    {
        Plugin.MidiFileConfigManager.UsingDefaultPerformer = false;
        Cids = new long[100];

        var bardCid = (long)DalamudApi.PlayerState.ContentId;
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
            Plugin.MidiFileConfigManager.LoadDefaultPerformer();

        return Plugin.MidiFileConfigManager.LoadDefaultPerformer(midiConfigFromTrack, ref Cids);
    }

    private MidiFileConfig LoadMidiConfigFromTrackAssignmentRules(MidiFileConfig midiConfigFromTrack)
    {
        return Plugin.MidiFileConfigManager.BuildMidiConfigFromRules(midiConfigFromTrack, ref Cids);
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
        float progress = currentTime.SafeDivideMetricTimeSpan(duration);
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

        var trackInfo = new TrackInfo
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
            IsProgramElectricGuitar = TrackName.ToLower().Replace(":", "").StartsWith("programelectricguitar"),
            //Channels = i.Events.OfType<ProgramChangeEvent>().Select(j => j.Channel).Distinct().Union(notes.Select(note => note.Channel).Distinct()).ToArray()
        };

        return trackInfo;
    }

    // private readonly Dictionary<long, Dictionary<SevenBitNumber, int>> timeEventsDictionary =
    //     new Dictionary<long, Dictionary<SevenBitNumber, int>>();

    // internal static List<Dictionary<long, Dictionary<int, int>>> TrimDict;

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
        return new BardPlayDevice.MidiPlaybackMetaData(Plugin.BardPlayDevice, trackIndex, time, compareValue);
    }

    public uint GetInstrumentId()
    {
        // find instrument from config file
        uint? configInstrumentId = MidiFileConfig?.Tracks?
            .FirstOrDefault(track => track.Enabled && MidiFileConfig.IsCidOnTrack((long)DalamudApi.PlayerState.ContentId, track, Plugin.Config.EnsembleMemberConfigs))
            ?.Instrument;

        // find instrument from first enabled track
        uint? trackInstrumentId = TrackInfos?
            .FirstOrDefault(i => i.IsEnabled(Plugin.Config.TrackStatus))
            ?.InstrumentIdFromTrackName((ushort)Plugin.Config.DefaultInstrumentId);

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
            var trackInstrumentId = trackInfo.InstrumentIdFromTrackName((ushort)Plugin.Config.DefaultInstrumentId);
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
                var isBardAssignedToTrack = MidiFileConfig.GetFirstCidInParty(tracks[trackIndex], Plugin.Config.EnsembleMemberConfigs) == (long)DalamudApi.PlayerState.ContentId;
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
