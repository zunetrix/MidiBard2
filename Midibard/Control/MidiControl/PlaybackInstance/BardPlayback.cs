// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

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

using static Dalamud.api;

namespace MidiBard.Control.MidiControl.PlaybackInstance;

internal sealed class BardPlayback : Playback
{
    internal MidiFileConfig MidiFileConfig { get; set; }
    internal MidiFile MidiFile { get; init; }
    internal string FilePath { get; init; }
    internal TrackChunk[] TrackChunks { get; init; }
    internal TrackInfo[] TrackInfos { get; init; }
    internal string DisplayName { get; init; }
    private static long[] Cids = new long[100];

    public static BardPlayback GetBardPlayback(MidiFile file, string filePath)
    {
        PreparePlaybackData(file, out var tempoMap, out var trackChunks, out var trackInfos, out var timedEventWithMetadata);
        MidiFileConfig midiFileConfig = ResolveMidiConfig(filePath, trackChunks, trackInfos);

        return new BardPlayback(timedEventWithMetadata, tempoMap)
        {
            MidiFile = file,
            FilePath = filePath,
            TrackChunks = trackChunks,
            TrackInfos = trackInfos,
            MidiFileConfig = midiFileConfig,
            DisplayName = Path.GetFileNameWithoutExtension(filePath)
        };
    }

    private static MidiFileConfig ResolveMidiConfig(string filePath, TrackChunk[] trackChunks, TrackInfo[] trackInfos)
    {
        // dont use midiFileConfi or Default Performer when not in a party
        var ignoreDefaultPerformer = api.PartyList.IsInParty() && MidiBard.config.lockTracks;
        if (!api.PartyList.IsInParty() || ignoreDefaultPerformer)
        {
            PluginLog.Debug($"[LoadPlayback] using config TrackStatus");
            return null;
        }

        var midiConfigFromTrack = MidiFileConfigManager.GetMidiConfigFromTrack(trackInfos);

        // PMD
        if (MidiBard.config.playOnMultipleDevices)
        {
            if (MidiBard.config.usingFileSharingServices)
            {
                PluginLog.Debug($"[LoadPlayback] using shared default performer");
                return LoadMidiConfigFromDefaultPerformer(midiConfigFromTrack);
            }

            PluginLog.Debug($"[LoadPlayback] using config TrackStatus");
            return LoadMidiConfigFromTrackStatus(midiConfigFromTrack);
        }

        // use midi specific json config
        // TODO: improve json config file changes detection
        // compare tracks name / order do decide what props to reset
        var midiFileConfig = MidiFileConfigManager.GetMidiConfigFromFile(filePath);
        var isMidiTracksEqualJsonConfigFileTracks = midiFileConfig?.Tracks.Count == trackChunks.Length;
        var useMidiJsonFileConfig = midiFileConfig is not null && isMidiTracksEqualJsonConfigFileTracks;
        if (useMidiJsonFileConfig)
        {
            PluginLog.Debug($"[LoadPlayback] using json midi file config");
            return LoadMidiConfigFromJson(midiFileConfig, filePath);
        }

        // default performer
        var defaultPerformerTrackMapping = MidiFileConfigManager.defaultPerformer?.TrackMappingDict ?? new();
        var useDefaultPerformer = defaultPerformerTrackMapping.Count > 0;
        if (useDefaultPerformer)
        {
            PluginLog.Debug($"[LoadPlayback] using default performer");
            return LoadMidiConfigFromDefaultPerformer(midiConfigFromTrack);
        }

        // if in a party but no default perform or midi json file use config.TrackStatus
        // for solo bards while in party or ensemble with PMD to not lose the assigned tracks
        PluginLog.Debug($"[LoadPlayback] no json midi file or default performer using config TrackStatus");
        return LoadMidiConfigFromTrackStatus(midiConfigFromTrack);
    }

