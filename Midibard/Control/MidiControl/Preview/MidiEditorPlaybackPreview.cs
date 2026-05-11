using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using FFXIVClientStructs.FFXIV.Client.Sound;
using InteropGenerator.Runtime;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Util;
using MidiBard.Util.MidiPreprocessor;

namespace MidiBard.Control.MidiControl.Preview;

internal sealed unsafe class MidiEditorPlaybackPreview : IDisposable
{
    // Performance key releases audibly roll off instead of cutting immediately.
    // Transport changes still use a short cleanup fade so stale preview sounds do not linger.
    private const uint NoteReleaseFadeMs = 500;
    private const uint CleanupFadeMs = 50;

    private readonly record struct PreviewPlaybackMetadata(
        int TrackIndex,
        long Time,
        int EventValue);

    private readonly record struct PreviewProgramEvent(double TimeSeconds, int TrackIndex, int Channel, SevenBitNumber Program);

    private readonly record struct HeldNote(int Channel, int MidiNote, int GameNote, uint InstrumentId, long OnsetTick, long Sequence);

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
        public SevenBitNumber?[] ChannelPrograms { get; } = new SevenBitNumber?[16];
    }

    private sealed class TrackPlaybackState
    {
        public List<HeldNote> HeldNotes { get; } = new();
        public HeldNote? CurrentNote { get; set; }
        public nint CurrentSound { get; set; }
    }

    private readonly Plugin plugin;
    // This is deliberately live rather than snapshotted: users can hide/show piano-roll
    // tracks during playback and preview should mute/resume those tracks immediately.
    private readonly Func<int, bool> trackVisibilityProvider;
    private readonly object playbackLock = new();
    private readonly List<PreviewProgramEvent> programEvents = new();
    private readonly HashSet<uint> missingSampleLogged = new();
    private readonly HashSet<uint> missingPathLogged = new();
    private Playback playback;
    private TrackPreviewState[] trackStates = Array.Empty<TrackPreviewState>();
    private TrackPlaybackState[] trackPlaybackStates = Array.Empty<TrackPlaybackState>();
    private long nextNoteSequence;
    private double durationSeconds;
    private bool hasEvents;

    public MidiEditorPlaybackPreview(Plugin plugin, Func<int, bool> trackVisibilityProvider = null)
    {
        this.plugin = plugin;
        this.trackVisibilityProvider = trackVisibilityProvider ?? (_ => true);
    }

    public bool IsPlaying => playback?.IsRunning == true;
    public double PositionSeconds => GetPlaybackPositionSeconds();
    public double DurationSeconds => durationSeconds;
    public bool HasEvents => hasEvents;
    public string? StatusMessage { get; private set; }

    public void Load(EditableMidiFile? file, bool preservePosition)
    {
        var oldPosition = preservePosition ? PositionSeconds : 0.0;
        StopAllSounds();
        DisposePlayback();
        programEvents.Clear();
        trackStates = Array.Empty<TrackPreviewState>();
        trackPlaybackStates = Array.Empty<TrackPlaybackState>();
        nextNoteSequence = 0;
        durationSeconds = 0.0;
        hasEvents = false;
        StatusMessage = null;
        missingSampleLogged.Clear();
        missingPathLogged.Clear();

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

        playback.Speed = Math.Max(0.1, plugin.Config.PlaySpeed);
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
            StopAllSoundsLocked(CleanupFadeMs);
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

        playback.Speed = Math.Max(0.1, plugin.Config.PlaySpeed);

        // Visibility changes are UI-only and do not generate MIDI events, so poll while playing.
        lock (playbackLock)
            RefreshAllTrackPlayback(NoteReleaseFadeMs);
    }

    private void BuildTrackStates(EditableMidiFile file)
    {
        trackStates = new TrackPreviewState[file.Tracks.Count];
        trackPlaybackStates = new TrackPlaybackState[file.Tracks.Count];
        for (var i = 0; i < file.Tracks.Count; i++)
        {
            var track = file.Tracks[i];
            var baseInstrumentId = ResolveTrackInstrument(track.Name);
            trackPlaybackStates[i] = new TrackPlaybackState();
            trackStates[i] = new TrackPreviewState
            {
                TrackName = track.Name,
                Transpose = TrackInfo.GetTransposeByName(track.Name),
                BaseInstrumentId = baseInstrumentId,
            };
        }
    }

    private uint? ResolveTrackInstrument(string trackName)
    {
        if (plugin.Config.ForceDefaultInstrument && plugin.Config.DefaultInstrumentId > 0)
            return plugin.Config.DefaultInstrumentId;

        return TrackInfo.GetInstrumentIdByName(trackName, (ushort?)plugin.Config.DefaultInstrumentId);
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

        tempoMap = snapshot.GetTempoMap();
        var playbackEvents = new List<PreviewTimedEvent>();
        programEvents.Clear();

        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            if (file.Tracks[trackIndex].IsConductorTrack)
                continue;

            foreach (var timedEvent in chunks[trackIndex].GetTimedEvents())
            {
                if (!TryCreatePlaybackEvent(trackIndex, timedEvent, tempoMap, out var playbackEvent))
                    continue;

                playbackEvents.Add(playbackEvent);
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

        if (timedEvent.Event is ProgramChangeEvent programChange)
        {
            var seconds = ToSeconds(TimeConverter.ConvertTo<MetricTimeSpan>(timedEvent.Time, tempoMap));
            programEvents.Add(new PreviewProgramEvent(seconds, trackIndex, channel, programChange.ProgramNumber));
        }

        playbackEvent = new PreviewTimedEvent(
            timedEvent.Event,
            timedEvent.Time,
            new PreviewPlaybackMetadata(trackIndex, timedEvent.Time, eventValue));
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
            Speed = Math.Max(0.1, plugin.Config.PlaySpeed),
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
                ProcessEvent(midiEvent, previewMetadata);

            return true;
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "[MidiEditorPreview] Error processing preview playback event.");
            return false;
        }
    }

    private void ProcessEvent(MidiEvent midiEvent, PreviewPlaybackMetadata metadata)
    {
        switch (midiEvent)
        {
            case ProgramChangeEvent programChange:
                if ((uint)metadata.TrackIndex >= (uint)trackStates.Length)
                    return;
                trackStates[metadata.TrackIndex].ChannelPrograms[(byte)programChange.Channel] = programChange.ProgramNumber;
                break;

            case NoteOffEvent noteOff:
                StopNote(metadata.TrackIndex, (byte)noteOff.Channel, (byte)noteOff.NoteNumber);
                break;

            case NoteOnEvent noteOn when (byte)noteOn.Velocity == 0:
                StopNote(metadata.TrackIndex, (byte)noteOn.Channel, (byte)noteOn.NoteNumber);
                break;

            case NoteOnEvent noteOn:
                PlayNote(metadata.TrackIndex, (byte)noteOn.Channel, (byte)noteOn.NoteNumber, metadata.Time);
                break;
        }
    }

    private void PlayNote(int trackIndex, int channel, int midiNote, long onsetTick)
    {
        var trackIsVisible = IsTrackVisible(trackIndex);
        if (!TryCreateHeldNote(trackIndex, channel, midiNote, onsetTick, trackIsVisible, out var heldNote))
            return;

        // Hidden tracks still keep held-note state so showing the track again can resume
        // a note that DryWetMidi still considers active.
        var playbackState = trackPlaybackStates[trackIndex];
        playbackState.HeldNotes.Add(heldNote);
        RefreshTrackPlayback(trackIndex, NoteReleaseFadeMs);
    }

    private bool TryCreateHeldNote(int trackIndex, int channel, int midiNote, long onsetTick, bool trackIsVisible, out HeldNote heldNote)
    {
        heldNote = default;

        if ((uint)trackIndex >= (uint)trackStates.Length || (uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return false;

        var trackState = trackStates[trackIndex];
        var translated = TrackInfo.TranslateNoteNumber(
            midiNote + trackState.Transpose,
            plugin.Config.TransposeGlobal,
            plugin.Config.AdaptNotesOOR);

        if (translated is < 0 or > 36)
            return false;

        var instrumentId = ResolveInstrumentForEvent(trackState, channel);
        if (instrumentId == null || instrumentId == 0)
        {
            if (trackIsVisible)
                StatusMessage = "Preview skipped a note because no instrument could be resolved.";
            return false;
        }

        heldNote = new HeldNote(channel, midiNote, translated, instrumentId.Value, onsetTick, nextNoteSequence++);
        return true;
    }

    private void RefreshTrackPlayback(int trackIndex, uint fadeOutDuration)
    {
        if ((uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return;

        var playbackState = trackPlaybackStates[trackIndex];
        if (!IsTrackVisible(trackIndex))
        {
            // Visibility is a live mute, not a MIDI NoteOff: keep HeldNotes intact.
            StopCurrentTrackSound(playbackState, fadeOutDuration);
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

        var sound = StartSound(winningNote.Value);
        if (sound == 0)
            return;

        playbackState.CurrentNote = winningNote;
        playbackState.CurrentSound = sound;
    }

    private static HeldNote? GetWinningNote(TrackPlaybackState playbackState)
    {
        if (playbackState.HeldNotes.Count == 0)
            return null;

        // The game performs one note per character/track. Later notes interrupt earlier
        // held notes; simultaneous chords choose the lowest translated game note.
        var winningNote = playbackState.HeldNotes[0];
        for (var i = 1; i < playbackState.HeldNotes.Count; i++)
        {
            var note = playbackState.HeldNotes[i];
            if (note.OnsetTick > winningNote.OnsetTick ||
                (note.OnsetTick == winningNote.OnsetTick && note.GameNote < winningNote.GameNote) ||
                (note.OnsetTick == winningNote.OnsetTick && note.GameNote == winningNote.GameNote && note.Sequence < winningNote.Sequence))
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

    private nint StartSound(HeldNote note)
    {
        if (!PerformanceSampleCatalog.TryGet(note.InstrumentId, out var sample))
        {
            if (missingSampleLogged.Add(note.InstrumentId))
                DalamudApi.PluginLog.Warning($"[MidiEditorPreview] No performance sample definition for instrument {note.InstrumentId}.");
            StatusMessage = $"No sample definition for instrument {note.InstrumentId}.";
            return 0;
        }

        if (!PerformanceSampleCatalog.TryResolvePath(sample, out var path))
        {
            if (missingPathLogged.Add(note.InstrumentId))
                DalamudApi.PluginLog.Warning($"[MidiEditorPreview] Could not resolve SCD path for {sample.InstrumentName} ({sample.FileName}). Use the Performance Sample Probe to capture the in-game path.");
            StatusMessage = $"Missing sample path: {sample.FileName}";
            return 0;
        }

        var soundManager = SoundManager.Instance();
        if (soundManager == null)
            return 0;

        var pathBytes = Encoding.UTF8.GetBytes(path + '\0');
        fixed (byte* pathPtr = pathBytes)
        {
            var soundData = soundManager->PlaySound(
                (CStringPointer)pathPtr,
                sample.Volume,
                sample.FadeInDuration,
                0f,
                0f,
                0f,
                sample.Speed,
                sample.A9,
                sample.GetSoundNumber(note.GameNote),
                sample.AutoRelease,
                sample.VolumeCategory,
                sample.A13,
                sample.GetMidiNote(note.GameNote),
                sample.A15,
                sample.DefaultFadeOut,
                sample.IsPositional,
                sample.A18);

            return (nint)soundData;
        }
    }

    private uint? ResolveInstrumentForEvent(TrackPreviewState trackState, int channel)
    {
        var baseInstrumentId = trackState.BaseInstrumentId;
        var hasBaseInstrument = baseInstrumentId is > 0;

        // Track-name instrument mapping is the editor's source of truth. Program changes
        // are fallback metadata, except guitar tracks can use them as tone variants.
        if ((uint)channel < 16 && trackState.ChannelPrograms[channel] is { } program &&
            TryResolveProgramInstrument(program, out var programInstrumentId))
        {
            if (!hasBaseInstrument)
                return programInstrumentId;

            if (InstrumentHelper.IsGuitar(baseInstrumentId!.Value) && InstrumentHelper.IsGuitar(programInstrumentId))
                return programInstrumentId;
        }

        return baseInstrumentId;
    }

    private static bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId)
    {
        instrumentId = 0;
        return InstrumentHelper.ProgramInstruments.TryGetValue(program, out instrumentId) &&
            instrumentId > 0 &&
            PerformanceSampleCatalog.TryGet(instrumentId, out _);
    }

    private void StopNote(int trackIndex, int channel, int midiNote)
    {
        if ((uint)trackIndex >= (uint)trackPlaybackStates.Length)
            return;

        var playbackState = trackPlaybackStates[trackIndex];
        var heldNoteIndex = playbackState.HeldNotes.FindIndex(note => note.Channel == channel && note.MidiNote == midiNote);
        if (heldNoteIndex < 0)
            return;

        playbackState.HeldNotes.RemoveAt(heldNoteIndex);
        RefreshTrackPlayback(trackIndex, NoteReleaseFadeMs);
    }

    private static void StopCurrentTrackSound(TrackPlaybackState playbackState, uint fadeOutDuration)
    {
        StopSound(playbackState.CurrentSound, fadeOutDuration);
        playbackState.CurrentSound = 0;
        playbackState.CurrentNote = null;
    }

    private static void StopSound(nint sound, uint fadeOutDuration)
    {
        if (sound == 0)
            return;

        try
        {
            // SoundData.Stop takes a fade-out duration, which gives direct SCD playback
            // the same release tail users hear from performance key releases.
            ((SoundData*)sound)->Stop(fadeOutDuration);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Verbose(e, "[MidiEditorPreview] Failed to stop preview sound.");
        }
    }

    private void StopAllSounds()
    {
        lock (playbackLock)
            StopAllSoundsLocked(CleanupFadeMs);
    }

    private void StopAllSoundsLocked(uint fadeOutDuration)
    {
        // Transport actions reset playback state, unlike live visibility muting.
        foreach (var playbackState in trackPlaybackStates)
        {
            StopSound(playbackState.CurrentSound, fadeOutDuration);
            playbackState.HeldNotes.Clear();
            playbackState.CurrentSound = 0;
            playbackState.CurrentNote = null;
        }
    }

    private void ResetProgramStates()
    {
        lock (playbackLock)
            ResetProgramStatesLocked();
    }

    private void ResetProgramStatesLocked()
    {
        foreach (var trackState in trackStates)
            Array.Clear(trackState.ChannelPrograms);
    }

    private void ApplyProgramStateAtLocked(double seconds)
    {
        foreach (var programEvent in programEvents)
        {
            if (programEvent.TimeSeconds > seconds)
                break;

            if ((uint)programEvent.TrackIndex < (uint)trackStates.Length)
                trackStates[programEvent.TrackIndex].ChannelPrograms[programEvent.Channel] = programEvent.Program;
        }
    }

    private void PlaybackFinished(object? sender, EventArgs e)
    {
        lock (playbackLock)
        {
            StopAllSoundsLocked(NoteReleaseFadeMs);
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
