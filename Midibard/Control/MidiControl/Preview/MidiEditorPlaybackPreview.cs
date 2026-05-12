using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Util.MidiPreprocessor;

namespace MidiBard.Control.MidiControl.Preview;

internal sealed unsafe class MidiEditorPlaybackPreview : IDisposable
{
    private const int SameOnsetRollStepMs = 35;
    private static readonly TimeSpan SameOnsetRollStep = TimeSpan.FromMilliseconds(SameOnsetRollStepMs);

    private readonly record struct PreviewPlaybackMetadata(
        int TrackIndex,
        long Time,
        double TimeSeconds,
        int EventValue,
        int? ResolvedGameNote = null,
        uint? ResolvedInstrumentId = null);

    private readonly record struct PreviewProgramEvent(double TimeSeconds, int TrackIndex, int Channel, SevenBitNumber Program);

    private readonly record struct HeldNote(int Channel, int MidiNote, int GameNote, uint InstrumentId, long OnsetTick, double OnsetSeconds, long Sequence);

    internal readonly record struct EventSnapshot(
        int TrackIndex,
        long Time,
        string EventType,
        int Channel,
        int EventValue,
        int? ProgramNumber = null);

    internal readonly record struct TrackSnapshot(
        int TrackIndex,
        int HeldNoteCount,
        int? CurrentMidiNote,
        int? CurrentGameNote,
        uint? CurrentInstrumentId,
        nint CurrentSound);

    private sealed class PreviewTimedEvent : TimedEvent, IMetadata
    {
        public PreviewTimedEvent(MidiEvent midiEvent, long time, PreviewPlaybackMetadata metadata)
            : base(midiEvent, time)
        {
            Metadata = metadata;
        }

        public object Metadata { get; set; }
    }

    private sealed class InternalPlayback : Playback
    {
        private readonly Func<MidiEvent, object, bool> tryPlayCallback;

        public InternalPlayback(IEnumerable<PreviewTimedEvent> timedObjects, TempoMap tempoMap, PlaybackSettings settings, Func<MidiEvent, object, bool> tryPlayCallback)
            : base(timedObjects, tempoMap, settings)
        {
            this.tryPlayCallback = tryPlayCallback;
        }

        protected override bool TryPlayEvent(MidiEvent midiEvent, object metadata)
            => tryPlayCallback(midiEvent, metadata);
    }

    private sealed class TrackPreviewState
    {
        public string TrackName { get; init; } = string.Empty;
        public int Transpose { get; init; }
        public uint? BaseInstrumentId { get; init; }
        public bool IsProgramElectricGuitar { get; init; }
        public SevenBitNumber?[] FallbackChannelPrograms { get; } = new SevenBitNumber?[16];
        public SevenBitNumber?[] GuitarToneChannelPrograms { get; } = new SevenBitNumber?[16];
    }

    private sealed class TrackPlaybackState
    {
        public List<HeldNote> HeldNotes { get; } = new();
        public List<HeldNote> SameOnsetRollQueue { get; } = new();
        public HeldNote? CurrentNote { get; set; }
        public nint CurrentSound { get; set; }
        public List<RetainedPreviewSound> SoundsForCleanup { get; } = new();
        public long? SameOnsetRollTick { get; set; }
        public double SameOnsetRollElapsedSeconds { get; set; }
        public IDisposable? SameOnsetRollSchedule { get; set; }
        public long SameOnsetRollVersion { get; set; }
    }

    private sealed class RetainedPreviewSound(nint sound)
    {
        public nint Sound { get; } = sound;
        public IDisposable? CleanupSchedule { get; set; }
        public bool Stopped { get; set; }
    }

    private sealed class PendingPreviewSchedule : IDisposable
    {
        public IDisposable? Schedule { get; set; }
        public bool Cancelled { get; private set; }

        public void Dispose()
        {
            Cancelled = true;
            Schedule?.Dispose();
        }
    }

    private readonly IMidiEditorPreviewSettings settings;
    private readonly IMidiEditorPreviewInstrumentCatalog instrumentCatalog;
    private readonly IMidiEditorPreviewSoundPlayer soundPlayer;
    private readonly IMidiEditorPreviewScheduler scheduler;
    private readonly MidiEditorPreviewReleasePolicy releasePolicy = new();
    private readonly MidiEditorPreviewCompensationPolicy compensationPolicy;
    // This is deliberately live rather than snapshotted: users can hide/show piano-roll
    // tracks during playback and preview should mute/resume those tracks immediately.
    private readonly Func<int, bool> trackVisibilityProvider;
    private readonly object playbackLock = new();
    private readonly List<PreviewProgramEvent> programEvents = new();
    private readonly List<EventSnapshot> eventSnapshots = new();
    private readonly List<PendingPreviewSchedule> pendingCompensatedEventSchedules = new();
    private readonly HashSet<uint> duplicateInstrumentDiagnosticsLogged = new();
    private Playback playback;
    private TrackPreviewState[] trackStates = Array.Empty<TrackPreviewState>();
    private TrackPlaybackState[] trackPlaybackStates = Array.Empty<TrackPlaybackState>();
    private long nextNoteSequence;
    private long compensatedEventScheduleVersion;
    private double durationSeconds;
    private bool hasEvents;

    public MidiEditorPlaybackPreview(Plugin plugin, Func<int, bool> trackVisibilityProvider = null)
        : this(
            new PluginMidiEditorPreviewSettings(plugin),
            new DefaultMidiEditorPreviewInstrumentCatalog(),
            new DalamudMidiEditorPreviewSoundPlayer(),
            trackVisibilityProvider,
            compensationProvider: new PluginMidiEditorPreviewCompensationProvider(plugin))
    {
    }