    private static MidiFileConfig LoadMidiConfigFromJson(MidiFileConfig midiFileConfig, string filePath)
    {
        var defaultPerformerFallback = LoadDefaultPerformer(midiFileConfig.JsonClone()); //clone this damn thing :P
        MidiFileConfigManager.UsingDefaultPerformer = false;

        bool changed = false;

        for (int i = 0; i < midiFileConfig.Tracks.Count; i++)
            Cids[i] = MidiFileConfig.GetFirstCidInParty(midiFileConfig.Tracks[i]);

        // fall back to default performer if can't find any record in the individual config(caused by changing characters)
        for (int i = 0; i < defaultPerformerFallback.Tracks.Count; i++)
        {
            var cid = MidiFileConfig.GetFirstCidInParty(defaultPerformerFallback.Tracks[i]);

            if (!Cids.Contains(cid))
            {
                midiFileConfig.Tracks[i].AssignedCids.Add(cid);
                changed = true;
            }
        }

        if (changed)
        {
            try
            {
                midiFileConfig.Save(filePath);
            }
            catch
            {
                // ignored
            }
        }

        return midiFileConfig;
    }

    private static MidiFileConfig LoadMidiConfigFromTrackStatus(MidiFileConfig midiConfigFromTrack)
    {
        MidiFileConfigManager.UsingDefaultPerformer = false;
        Cids = new long[100];

        var bardCid = (long)api.ClientState.LocalContentId;
        for (int i = 0; i < midiConfigFromTrack.Tracks.Count; i++)
        {
            if (MidiBard.config.TrackStatus[i].Enabled)
            {
                midiConfigFromTrack.Tracks[i].Enabled = true;
                midiConfigFromTrack.Tracks[i].AssignedCids.Add(bardCid);
                Cids[i] = bardCid;
            }
        }

        return midiConfigFromTrack;
    }

    private static MidiFileConfig LoadMidiConfigFromDefaultPerformer(MidiFileConfig midiConfigFromTrack)
    {
        // PMD
        if (MidiBard.config.usingFileSharingServices)
            MidiFileConfigManager.LoadDefaultPerformer();

        return LoadDefaultPerformer(midiConfigFromTrack);
    }

    private BardPlayback(IEnumerable<TimedEventWithMetadata> timedObjects, TempoMap tempoMap)
    : base(timedObjects, tempoMap, new PlaybackSettings { ClockSettings = new MidiClockSettings { CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator() } })
    {
    }

    protected override bool TryPlayEvent(MidiEvent midiEvent, object metadata)
    {
        // Place your logic here
        // Return true if event played (sent to plug-in); false otherwise
        MidiBard.BardPlayDevice.SendEventWithMetadata(midiEvent, metadata);
        return true;
    }

    private static void PreparePlaybackData(MidiFile file, out TempoMap tempoMap, out TrackChunk[] trackChunks, out TrackInfo[] trackInfos, out TimedEventWithMetadata[] timedEventWithMetadata)
    {
        if (MidiBard.config.AntiStackType != AntiStackType.Off)
            file = MidiPreprocessor.RemoveStackedNotes(file, MidiBard.config.AntiStackType);
        if (MidiBard.config.AlignMidi)
            file = MidiPreprocessor.RealignMidiFile(file, MidiBard.config.AlignMidiStartOffset);

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

    private static TempoMap TryGetTempoMap(MidiFile midiFile)
    {
        try
        {
            return midiFile.GetTempoMap();
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, "[LoadPlayback] error when getting file TempoMap, using default TempoMap instead.");
            return TempoMap.Default;
        }
    }

    private static IEnumerable<TrackChunk> GetNoteTracks(MidiFile midifile)
    {
        try
        {
            return midifile.GetTrackChunks().Where(i => i.Events.Any(j => j is NoteOnEvent));
        }
        catch (Exception e)
        {
            PluginLog.Warning(e, $"[LoadPlayback] error when parsing tracks, falling back to generated NoteEvent playback.");
            try
            {
                PluginLog.Debug($"[LoadPlayback] file.Chunks.Count {midifile.Chunks.Count}");
                var trackChunks = midifile.GetTrackChunks().ToArray();
                PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.Count {trackChunks.Length}");
                PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.First {trackChunks.First()}");
                PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.Events.Count {trackChunks.First().Events.Count}");
                PluginLog.Debug($"[LoadPlayback] file.GetTrackChunks.Events.OfType<NoteEvent>.Count {trackChunks.First().Events.OfType<NoteEvent>().Count()}");

                return trackChunks.Where(i => i.Events.Any(j => j is NoteOnEvent))
                    .Select((i) =>
                    {
                        var noteEvents = i.Events.Where(midiEvent => midiEvent is NoteEvent or ProgramChangeEvent or TextEvent);
                        return new TrackChunk(noteEvents);
                    });
            }
            catch (Exception exception2)
            {
                PluginLog.Error(exception2, "[LoadPlayback] still errors? check your file");
                throw;
            }
        }
    }

