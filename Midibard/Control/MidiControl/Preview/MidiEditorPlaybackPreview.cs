using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using FFXIVClientStructs.FFXIV.Client.Sound;
using InteropGenerator.Runtime;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Util;

namespace MidiBard.Control.MidiControl.Preview;

internal sealed unsafe class MidiEditorPlaybackPreview : IDisposable
{
    private enum PreviewEventKind
    {
        ProgramChange,
        NoteOff,
        NoteOn,
    }

    private readonly record struct PreviewEvent(
        double TimeSeconds,
        long Tick,
        int TrackIndex,
        int Channel,
        MidiEvent Event,
        PreviewEventKind Kind,
        int SortValue);

    private readonly record struct HeldNote(int Channel, int MidiNote, int GameNote, uint InstrumentId, long OnsetTick, long Sequence);

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
    private readonly List<PreviewEvent> events = new();
    private readonly HashSet<uint> missingSampleLogged = new();
    private readonly HashSet<uint> missingPathLogged = new();
    private TrackPreviewState[] trackStates = Array.Empty<TrackPreviewState>();
    private TrackPlaybackState[] trackPlaybackStates = Array.Empty<TrackPlaybackState>();
    private int nextEventIndex;
    private long nextNoteSequence;
    private long lastTimestamp;
    private double positionSeconds;
    private double durationSeconds;

    public MidiEditorPlaybackPreview(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public bool IsPlaying { get; private set; }
    public double PositionSeconds => positionSeconds;
    public double DurationSeconds => durationSeconds;
    public bool HasEvents => events.Count > 0;
    public string? StatusMessage { get; private set; }

    public void Load(EditableMidiFile? file, bool preservePosition)
    {
        var oldPosition = preservePosition ? positionSeconds : 0.0;
        StopAllSounds();
        IsPlaying = false;
        events.Clear();
        trackStates = Array.Empty<TrackPreviewState>();
        trackPlaybackStates = Array.Empty<TrackPlaybackState>();
        nextEventIndex = 0;
        nextNoteSequence = 0;
        positionSeconds = 0.0;
        durationSeconds = 0.0;
        StatusMessage = null;
        missingSampleLogged.Clear();
        missingPathLogged.Clear();

        if (file == null)
            return;

        BuildTrackStates(file);
        BuildEvents(file);
        durationSeconds = Math.Max(0.0, events.Count == 0 ? 0.0 : events.Max(ev => ev.TimeSeconds));

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

        IsPlaying = true;
        lastTimestamp = Stopwatch.GetTimestamp();
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        IsPlaying = false;
        StopAllSounds();
    }

    public void Stop()
    {
        IsPlaying = false;
        StopAllSounds();
        positionSeconds = 0.0;
        nextEventIndex = 0;
        ResetProgramStates();
    }

    public void Seek(double seconds)
    {
        positionSeconds = Math.Clamp(seconds, 0.0, Math.Max(durationSeconds, 0.0));
        nextEventIndex = FindFirstEventAtOrAfter(positionSeconds);
        StopAllSounds();
        ResetProgramStates();
        ApplyProgramStateAt(positionSeconds);
        lastTimestamp = Stopwatch.GetTimestamp();
    }

    public void Update()
    {
        if (!IsPlaying)
            return;

        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - lastTimestamp) / (double)Stopwatch.Frequency;
        lastTimestamp = now;
        positionSeconds = Math.Min(durationSeconds, positionSeconds + elapsed);

        while (nextEventIndex < events.Count && events[nextEventIndex].TimeSeconds <= positionSeconds + 0.001)
        {
            ProcessEvent(events[nextEventIndex]);
            nextEventIndex++;
        }

        if (positionSeconds >= durationSeconds)
        {
            IsPlaying = false;
            StopAllSounds();
        }
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

    private void BuildEvents(EditableMidiFile file)
    {
        var tempoMap = file.TempoMap;

        for (var trackIndex = 0; trackIndex < file.Tracks.Count; trackIndex++)
        {
            var track = file.Tracks[trackIndex];
            if (track.IsConductorTrack)
                continue;

            if (track.Events != null)
            {
                foreach (var editableEvent in track.Events)
                {
                    AddTimedEvent(trackIndex, editableEvent.Source, tempoMap);
                    if (editableEvent.NoteOffSource != null)
                        AddTimedEvent(trackIndex, editableEvent.NoteOffSource, tempoMap);
                }
            }
            else
            {
                foreach (var timedEvent in track.Chunk.GetTimedEvents())
                    AddTimedEvent(trackIndex, timedEvent, tempoMap);
            }
        }

        events.Sort((a, b) =>
        {
            var timeCompare = a.TimeSeconds.CompareTo(b.TimeSeconds);
            if (timeCompare != 0) return timeCompare;
            var kindCompare = a.Kind.CompareTo(b.Kind);
            if (kindCompare != 0) return kindCompare;
            return a.SortValue.CompareTo(b.SortValue);
        });
    }

    private void AddTimedEvent(int trackIndex, TimedEvent timedEvent, TempoMap tempoMap)
    {
        if (!TryClassifyEvent(timedEvent.Event, out var kind, out var channel, out var sortValue))
            return;

        var seconds = TimeConverter.ConvertTo<MetricTimeSpan>(timedEvent.Time, tempoMap).TotalMicroseconds / 1_000_000.0;
        events.Add(new PreviewEvent(seconds, timedEvent.Time, trackIndex, channel, timedEvent.Event, kind, sortValue));
    }

    private static bool TryClassifyEvent(MidiEvent midiEvent, out PreviewEventKind kind, out int channel, out int sortValue)
    {
        kind = PreviewEventKind.NoteOn;
        channel = 0;
        sortValue = 0;

        switch (midiEvent)
        {
            case ProgramChangeEvent programChange:
                kind = PreviewEventKind.ProgramChange;
                channel = (byte)programChange.Channel;
                sortValue = -2;
                return true;
            case NoteOffEvent noteOff:
                kind = PreviewEventKind.NoteOff;
                channel = (byte)noteOff.Channel;
                sortValue = (byte)noteOff.NoteNumber;
                return true;
            case NoteOnEvent noteOn when (byte)noteOn.Velocity == 0:
                kind = PreviewEventKind.NoteOff;
                channel = (byte)noteOn.Channel;
                sortValue = (byte)noteOn.NoteNumber;
                return true;
            case NoteOnEvent noteOn:
                kind = PreviewEventKind.NoteOn;
                channel = (byte)noteOn.Channel;
                sortValue = (byte)noteOn.NoteNumber;
                return true;
            default:
                return false;
        }
    }

    private void ProcessEvent(PreviewEvent previewEvent)
    {
        switch (previewEvent.Event)
        {
            case ProgramChangeEvent programChange:
                if ((uint)previewEvent.TrackIndex >= (uint)trackStates.Length)
                    return;
                trackStates[previewEvent.TrackIndex].ChannelPrograms[previewEvent.Channel] = programChange.ProgramNumber;
                break;

            case NoteOffEvent noteOff:
                StopNote(previewEvent.TrackIndex, previewEvent.Channel, (byte)noteOff.NoteNumber);
                break;

            case NoteOnEvent noteOn when (byte)noteOn.Velocity == 0:
                StopNote(previewEvent.TrackIndex, previewEvent.Channel, (byte)noteOn.NoteNumber);
                break;

            case NoteOnEvent noteOn:
                PlayNote(previewEvent.TrackIndex, previewEvent.Channel, (byte)noteOn.NoteNumber, previewEvent.Tick);
                break;
        }
    }

    private void PlayNote(int trackIndex, int channel, int midiNote, long onsetTick)
    {
        if (!TryCreateHeldNote(trackIndex, channel, midiNote, onsetTick, out var heldNote))
            return;

        var playbackState = trackPlaybackStates[trackIndex];
        playbackState.HeldNotes.Add(heldNote);
        RefreshTrackPlayback(trackIndex, 0);
    }

    private bool TryCreateHeldNote(int trackIndex, int channel, int midiNote, long onsetTick, out HeldNote heldNote)
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
        RefreshTrackPlayback(trackIndex, 50);
    }