    internal MidiEditorPlaybackPreview(
        IMidiEditorPreviewSettings settings,
        IMidiEditorPreviewInstrumentCatalog instrumentCatalog,
        IMidiEditorPreviewSoundPlayer soundPlayer,
        Func<int, bool> trackVisibilityProvider = null,
        IMidiEditorPreviewScheduler? scheduler = null,
        IMidiEditorPreviewCompensationProvider? compensationProvider = null)
    {
        this.settings = settings;
        this.instrumentCatalog = instrumentCatalog;
        this.soundPlayer = soundPlayer;
        this.scheduler = scheduler ?? new TimerMidiEditorPreviewScheduler();
        compensationPolicy = new MidiEditorPreviewCompensationPolicy(
            compensationProvider ?? NoOpMidiEditorPreviewCompensationProvider.Instance);
        this.trackVisibilityProvider = trackVisibilityProvider ?? (_ => true);
    }

    public bool IsPlaying => playback?.IsRunning == true;
    public double PositionSeconds => GetPlaybackPositionSeconds();
    public double DurationSeconds => durationSeconds;
    public bool HasEvents => hasEvents;
    public string? StatusMessage { get; private set; }
    internal IReadOnlyList<EventSnapshot> EventSnapshots => eventSnapshots;

    public void Load(EditableMidiFile? file, bool preservePosition)
    {
        var oldPosition = preservePosition ? PositionSeconds : 0.0;
        StopAllSounds();
        DisposePlayback();
        programEvents.Clear();
        eventSnapshots.Clear();
        duplicateInstrumentDiagnosticsLogged.Clear();
        trackStates = Array.Empty<TrackPreviewState>();
        trackPlaybackStates = Array.Empty<TrackPlaybackState>();
        nextNoteSequence = 0;
        durationSeconds = 0.0;
        hasEvents = false;
        StatusMessage = null;

        if (file == null)
            return;

        BuildTrackStates(file);
        var playbackEvents = BuildPlaybackEvents(file, out var tempoMap);
        hasEvents = playbackEvents.Any(ev => ev.Event is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0);
        if (!hasEvents)
            return;

        playback = CreatePlayback(playbackEvents, tempoMap);
        durationSeconds = GetDurationSeconds(playback);

        if (preservePosition)
            Seek(oldPosition);
    }

    public void Restart()
    {
        Seek(0.0);
        Play();
    }

    public void Play()
    {
        if (!HasEvents)
            return;

        if (playback == null)
            return;

        if (durationSeconds > 0 && PositionSeconds >= durationSeconds)
            Seek(0.0);

        playback.Speed = Math.Max(0.1, settings.PlaySpeed);
        playback.Start();
    }

    public void Pause()
    {
        if (playback?.IsRunning != true)
            return;

        playback.Stop();
        StopAllSounds();
    }

    public void Stop()
    {
        if (playback != null)
        {
            playback.Stop();
            playback.MoveToStart();
        }

        StopAllSounds();
        ResetProgramStates();
    }

    public void Seek(double seconds)
    {
        if (playback == null)
            return;

        var clampedSeconds = Math.Clamp(seconds, 0.0, Math.Max(durationSeconds, 0.0));
        var wasPlaying = playback.IsRunning;
        if (wasPlaying)
            playback.Stop();

        lock (playbackLock)
        {
            StopAllSoundsLocked(MidiEditorPreviewReleasePolicy.CleanupFadeMs);
            ResetProgramStatesLocked();
            ApplyProgramStateAtLocked(clampedSeconds);
        }

        playback.MoveToTime(ToMetricTimeSpan(clampedSeconds));

        if (wasPlaying)
            playback.Start();
    }

    public void Update()
    {
        if (playback == null || !playback.IsRunning)
            return;

        playback.Speed = Math.Max(0.1, settings.PlaySpeed);

        // Visibility changes are UI-only and do not generate MIDI events, so poll while playing.
        lock (playbackLock)
            RefreshAllTrackPlayback(MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);
    }

    private void BuildTrackStates(EditableMidiFile file)
    {
        trackStates = new TrackPreviewState[file.Tracks.Count];
        trackPlaybackStates = new TrackPlaybackState[file.Tracks.Count];
        for (var i = 0; i < file.Tracks.Count; i++)
        {
            var track = file.Tracks[i];
            var baseInstrumentId = instrumentCatalog.ResolveTrackInstrument(
                track.Name,
                settings.DefaultInstrumentId,
                settings.ForceDefaultInstrument);
            trackPlaybackStates[i] = new TrackPlaybackState();
            trackStates[i] = new TrackPreviewState
            {
                TrackName = track.Name,
                Transpose = TrackInfo.GetTransposeByName(track.Name),
                BaseInstrumentId = baseInstrumentId,
                IsProgramElectricGuitar = TrackInfo.IsProgramElectricGuitarTrackName(track.Name),
            };
        }
    }

    private List<PreviewTimedEvent> BuildPlaybackEvents(EditableMidiFile file, out TempoMap tempoMap)
    {
        // Build DryWetMidi playback from a cloned editor snapshot instead of the saved file.
        // Loaded tracks may contain unsaved in-memory edits in EditableEvent/NoteOffSource.
        var chunks = new TrackChunk[file.Tracks.Count];
        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            chunks[trackIndex] = BuildPlaybackTrackChunk(file.Tracks[trackIndex]);
            // Keep the same legacy MIDI compatibility normal playback uses: some files have
            // NoteOff events on the wrong channel, which breaks DryWetMidi note tracking.
            MidiPreprocessor.FixNoteOffChannels(chunks[trackIndex]);
        }

        var snapshot = new MidiFile(chunks)
        {
            TimeDivision = file.Source.TimeDivision,
        };

        // Match the player's AntiStack setting on the cloned snapshot only; the editor's
        // live MIDI data must remain unchanged until the user explicitly edits or saves.
        if (settings.AntiStackType != AntiStackType.Off)
            MidiPreprocessor.RemoveStackedNotes(snapshot, settings.AntiStackType);

        tempoMap = snapshot.GetTempoMap();
        var playbackEvents = new List<PreviewTimedEvent>();
        programEvents.Clear();
        eventSnapshots.Clear();

        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            if (file.Tracks[trackIndex].IsConductorTrack)
                continue;