    private static TrackInfo GetTrackInfos(TrackChunk i, int index, TempoMap tempoMap)
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
    private static IEnumerable<TimedEventWithMetadata> GetTimedEventWithMetadata(IEnumerable<TrackChunk> tracks)
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
    static BardPlayDevice.MidiPlaybackMetaData GetMetadataForEvent(MidiEvent midiEvent, long time, int trackIndex)
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    public static MidiFileConfig LoadDefaultPerformer(MidiFileConfig midiFileConfig)
    {
        PluginLog.Debug($"[LoadPlayback] using default performer");
        MidiFileConfigManager.UsingDefaultPerformer = true;
        var trackMapping = MidiFileConfigManager.defaultPerformer?.TrackMappingDict ?? new();
        Cids = new long[100];

        var partyMembers = api.PartyList.ToList();

        foreach (var member in partyMembers)
        {
            if (member?.ContentId > 0 && trackMapping.TryGetValue(member.ContentId, out var trackIndices))
            {
                foreach (var trackIdx in trackIndices)
                    Cids[trackIdx] = member.ContentId;
            }
        }

        for (int i = 0; i < midiFileConfig.Tracks.Count; i++)
        {
            try
            {
                var track = midiFileConfig.Tracks[i];
                if (MidiFileConfig.GetFirstCidInParty(track) <= 0 && Cids[i] > 0)
                {
                    if (!track.AssignedCids.Contains(Cids[i]))
                        track.AssignedCids.Insert(0, Cids[i]);
                }
            }
            catch (Exception e)
            {
                PluginLog.Warning($"Track {i}: {e.Message}");
            }
        }

        return midiFileConfig;
    }

    public uint GetInstrumentId()
    {
        // find instrument from config file
        uint? configInstrumentId = MidiFileConfig?.Tracks?
            .FirstOrDefault(t => t.Enabled && MidiFileConfig.IsCidOnTrack((long)api.ClientState.LocalContentId, t))
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
                PluginLog.Information($"applying transpose {transposePerTrack:+#;-#;0} for track [{trackInfo.Index + 1}] {trackInfo.TrackName}");
            }

            MidiBard.config.TrackStatus[trackInfo.Index].Transpose = transposePerTrack;
        }

        MidiBard.config.TransposeGlobal = 0;
    }

    internal void UpdateGuitarToneByConfig()
    {
        var playback = MidiBard.CurrentPlayback;
        if (playback == null) return;

        foreach (var (trackInfo, index) in playback.TrackInfos.Select((info, i) => (info, i)))
        {
            var trackInstrumentId = trackInfo.InstrumentIDFromTrackName;
            if (trackInstrumentId is uint instrumentId && MidiBard.Instruments[instrumentId].IsGuitar)
            {
                MidiBard.config.TrackStatus[index].Tone = MidiBard.Instruments[instrumentId].GuitarTone;
            }
        }
    }

    internal void SyncTrackStatusWithMidiFileConfig()
    {
        // if (MidiFileConfig == null || MidiFileConfigManager.UsingDefaultPerformer)
        //     return;

        var tracks = MidiFileConfig.Tracks;
        MidiBard.config.ResetTrackStatus();
        PluginLog.Debug($"[LoadPlayback] SyncTrackStatusWithMidiFileConfig");
        for (var trackIndex = 0; trackIndex < MidiFileConfig.Tracks.Count; trackIndex++)
        {
            try
            {
                var isBardAssignedToTrack = MidiFileConfig.GetFirstCidInParty(tracks[trackIndex]) == (long)api.ClientState.LocalContentId;
                MidiBard.config.TrackStatus[trackIndex].Enabled = tracks[trackIndex].Enabled && isBardAssignedToTrack;
                MidiBard.config.TrackStatus[trackIndex].Transpose = tracks[trackIndex].Transpose;
                MidiBard.config.TrackStatus[trackIndex].Tone = InstrumentHelper.GetGuitarTone(tracks[trackIndex].Instrument);
                MidiBard.config.SoloedTrack = null;
            }
            catch (Exception e)
            {
                PluginLog.Error(e, $"error when updating track {trackIndex}");
            }
        }
    }
}