    private static void StopCurrentTrackSound(TrackPlaybackState playbackState, uint fadeOutDuration)
    {
        StopSound(playbackState.CurrentSound, fadeOutDuration);
        playbackState.CurrentSound = 0;
        playbackState.CurrentNote = null;
    }

    private void ClearTrackPlaybackStates()
    {
        foreach (var playbackState in trackPlaybackStates)
        {
            playbackState.HeldNotes.Clear();
            playbackState.CurrentSound = 0;
            playbackState.CurrentNote = null;
        }
    }

    private static void StopSound(nint sound, uint fadeOutDuration)
    {
        if (sound == 0)
            return;

        try
        {
            ((SoundData*)sound)->Stop(fadeOutDuration);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Verbose(e, "[MidiEditorPreview] Failed to stop preview sound.");
        }
    }

    private void StopAllSounds()
    {
        foreach (var playbackState in trackPlaybackStates)
        {
            StopSound(playbackState.CurrentSound, 50);
            playbackState.HeldNotes.Clear();
            playbackState.CurrentSound = 0;
            playbackState.CurrentNote = null;
        }
    }

    private void ResetProgramStates()
    {
        foreach (var trackState in trackStates)
            Array.Clear(trackState.ChannelPrograms);
    }

    private void ApplyProgramStateAt(double seconds)
    {
        foreach (var previewEvent in events)
        {
            if (previewEvent.TimeSeconds > seconds)
                break;

            if (previewEvent.Event is ProgramChangeEvent programChange &&
                (uint)previewEvent.TrackIndex < (uint)trackStates.Length)
            {
                trackStates[previewEvent.TrackIndex].ChannelPrograms[previewEvent.Channel] = programChange.ProgramNumber;
            }
        }
    }

    private int FindFirstEventAtOrAfter(double seconds)
    {
        var lo = 0;
        var hi = events.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (events[mid].TimeSeconds < seconds)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    public void Dispose()
    {
        StopAllSounds();
        IsPlaying = false;
    }
}