            foreach (var timedEvent in chunks[trackIndex].GetTimedEvents())
            {
                if (!TryCreatePlaybackEvent(trackIndex, timedEvent, tempoMap, out var playbackEvent))
                    continue;

                playbackEvents.Add(playbackEvent);
                eventSnapshots.Add(CreateEventSnapshot(playbackEvent));
            }
        }

        programEvents.Sort((a, b) =>
        {
            var timeCompare = a.TimeSeconds.CompareTo(b.TimeSeconds);
            return timeCompare != 0 ? timeCompare : a.TrackIndex.CompareTo(b.TrackIndex);
        });

        return playbackEvents
            .OrderBy(ev => ev.Time)
            .ThenBy(ev => ((PreviewPlaybackMetadata)ev.Metadata).EventValue)
            .ToList();
    }

    private static TrackChunk BuildPlaybackTrackChunk(EditableTrack track)
    {
        if (track.Events == null)
            return new TrackChunk(track.Chunk.Events.Select(ev => ev.Clone()));

        // Do not FlushChanges here; preview must not commit edit buffers just to play them.
        var chunk = new TrackChunk();
        using var manager = chunk.ManageTimedEvents();
        foreach (var timedEvent in EnumerateLiveTimedEvents(track))
            manager.Objects.Add(CloneTimedEvent(timedEvent));

        return chunk;
    }

    private static IEnumerable<TimedEvent> EnumerateLiveTimedEvents(EditableTrack track)
    {
        foreach (var editableEvent in track.Events)
        {
            yield return editableEvent.Source;
            if (editableEvent.NoteOffSource != null)
                yield return editableEvent.NoteOffSource;
        }
    }

    private static TimedEvent CloneTimedEvent(TimedEvent timedEvent)
        => new(timedEvent.Event.Clone(), timedEvent.Time);

    private bool TryCreatePlaybackEvent(int trackIndex, TimedEvent timedEvent, TempoMap tempoMap, out PreviewTimedEvent playbackEvent)
    {
        playbackEvent = null;
        if (!TryGetEventInfo(timedEvent.Event, out var channel, out var eventValue))
            return false;

        var seconds = ToSeconds(TimeConverter.ConvertTo<MetricTimeSpan>(timedEvent.Time, tempoMap));
        if (timedEvent.Event is ProgramChangeEvent programChange)
        {
            programEvents.Add(new PreviewProgramEvent(seconds, trackIndex, channel, programChange.ProgramNumber));
        }

        playbackEvent = new PreviewTimedEvent(
            timedEvent.Event,
            timedEvent.Time,
            new PreviewPlaybackMetadata(trackIndex, timedEvent.Time, seconds, eventValue));
        return true;
    }

    private static bool TryGetEventInfo(MidiEvent midiEvent, out int channel, out int eventValue)
    {
        channel = 0;
        eventValue = -1;

        switch (midiEvent)
        {
            case ProgramChangeEvent programChange:
                channel = (byte)programChange.Channel;
                eventValue = -2;
                return true;
            case NoteOffEvent noteOff:
                channel = (byte)noteOff.Channel;
                eventValue = (byte)noteOff.NoteNumber;
                return true;
            case NoteOnEvent noteOn:
                channel = (byte)noteOn.Channel;
                eventValue = (byte)noteOn.NoteNumber;
                return true;
            default:
                return false;
        }
    }

    private static EventSnapshot CreateEventSnapshot(PreviewTimedEvent playbackEvent)
    {
        var metadata = (PreviewPlaybackMetadata)playbackEvent.Metadata;
        TryGetEventInfo(playbackEvent.Event, out var channel, out var eventValue);
        var programNumber = playbackEvent.Event is ProgramChangeEvent programChange
            ? (int)(byte)programChange.ProgramNumber
            : (int?)null;

        return new EventSnapshot(
            metadata.TrackIndex,
            playbackEvent.Time,
            playbackEvent.Event.EventType.ToString(),
            channel,
            eventValue,
            programNumber);
    }

    private Playback CreatePlayback(List<PreviewTimedEvent> playbackEvents, TempoMap tempoMap)
    {
        var playbackSettings = new PlaybackSettings
        {
            ClockSettings = new MidiClockSettings
            {
                CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator()
            },
        };

        var result = new InternalPlayback(playbackEvents, tempoMap, playbackSettings, SendPreviewEvent)
        {
            InterruptNotesOnStop = true,
            TrackNotes = true,
            TrackProgram = true,
            Speed = Math.Max(0.1, settings.PlaySpeed),
            // Let every same-channel/pitch event reach the preview layer. The preview's
            // per-track monophonic state handles duplicates and visibility filtering.
            SendNoteOnEventsForActiveNotes = true,
            SendNoteOffEventsForNonActiveNotes = true,
        };

        result.Finished += PlaybackFinished;
        return result;
    }

    private bool SendPreviewEvent(MidiEvent midiEvent, object metadata)
    {
        try
        {
            if (metadata is not PreviewPlaybackMetadata previewMetadata)
                return true;

            lock (playbackLock)
            {
                var delayMs = GetCompensationDelayMs(midiEvent, previewMetadata, out var resolvedMetadata);
                if (delayMs <= 0)
                    ProcessEvent(midiEvent, resolvedMetadata);
                else
                    ScheduleCompensatedEvent(midiEvent.Clone(), resolvedMetadata, delayMs);
            }

            return true;
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditorPreview] Error processing preview playback event.");
            return false;
        }
    }

    internal void ProcessEventForTesting(MidiEvent midiEvent, int trackIndex, long time, double? timeSeconds = null)
    {
        lock (playbackLock)
            ProcessEvent(midiEvent, new PreviewPlaybackMetadata(trackIndex, time, timeSeconds ?? time / 1000.0, -1));
    }

    internal bool SendEventForTesting(MidiEvent midiEvent, int trackIndex, long time, double? timeSeconds = null)
        => SendPreviewEvent(midiEvent, new PreviewPlaybackMetadata(trackIndex, time, timeSeconds ?? time / 1000.0, -1));

    internal IReadOnlyList<TrackSnapshot> GetTrackSnapshots()
    {
        lock (playbackLock)
        {
            return trackPlaybackStates
                .Select((state, index) =>
                {
                    var current = state.CurrentNote;
                    return new TrackSnapshot(
                        index,
                        state.HeldNotes.Count,
                        current?.MidiNote,
                        current?.GameNote,
                        current?.InstrumentId,
                        state.CurrentSound);
                })
                .ToArray();
        }
    }

    internal void RefreshVisibilityForTesting()
    {
        lock (playbackLock)
            RefreshAllTrackPlayback(MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);
    }

    private void ProcessEvent(MidiEvent midiEvent, PreviewPlaybackMetadata metadata)
    {
        switch (midiEvent)
        {
            case ProgramChangeEvent programChange:
                if ((uint)metadata.TrackIndex >= (uint)trackStates.Length)
                    return;
                ProcessProgramChange(metadata.TrackIndex, (byte)programChange.Channel, programChange.ProgramNumber);
                break;

            case NoteOffEvent noteOff:
                StopNote(metadata.TrackIndex, (byte)noteOff.Channel, (byte)noteOff.NoteNumber, metadata.TimeSeconds);
                break;

            case NoteOnEvent noteOn when (byte)noteOn.Velocity == 0:
                StopNote(metadata.TrackIndex, (byte)noteOn.Channel, (byte)noteOn.NoteNumber, metadata.TimeSeconds);
                break;

            case NoteOnEvent noteOn:
                PlayNote(
                    metadata.TrackIndex,
                    (byte)noteOn.Channel,
                    (byte)noteOn.NoteNumber,
                    metadata.Time,
                    metadata.TimeSeconds,
                    metadata.ResolvedGameNote,
                    metadata.ResolvedInstrumentId);
                break;
        }
    }

    private int GetCompensationDelayMs(MidiEvent midiEvent, PreviewPlaybackMetadata metadata, out PreviewPlaybackMetadata resolvedMetadata)
    {
        resolvedMetadata = metadata;
        if (!TryGetNoteEventInfo(midiEvent, out var channel, out var midiNote, out var isNoteOn))
            return 0;

        if ((uint)metadata.TrackIndex >= (uint)trackStates.Length)
            return 0;

        var trackState = trackStates[metadata.TrackIndex];
        var gameNote = TrackInfo.TranslateNoteNumber(
            midiNote + trackState.Transpose,
            settings.TransposeGlobal,
            settings.AdaptNotesOOR);

        if (gameNote is < 0 or > 36)
            return 0;

        var instrumentId = ResolveInstrumentForEvent(metadata.TrackIndex, trackState, channel);
        if (instrumentId is null or 0)
            return 0;

        resolvedMetadata = metadata with
        {
            ResolvedGameNote = gameNote,
            ResolvedInstrumentId = instrumentId.Value,
        };
        return compensationPolicy.GetDelayMs(
            instrumentId.Value,
            gameNote,
            metadata.TrackIndex,
            metadata.Time,
            isNoteOn);
    }

    private static bool TryGetNoteEventInfo(MidiEvent midiEvent, out int channel, out int midiNote, out bool isNoteOn)
    {
        channel = 0;
        midiNote = 0;
        isNoteOn = false;

        switch (midiEvent)
        {
            case NoteOffEvent noteOff:
                channel = (byte)noteOff.Channel;
                midiNote = (byte)noteOff.NoteNumber;
                return true;
            case NoteOnEvent noteOn:
                channel = (byte)noteOn.Channel;
                midiNote = (byte)noteOn.NoteNumber;
                isNoteOn = (byte)noteOn.Velocity > 0;
                return true;
            default:
                return false;
        }
    }

    private void ScheduleCompensatedEvent(MidiEvent midiEvent, PreviewPlaybackMetadata metadata, int delayMs)
    {
        var pending = new PendingPreviewSchedule();
        var scheduleVersion = compensatedEventScheduleVersion;
        var delayedMetadata = metadata with
        {
            TimeSeconds = metadata.TimeSeconds + delayMs / 1000.0,
        };
        pendingCompensatedEventSchedules.Add(pending);
        pending.Schedule = scheduler.Schedule(
            TimeSpan.FromMilliseconds(delayMs),
            () => ProcessScheduledCompensatedEvent(pending, midiEvent, delayedMetadata, scheduleVersion));
    }

    private void ProcessScheduledCompensatedEvent(
        PendingPreviewSchedule pending,
        MidiEvent midiEvent,
        PreviewPlaybackMetadata metadata,
        long scheduleVersion)
    {
        lock (playbackLock)
        {
            pendingCompensatedEventSchedules.Remove(pending);
            if (pending.Cancelled || scheduleVersion != compensatedEventScheduleVersion)
                return;

            try
            {
                ProcessEvent(midiEvent, metadata);
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "[MidiEditorPreview] Error processing compensated preview event.");
            }
        }
    }

    private void PlayNote(
        int trackIndex,
        int channel,
        int midiNote,
        long onsetTick,
        double onsetSeconds,
        int? resolvedGameNote,
        uint? resolvedInstrumentId)
    {
        var trackIsVisible = IsTrackVisible(trackIndex);
        if (!TryCreateHeldNote(
                trackIndex,
                channel,
                midiNote,
                onsetTick,
                onsetSeconds,
                trackIsVisible,
                resolvedGameNote,
                resolvedInstrumentId,
                out var heldNote))
            return;

        // Hidden tracks still keep held-note state so showing the track again can resume
        // a note that DryWetMidi still considers active.
        var playbackState = trackPlaybackStates[trackIndex];
        if (playbackState.SameOnsetRollTick is { } rollTick && rollTick != heldNote.OnsetTick)
            CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);

        playbackState.HeldNotes.Add(heldNote);

        if (!trackIsVisible)
        {
            CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);
            RemoveSameOnsetLowerNotes(playbackState, heldNote.OnsetTick);
            StopAllTrackSounds(playbackState, MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);
            return;
        }

        if (TryQueueSameOnsetRoll(trackIndex, playbackState, heldNote))
            return;

        RefreshTrackPlaybackForMusicalChange(trackIndex, onsetSeconds);
    }

    private bool TryCreateHeldNote(
        int trackIndex,
        int channel,
        int midiNote,
        long onsetTick,
        double onsetSeconds,
        bool trackIsVisible,
        int? resolvedGameNote,
        uint? resolvedInstrumentId,
        out HeldNote heldNote)
    {
        heldNote = default;

        if ((uint)trackIndex >= (uint)trackStates.Length || (uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return false;

        var trackState = trackStates[trackIndex];
        var translated = resolvedGameNote ?? TrackInfo.TranslateNoteNumber(
            midiNote + trackState.Transpose,
            settings.TransposeGlobal,
            settings.AdaptNotesOOR);

        if (translated is < 0 or > 36)
            return false;

        var instrumentId = resolvedInstrumentId ?? ResolveInstrumentForEvent(trackIndex, trackState, channel);
        if (instrumentId == null || instrumentId == 0)
        {
            if (trackIsVisible)
                StatusMessage = "Preview skipped a note because no instrument could be resolved.";
            return false;
        }

        heldNote = new HeldNote(channel, midiNote, translated, instrumentId.Value, onsetTick, onsetSeconds, nextNoteSequence++);
        return true;
    }

    private static void RemoveSameOnsetLowerNotes(TrackPlaybackState playbackState, long onsetTick)
    {
        var highestMidiNote = int.MinValue;
        var sameOnsetCount = 0;
        foreach (var note in playbackState.HeldNotes)
        {
            if (note.OnsetTick != onsetTick)
                continue;

            sameOnsetCount++;
            if (note.MidiNote > highestMidiNote)
                highestMidiNote = note.MidiNote;
        }

        if (sameOnsetCount < 2)
            return;

        playbackState.HeldNotes.RemoveAll(note =>
            note.OnsetTick == onsetTick &&
            note.MidiNote < highestMidiNote);
    }

    private bool TryQueueSameOnsetRoll(int trackIndex, TrackPlaybackState playbackState, HeldNote heldNote)
    {
        var currentNote = playbackState.CurrentNote;
        if (!currentNote.HasValue || currentNote.Value.OnsetTick != heldNote.OnsetTick)
            return false;

        if (heldNote.MidiNote <= currentNote.Value.MidiNote)
        {
            playbackState.HeldNotes.RemoveAll(note => note.Sequence == heldNote.Sequence);
            return true;
        }

        if (playbackState.SameOnsetRollTick != heldNote.OnsetTick)
        {
            CancelSameOnsetRoll(playbackState, pruneLowerNotes: false);
            playbackState.SameOnsetRollTick = heldNote.OnsetTick;
            playbackState.SameOnsetRollElapsedSeconds = 0.0;
            playbackState.SameOnsetRollVersion++;
        }

        AddSameOnsetRollNote(playbackState, heldNote);
        ScheduleSameOnsetRoll(trackIndex, playbackState);
        return true;
    }

    private static void AddSameOnsetRollNote(TrackPlaybackState playbackState, HeldNote heldNote)
    {
        if (playbackState.SameOnsetRollQueue.Any(note => note.Sequence == heldNote.Sequence))
            return;

        playbackState.SameOnsetRollQueue.Add(heldNote);
        playbackState.SameOnsetRollQueue.Sort((a, b) =>
        {
            var noteCompare = a.MidiNote.CompareTo(b.MidiNote);
            return noteCompare != 0 ? noteCompare : a.Sequence.CompareTo(b.Sequence);
        });
    }

    private void ScheduleSameOnsetRoll(int trackIndex, TrackPlaybackState playbackState)
    {
        if (playbackState.SameOnsetRollSchedule != null)
            return;

        var version = playbackState.SameOnsetRollVersion;
        playbackState.SameOnsetRollSchedule = scheduler.Schedule(
            SameOnsetRollStep,
            () => AdvanceSameOnsetRoll(trackIndex, version));
    }

    private void AdvanceSameOnsetRoll(int trackIndex, long version)
    {
        lock (playbackLock)
        {
            if ((uint)trackIndex >= (uint)trackPlaybackStates.Length)
                return;

            var playbackState = trackPlaybackStates[trackIndex];
            if (playbackState.SameOnsetRollVersion != version)
                return;

            playbackState.SameOnsetRollSchedule = null;

            if (playbackState.SameOnsetRollTick is not { } rollTick)
                return;

            if (!IsTrackVisible(trackIndex))
            {
                CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);
                StopAllTrackSounds(playbackState, MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);
                return;
            }

            var currentNote = playbackState.CurrentNote;
            if (!currentNote.HasValue || currentNote.Value.OnsetTick != rollTick)
            {
                CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);
                return;
            }

            PruneSameOnsetRollQueue(playbackState, currentNote.Value);
            if (playbackState.SameOnsetRollQueue.Count == 0)
            {
                CompleteSameOnsetRoll(playbackState);
                return;
            }

            var nextNote = playbackState.SameOnsetRollQueue[0];
            playbackState.SameOnsetRollQueue.RemoveAt(0);
            playbackState.SameOnsetRollElapsedSeconds += SameOnsetRollStep.TotalSeconds;
            var releaseSeconds = currentNote.Value.OnsetSeconds + playbackState.SameOnsetRollElapsedSeconds;
            ReleaseCurrentTrackSound(playbackState, releaseSeconds);
            StartTrackSound(trackIndex, playbackState, nextNote);

            PruneSameOnsetRollQueue(playbackState, nextNote);
            if (playbackState.SameOnsetRollQueue.Count == 0)
                CompleteSameOnsetRoll(playbackState);
            else
                ScheduleSameOnsetRoll(trackIndex, playbackState);
        }
    }

    private static void PruneSameOnsetRollQueue(TrackPlaybackState playbackState, HeldNote currentNote)
    {
        playbackState.SameOnsetRollQueue.RemoveAll(note =>
            note.OnsetTick != currentNote.OnsetTick ||
            note.MidiNote <= currentNote.MidiNote ||
            !playbackState.HeldNotes.Any(held => held.Sequence == note.Sequence));
    }

    private static void CompleteSameOnsetRoll(TrackPlaybackState playbackState)
    {
        var rollTick = playbackState.SameOnsetRollTick;
        CancelSameOnsetRoll(playbackState, pruneLowerNotes: false);
        if (rollTick.HasValue)
            RemoveSameOnsetLowerNotes(playbackState, rollTick.Value);
    }

    private static void CancelSameOnsetRoll(TrackPlaybackState playbackState, bool pruneLowerNotes)
    {
        var rollTick = playbackState.SameOnsetRollTick;
        playbackState.SameOnsetRollVersion++;
        playbackState.SameOnsetRollSchedule?.Dispose();
        playbackState.SameOnsetRollSchedule = null;
        playbackState.SameOnsetRollQueue.Clear();
        playbackState.SameOnsetRollTick = null;
        playbackState.SameOnsetRollElapsedSeconds = 0.0;

        if (pruneLowerNotes && rollTick.HasValue)
            RemoveSameOnsetLowerNotes(playbackState, rollTick.Value);
    }

    private void RefreshTrackPlayback(int trackIndex, uint fadeOutDuration)
    {
        if ((uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return;

        var playbackState = trackPlaybackStates[trackIndex];
        if (!IsTrackVisible(trackIndex))
        {
            // Visibility is a live mute, not a MIDI NoteOff: keep HeldNotes intact.
            CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);
            StopAllTrackSounds(playbackState, fadeOutDuration);
            return;
        }

        if (playbackState.SameOnsetRollTick.HasValue &&
            playbackState.CurrentNote?.OnsetTick == playbackState.SameOnsetRollTick.Value &&
            playbackState.SameOnsetRollQueue.Count > 0)
        {
            return;
        }

        var winningNote = GetWinningNote(playbackState);

        if (playbackState.CurrentNote.HasValue && winningNote.HasValue &&
            IsSameSoundingNote(playbackState.CurrentNote.Value, winningNote.Value))
        {
            playbackState.CurrentNote = winningNote;
            return;
        }

        StopCurrentTrackSound(playbackState, fadeOutDuration);

        if (!winningNote.HasValue)
            return;

        StartTrackSound(trackIndex, playbackState, winningNote.Value);
    }

    private void RefreshTrackPlaybackForMusicalChange(int trackIndex, double releaseSeconds)
    {
        if ((uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return;

        var playbackState = trackPlaybackStates[trackIndex];
        if (!IsTrackVisible(trackIndex))
        {
            CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);
            StopAllTrackSounds(playbackState, MidiEditorPreviewReleasePolicy.MaximumDynamicReleaseFadeMs);
            return;
        }

        if (playbackState.SameOnsetRollTick.HasValue &&
            playbackState.CurrentNote is { } currentRollNote &&
            currentRollNote.OnsetTick == playbackState.SameOnsetRollTick.Value &&
            playbackState.SameOnsetRollQueue.Count > 0)
        {
            if (playbackState.HeldNotes.Any(note => note.Sequence == currentRollNote.Sequence))
                return;

            CancelSameOnsetRoll(playbackState, pruneLowerNotes: true);
        }

        var winningNote = GetWinningNote(playbackState);

        if (playbackState.CurrentNote.HasValue && winningNote.HasValue &&
            IsSameSoundingNote(playbackState.CurrentNote.Value, winningNote.Value))
        {
            playbackState.CurrentNote = winningNote;
            return;
        }

        ReleaseCurrentTrackSound(playbackState, releaseSeconds);

        if (!winningNote.HasValue)
            return;

        StartTrackSound(trackIndex, playbackState, winningNote.Value);
    }

    private void StartTrackSound(int trackIndex, TrackPlaybackState playbackState, HeldNote note)
    {
        LogDuplicateInstrumentPreviewIfNeeded(trackIndex, note);
        var sound = StartSound(trackIndex, note);
        if (sound == 0)
            return;

        playbackState.CurrentNote = note;
        playbackState.CurrentSound = sound;
    }

    private static HeldNote? GetWinningNote(TrackPlaybackState playbackState)
    {
        if (playbackState.HeldNotes.Count == 0)
            return null;

        // The game performs one note per character/track. Later notes interrupt earlier
        // held notes; simultaneous chords follow normal playback's low-to-high order, so
        // the highest raw MIDI note remains held.
        var winningNote = playbackState.HeldNotes[0];
        for (var i = 1; i < playbackState.HeldNotes.Count; i++)
        {
            var note = playbackState.HeldNotes[i];
            if (note.OnsetTick > winningNote.OnsetTick ||
                (note.OnsetTick == winningNote.OnsetTick && note.MidiNote > winningNote.MidiNote) ||
                (note.OnsetTick == winningNote.OnsetTick && note.MidiNote == winningNote.MidiNote && note.Sequence < winningNote.Sequence))
            {
                winningNote = note;
            }
        }

        return winningNote;
    }

    private void RefreshAllTrackPlayback(uint fadeOutDuration)
    {
        for (var trackIndex = 0; trackIndex < trackPlaybackStates.Length; trackIndex++)
            RefreshTrackPlayback(trackIndex, fadeOutDuration);
    }

    private bool IsTrackVisible(int trackIndex)
    {
        try
        {
            return trackVisibilityProvider(trackIndex);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Verbose(e, "[MidiEditorPreview] Failed to read preview track visibility.");
            return false;
        }
    }

    private static bool IsSameSoundingNote(HeldNote a, HeldNote b)
        => a.GameNote == b.GameNote && a.InstrumentId == b.InstrumentId && a.OnsetTick == b.OnsetTick;

    private void LogDuplicateInstrumentPreviewIfNeeded(int trackIndex, HeldNote note)
    {
        if (duplicateInstrumentDiagnosticsLogged.Contains(note.InstrumentId))
            return;

        var activeTrackLabels = new List<string>();
        for (var i = 0; i < trackPlaybackStates.Length; i++)
        {
            if (i == trackIndex || !IsTrackVisible(i))
                continue;

            var state = trackPlaybackStates[i];
            if (state.CurrentNote?.InstrumentId == note.InstrumentId ||
                state.HeldNotes.Any(held => held.InstrumentId == note.InstrumentId))
            {
                activeTrackLabels.Add(GetDiagnosticTrackLabel(i));
            }
        }

        if (activeTrackLabels.Count == 0)
            return;

        duplicateInstrumentDiagnosticsLogged.Add(note.InstrumentId);
        activeTrackLabels.Add(GetDiagnosticTrackLabel(trackIndex));
        DalamudApi.PluginLog.Verbose(
            $"[MidiEditorPreview] Multiple visible preview tracks are active with instrument {note.InstrumentId}: {string.Join(", ", activeTrackLabels)}. " +
            "If one part is inaudible, direct SCD playback may be voice-limited by the game sound manager for this sample.");
    }

    private string GetDiagnosticTrackLabel(int trackIndex)
    {
        if ((uint)trackIndex >= (uint)trackStates.Length)
            return $"#{trackIndex + 1}";

        var name = trackStates[trackIndex].TrackName;
        return string.IsNullOrWhiteSpace(name)
            ? $"#{trackIndex + 1}"
            : $"#{trackIndex + 1} {name}";
    }

    private nint StartSound(int trackIndex, HeldNote note)
    {
        var request = new PreviewSoundRequest(trackIndex, note.Channel, note.MidiNote, note.GameNote, note.InstrumentId);
        var sound = soundPlayer.Play(request, out var statusMessage);
        if (!string.IsNullOrWhiteSpace(statusMessage))
            StatusMessage = statusMessage;
        return sound;
    }

    private uint? ResolveInstrumentForEvent(int trackIndex, TrackPreviewState trackState, int channel)
    {
        var baseInstrumentId = trackState.BaseInstrumentId;
        var hasBaseInstrument = baseInstrumentId is > 0;

        if (!hasBaseInstrument)
            return ResolveFallbackProgramInstrument(trackState, channel);

        // Track-name/default instrument mapping is primary. Program changes only select
        // guitar tone variants when the existing GuitarToneMode setting allows it.
        if (!instrumentCatalog.IsGuitar(baseInstrumentId!.Value))
            return baseInstrumentId;

        if (TryResolveOverrideByTrackInstrument(trackIndex, baseInstrumentId.Value, out var overrideInstrumentId))
            return overrideInstrumentId;

        if ((uint)channel < 16 && trackState.GuitarToneChannelPrograms[channel] is { } program &&
            TryResolveGuitarProgramInstrument(program, out var guitarProgramInstrumentId))
            return guitarProgramInstrumentId;

        return baseInstrumentId;
    }

    private void ProcessProgramChange(int trackIndex, int channel, SevenBitNumber program)
    {
        if ((uint)trackIndex >= (uint)trackStates.Length || (uint)channel >= 16)
            return;

        var trackState = trackStates[trackIndex];

        // Raw program data remains available for unnamed-track fallback. Guitar tone
        // switching below mirrors BardPlayDevice's GuitarToneMode handling.
        trackState.FallbackChannelPrograms[channel] = program;

        switch (settings.GuitarToneMode)
        {
            case GuitarToneMode.Off:
                break;
            case GuitarToneMode.Standard:
                trackState.GuitarToneChannelPrograms[channel] = program;
                break;
            case GuitarToneMode.Simple:
                SetAllGuitarTonePrograms(trackState, program);
                break;
            case GuitarToneMode.OverrideByTrack:
                break;
            case GuitarToneMode.ProgramElectricGuitarMode:
                if (trackState.IsProgramElectricGuitar)
                    trackState.GuitarToneChannelPrograms[channel] = program;
                break;
            default:
                break;
        }
    }

    private static void SetAllGuitarTonePrograms(TrackPreviewState trackState, SevenBitNumber program)
    {
        for (var i = 0; i < trackState.GuitarToneChannelPrograms.Length; i++)
            trackState.GuitarToneChannelPrograms[i] = program;
    }

    private uint? ResolveFallbackProgramInstrument(TrackPreviewState trackState, int channel)
    {
        if ((uint)channel >= 16 || trackState.FallbackChannelPrograms[channel] is not { } program)
            return null;

        return instrumentCatalog.TryResolveProgramInstrument(program, out var instrumentId) ? instrumentId : null;
    }

    private bool TryResolveOverrideByTrackInstrument(int trackIndex, uint baseInstrumentId, out uint instrumentId)
    {
        instrumentId = 0;
        if (settings.GuitarToneMode != GuitarToneMode.OverrideByTrack || !instrumentCatalog.IsGuitar(baseInstrumentId))
            return false;

        if ((uint)trackIndex >= (uint)settings.TrackStatus.Length)
            return false;

        var tone = Math.Clamp(settings.TrackStatus[trackIndex].Tone, 0, 4);
        instrumentId = (uint)(24 + tone);
        return true;
    }

    private bool TryResolveGuitarProgramInstrument(SevenBitNumber program, out uint instrumentId)
    {
        if (instrumentCatalog.TryResolveProgramInstrument(program, out instrumentId) &&
            instrumentCatalog.IsGuitar(instrumentId))
            return true;

        instrumentId = 0;
        return false;
    }

    private void StopNote(int trackIndex, int channel, int midiNote, double releaseSeconds)
    {
        if ((uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return;

        var playbackState = trackPlaybackStates[trackIndex];
        playbackState.SameOnsetRollQueue.RemoveAll(note => note.Channel == channel && note.MidiNote == midiNote);
        var heldNoteIndex = playbackState.HeldNotes.FindIndex(note => note.Channel == channel && note.MidiNote == midiNote);
        if (heldNoteIndex < 0)
            return;

        playbackState.HeldNotes.RemoveAt(heldNoteIndex);
        RefreshTrackPlaybackForMusicalChange(trackIndex, releaseSeconds);
    }

    private void ReleaseCurrentTrackSound(TrackPlaybackState playbackState, double releaseSeconds)
    {
        var currentNote = playbackState.CurrentNote;
        var currentSound = playbackState.CurrentSound;
        playbackState.CurrentSound = 0;
        playbackState.CurrentNote = null;

        if (currentSound == 0 || !currentNote.HasValue)
            return;

        if (!releasePolicy.ShouldStopOnMusicalRelease(currentNote.Value.InstrumentId))
        {
            RetainNaturalOneShotSound(playbackState, currentSound, currentNote.Value.InstrumentId);
            return;
        }

        var heldSeconds = releaseSeconds - currentNote.Value.OnsetSeconds;
        var fadeOutDuration = releasePolicy.GetMusicalReleaseFadeMs(currentNote.Value.InstrumentId, heldSeconds);
        soundPlayer.Stop(currentSound, fadeOutDuration);
    }

    private void RetainNaturalOneShotSound(TrackPlaybackState playbackState, nint sound, uint instrumentId)
    {
        var retainedSound = new RetainedPreviewSound(sound);
        playbackState.SoundsForCleanup.Add(retainedSound);
        retainedSound.CleanupSchedule = scheduler.Schedule(
            TimeSpan.FromMilliseconds(releasePolicy.GetNaturalOneShotCleanupDelayMs(instrumentId)),
            () => CleanupRetainedSound(playbackState, retainedSound));
    }

    private void CleanupRetainedSound(TrackPlaybackState playbackState, RetainedPreviewSound retainedSound)
    {
        lock (playbackLock)
        {
            if (!playbackState.SoundsForCleanup.Remove(retainedSound))
                return;

            StopRetainedSound(retainedSound, MidiEditorPreviewReleasePolicy.CleanupFadeMs);
        }
    }

    private void StopCurrentTrackSound(TrackPlaybackState playbackState, uint fadeOutDuration)
    {
        if (playbackState.CurrentSound != 0)
            soundPlayer.Stop(playbackState.CurrentSound, fadeOutDuration);

        playbackState.CurrentSound = 0;
        playbackState.CurrentNote = null;
    }

    private void StopAllTrackSounds(TrackPlaybackState playbackState, uint fadeOutDuration)
    {
        StopCurrentTrackSound(playbackState, fadeOutDuration);
        foreach (var sound in playbackState.SoundsForCleanup.ToArray())
            StopRetainedSound(sound, fadeOutDuration);

        playbackState.SoundsForCleanup.Clear();
    }

    private void StopRetainedSound(RetainedPreviewSound retainedSound, uint fadeOutDuration)
    {
        retainedSound.CleanupSchedule?.Dispose();
        retainedSound.CleanupSchedule = null;
        if (retainedSound.Stopped || retainedSound.Sound == 0)
            return;

        soundPlayer.Stop(retainedSound.Sound, fadeOutDuration);
        retainedSound.Stopped = true;
    }

    private void StopAllSounds()
    {
        lock (playbackLock)
            StopAllSoundsLocked(MidiEditorPreviewReleasePolicy.CleanupFadeMs);
    }

    private void StopAllSoundsLocked(uint fadeOutDuration)
    {
        CancelPendingCompensatedEventsLocked();

        // Transport actions reset playback state, unlike live visibility muting.
        foreach (var playbackState in trackPlaybackStates)
        {
            CancelSameOnsetRoll(playbackState, pruneLowerNotes: false);
            StopAllTrackSounds(playbackState, fadeOutDuration);
            playbackState.HeldNotes.Clear();
        }
    }

    private void CancelPendingCompensatedEventsLocked()
    {
        compensatedEventScheduleVersion++;
        compensationPolicy.Reset();
        foreach (var pending in pendingCompensatedEventSchedules)
            pending.Dispose();

        pendingCompensatedEventSchedules.Clear();
    }

    private void ResetProgramStates()
    {
        lock (playbackLock)
            ResetProgramStatesLocked();
    }

    private void ResetProgramStatesLocked()
    {
        foreach (var trackState in trackStates)
        {
            Array.Clear(trackState.FallbackChannelPrograms);
            Array.Clear(trackState.GuitarToneChannelPrograms);
        }
    }

    private void ApplyProgramStateAtLocked(double seconds)
    {
        foreach (var programEvent in programEvents)
        {
            if (programEvent.TimeSeconds > seconds)
                break;

            ProcessProgramChange(programEvent.TrackIndex, programEvent.Channel, programEvent.Program);
        }
    }

    private void PlaybackFinished(object? sender, EventArgs e)
    {
        lock (playbackLock)
        {
            StopAllSoundsLocked(MidiEditorPreviewReleasePolicy.CleanupFadeMs);
            ResetProgramStatesLocked();
        }
    }

    private double GetPlaybackPositionSeconds()
    {
        if (playback == null)
            return 0.0;

        try
        {
            return Math.Clamp(ToSeconds(playback.GetCurrentTime<MetricTimeSpan>()), 0.0, Math.Max(durationSeconds, 0.0));
        }
        catch (ObjectDisposedException)
        {
            return 0.0;
        }
    }

    private static double GetDurationSeconds(Playback playback)
        => Math.Max(0.0, ToSeconds(playback.GetDuration<MetricTimeSpan>()));

    private static double ToSeconds(MetricTimeSpan timeSpan)
        => timeSpan.TotalMicroseconds / 1_000_000.0;

    private static MetricTimeSpan ToMetricTimeSpan(double seconds)
        => new((long)(Math.Max(0.0, seconds) * 1_000_000.0));

    private void DisposePlayback()
    {
        if (playback == null)
            return;

        try
        {
            playback.Finished -= PlaybackFinished;
            playback.Stop();
            playback.Dispose();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Verbose(e, "[MidiEditorPreview] Failed to dispose preview playback.");
        }
        finally
        {
            playback = null;
        }
    }

    public void Dispose()
    {
        StopAllSounds();
        DisposePlayback();
    }
}
