using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Control.MidiControl;
using MidiBard.Managers.Agents;

namespace MidiBard.Control;

public class BardPlayDevice : IOutputDevice
{
    private Plugin Plugin { get; }
    // Immutable snapshot stored as a volatile reference — CLR guarantees atomic
    // reads/writes for reference-type fields, so no lock is needed here.
    private sealed record LastNoteOnState(MidiPlaybackMetaData Metadata, int DelayMs);
    private volatile LastNoteOnState _lastNoteOn;
    public abstract record MidiEventMetaData;
    public record MidiDeviceMetaData : MidiEventMetaData;
    // public record MidiPlaybackMetaData(int TrackIndex, long Time, int EventValue) : MidiEventMetaData
    // {
    //     public int EventValueTransposed => EventValue >= 0 ? GetNoteNumberTranslatedByTrack(EventValue, TrackIndex) : EventValue;
    // }
    public record MidiPlaybackMetaData(
        BardPlayDevice Device,
        int TrackIndex,
        long Time,
        int EventValue
    ) : MidiEventMetaData
    {
        public int EventValueTransposed =>
            EventValue >= 0
                ? Device.GetNoteNumberTranslatedByTrack(EventValue, TrackIndex)
                : EventValue;
    }

    private readonly MidiClock PlaybackTicker;
    private readonly ConcurrentQueue<(MidiEvent, MidiPlaybackMetaData)>[] MidiEventsBuffer;
    const int BufferLength = 500;

    public BardPlayDevice(Plugin plugin)
    {
        Plugin = plugin;

        _lastNoteOn = new LastNoteOnState(new MidiPlaybackMetaData(this, -1, -1, -1), 0);

        Channels = new ChannelState[16];
        CurrentChannel = FourBitNumber.MinValue;
        MidiEventsBuffer = new ConcurrentQueue<(MidiEvent, MidiPlaybackMetaData)>[BufferLength];
        for (var i = 0; i < MidiEventsBuffer.Length; i++)
        {
            MidiEventsBuffer[i] = new ConcurrentQueue<(MidiEvent, MidiPlaybackMetaData)>();
        }

        PlaybackTicker = new MidiClock(false, new HighPrecisionTickGenerator(), TimeSpan.FromMilliseconds(1));
        PlaybackTicker.Ticked += PlaybackTickerTicked;
        PlaybackTicker.Restart();
    }

    private long _currentBufferIndex;
    private long CurrentBufferIndex => Interlocked.Read(ref _currentBufferIndex);

    private void PlaybackTickerTicked(object sender, EventArgs e)
    {
        if (IsDisposed) return;
        try
        {
            var idx = Interlocked.Read(ref _currentBufferIndex);
            var slot = MidiEventsBuffer[idx];

            // Ticker is the sole consumer of slot idx; enqueuers only write to
            // idx+1 or later, so no lock is needed here.
            if (!slot.IsEmpty)
            {
                // Drain the slot — TryDequeue also serves as the Clear.
                var items = new List<(MidiEvent, MidiPlaybackMetaData)>(slot.Count);
                while (slot.TryDequeue(out var item))
                    items.Add(item);

                foreach (var (midiEvent, (device, trackIndex, time, eventValue)) in items.OrderBy(i => i.Item2.EventValueTransposed))
                {
                    try
                    {
                        // DalamudApi.PluginLog.Verbose($"[MidiClockTick] buffer: {CurrentBufferIndex} {midiEvent} Track: {trackIndex}");
                        PlayMidiEvent(midiEvent, trackIndex, false);
                    }
                    catch (Exception exception)
                    {
                        DalamudApi.PluginLog.Error(exception, "exception in dequeue tick method");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            DalamudApi.PluginLog.Error(exception, "error when dequeuing midi event");
        }

        Interlocked.Increment(ref _currentBufferIndex);
        if (Interlocked.Read(ref _currentBufferIndex) >= BufferLength)
            Interlocked.Exchange(ref _currentBufferIndex, 0);
    }

    public void QueuePlaybackMidiEvent(MidiEvent midiEvent, MidiPlaybackMetaData metadata)
    {
        var trackIndex = metadata.TrackIndex;

        int delayMs;
        if (midiEvent is not NoteEvent noteEvent)
        {
            delayMs = Plugin.EnsembleManager.GetCompensationNew(PerformanceState.CurrentInstrumentWithTone, -1);
        }
        else
        {
            delayMs = Plugin.EnsembleManager.GetCompensationNew(PerformanceState.CurrentInstrumentWithTone, GetNoteNumberTranslatedByTrack(noteEvent.NoteNumber, trackIndex));

            if (midiEvent is NoteOnEvent noteOn)
            {
                // Snapshot the current state — volatile read is atomic for references.
                var prev = _lastNoteOn;
                //same track and same time (chord detection)
                if (prev.Metadata.TrackIndex == metadata.TrackIndex && prev.Metadata.Time == metadata.Time)
                {
                    var eventValueTransposed = metadata.EventValueTransposed;
                    var lastEventValueTransposed = prev.Metadata.EventValueTransposed;
                    DalamudApi.PluginLog.Debug($"Chord note t{metadata.Time,6}/{prev.Metadata.Time,-6} noteNumber:{noteOn.NoteNumber} delay:{delayMs}/{prev.DelayMs} eventValue:{eventValueTransposed}/{lastEventValueTransposed}");
                    //new note delay is > previous delay
                    if (delayMs < prev.DelayMs && eventValueTransposed > lastEventValueTransposed
                        || delayMs > prev.DelayMs && eventValueTransposed < lastEventValueTransposed)
                    {
                        //new note is lower than previous note
                        DalamudApi.PluginLog.Warning($"Correct delayms from {delayMs} -> {prev.DelayMs}");
                        delayMs = prev.DelayMs;
                    }
                }
                // Atomic reference write — no lock needed.
                _lastNoteOn = new LastNoteOnState(metadata, delayMs);
            }
        }

        // Capture the buffer index *inside* the lock so the ticker cannot
        // advance past this slot between the snapshot and the Add.
        var delayedBufferIndex = (Interlocked.Read(ref _currentBufferIndex) + delayMs + 1) % BufferLength;

        // DalamudApi.PluginLog.Verbose($"[enqueue] ti{metadata.Time} dt{midiEvent.DeltaTime} event {midiEvent} to: {CurrentBufferIndex}+{delayMs}={delayedBufferIndex} ({EnsembleManager.CompensationMax - delayMs})");
        MidiEventsBuffer[delayedBufferIndex].Enqueue((midiEvent, metadata));
    }

    private struct ChannelState
    {
        public SevenBitNumber Program { get; set; }

        public ChannelState(SevenBitNumber? program)
        {
            this.Program = program ?? SevenBitNumber.MinValue;
        }
    }

    private readonly ChannelState[] Channels;

    private FourBitNumber CurrentChannel;

    public void ResetChannelStates()
    {
        for (var i = 0; i < Channels.Length; i++)
        {
            Channels[i].Program = SevenBitNumber.MinValue;
        }
    }

    event EventHandler<MidiEventSentEventArgs>? IOutputDevice.EventSent
    {
        add { }
        remove { }
    }

    public void PrepareForEventsSending()
    {
    }

    [Obsolete("Use SendEventWithMetadata Instead", true)]
    public void SendEvent(MidiEvent midiEvent)
    {
    }

    public bool SendEventWithMetadata(MidiEvent midiEvent, object metadata)
    {
        if (IsDisposed) return false;
        if (!AgentManager.AgentPerformance.InPerformanceMode) return false;

        switch (metadata)
        {
            case MidiDeviceMetaData:
                return PlayMidiEvent(midiEvent, 0, true);

            case MidiPlaybackMetaData midiPlaybackMeta:
                // Capture once to avoid a teardown race: CurrentBardPlayback or its
                // TrackInfos can be nulled on the main thread while the playback engine
                // still fires final events (e.g. InterruptNotesOnStop NoteOffs).
                var bardPlayback = Plugin.CurrentBardPlayback;
                var trackInfos = bardPlayback?.TrackInfos;
                if (trackInfos == null) return false;
                if (trackInfos[midiPlaybackMeta.TrackIndex].IsPlaying(Plugin.Config.SoloedTrack, Plugin.Config.TrackStatus) != true)
                    return false;
                if (Plugin.EnsembleManager.EnsembleRunning)
                {
                    QueuePlaybackMidiEvent(midiEvent, midiPlaybackMeta);
                    return true;
                }
                return PlayMidiEvent(midiEvent, midiPlaybackMeta.TrackIndex, false);
        }

        return false;
    }

    private bool PlayMidiEvent(MidiEvent midiEvent, int trackIndex, bool isDevice)
    {
        if (IsDisposed) return false;

        // Snapshot to avoid teardown races between the playback thread and the main thread.
        var currentPlayback = Plugin.CurrentBardPlayback;
        var trackInfos = currentPlayback?.TrackInfos;

        switch (midiEvent)
        {
            case ProgramChangeEvent programChangeEvent:
                if (trackInfos != null && trackInfos[trackIndex].IsProgramElectricGuitar && Plugin.Config.GuitarToneMode == GuitarToneMode.ProgramElectricGuitarMode)
                    Channels[programChangeEvent.Channel].Program = programChangeEvent.ProgramNumber;
                else
                    ProcessProgramChange(programChangeEvent);
                break;
            case NoteEvent noteEvent:
                var noteNum = isDevice ? GetNoteNumberTranslated(noteEvent.NoteNumber) : GetNoteNumberTranslatedByTrack(noteEvent.NoteNumber, trackIndex);
                if (noteNum is < 0 or > 36) return false;

                if (PerformanceState.PlayingGuitar)
                {
                    if (trackInfos != null && currentPlayback.IsLoaded && trackInfos[trackIndex].IsProgramElectricGuitar && Plugin.Config.GuitarToneMode == GuitarToneMode.ProgramElectricGuitarMode)
                    {
                        ApplyToneByChannel(noteEvent.Channel);
                    }
                    else
                    {
                        switch (Plugin.Config.GuitarToneMode)
                        {
                            case GuitarToneMode.Off:
                                break;
                            case GuitarToneMode.Standard:
                            case GuitarToneMode.Simple:
                                {
                                    ApplyToneByChannel(noteEvent.Channel);
                                    break;
                                }
                            case GuitarToneMode.OverrideByTrack when !isDevice:
                                {
                                    ApplyToneByTrack(trackIndex);
                                    break;
                                }
                        }
                    }
                }

                return noteEvent switch
                {
                    NoteOnEvent => KeyDown(noteNum),
                    NoteOffEvent => KeyUp(noteNum),
                    _ => false,
                };
        }

        return false;
    }

    private static unsafe bool KeyUp(int noteNum)
    {
        var agentPerformance = AgentPerformance.Instance;
        // not holding same note skip
        if (agentPerformance.Struct->CurrentPressingNote - 39 != noteNum)
        {
            // DalamudApi.PluginLog.Verbose($"[SkipKUp] {noteNum} != {agentPerformance.Struct->CurrentPressingNote - 39}");
            return true;
        }

        // only release a key when it been pressing
        if (Playlib.ReleaseKey(noteNum))
        {
            // DalamudApi.PluginLog.Debug($"[KeyUp  ] {noteNum}");
            agentPerformance.Struct->CurrentPressingNote = -100;
            return true;
        }

        return false;
    }

    private static unsafe bool KeyDown(int noteNum)
    {
        var agentPerformance = AgentPerformance.Instance;
        //currently holding the same note?
        if (agentPerformance.noteNumber - 39 == noteNum)
        {
            // release repeated note in order to press it again
            if (Playlib.ReleaseKey(noteNum))
            {
                agentPerformance.Struct->CurrentPressingNote = -100;
                // DalamudApi.PluginLog.Verbose($"[ReKeyUp] {noteNum}");
            }
        }

        if (Playlib.PressKey(noteNum, ref agentPerformance.Struct->NoteOffset, ref agentPerformance.Struct->OctaveOffset))
        {
            agentPerformance.Struct->CurrentPressingNote = noteNum + 39;
            // DalamudApi.PluginLog.Debug($"[KeyDown] {noteNum}");
            return true;
        }

        return false;
    }

    private void ApplyToneByTrack(int trackIndex)
    {
        int tone = Plugin.Config.TrackStatus[trackIndex].Tone;
        Playlib.GuitarSwitchTone(tone);
    }

    private void ApplyToneByChannel(FourBitNumber channel)
    {
        CurrentChannel = channel;
        if (!TryGetToneFromProgram(Channels[channel].Program, out var tone)) return;
        Playlib.GuitarSwitchTone(tone);
    }

    private void ProcessProgramChange(ProgramChangeEvent programChangeEvent)
    {
        switch (Plugin.Config.GuitarToneMode)
        {
            case GuitarToneMode.Off:
                break;
            case GuitarToneMode.Standard:
                Channels[programChangeEvent.Channel].Program = programChangeEvent.ProgramNumber;
                break;
            case GuitarToneMode.Simple:
                for (var i = 0; i < Channels.Length; i++)
                {
                    Channels[i].Program = programChangeEvent.ProgramNumber;
                }
                break;
            case GuitarToneMode.OverrideByTrack:
            case GuitarToneMode.ProgramElectricGuitarMode:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return;
    }

    private static bool TryGetToneFromProgram(SevenBitNumber program, out int tone)
        => GuitarToneProgramResolver.TryResolveToneFromProgram(program, out tone);

    static string GetNoteName(NoteEvent note) => $"{note.GetNoteName().ToString().Replace("Sharp", "#")}{note.GetNoteOctave()}";

    public int GetNoteNumberTranslatedByTrack(int noteNumber, int trackIndex)
    {
        noteNumber += Plugin.Config.TrackStatus[trackIndex].Transpose;
        return GetNoteNumberTranslated(noteNumber);
    }

    private int GetNoteNumberTranslated(int noteNumber)
        => TrackInfo.TranslateNoteNumber(noteNumber, Plugin.Config.TransposeGlobal, Plugin.Config.AdaptNotesOOR);

    private bool IsDisposed;
    private void ReleaseUnmanagedResources()
    {
        PlaybackTicker.Ticked -= PlaybackTickerTicked;
        PlaybackTicker.Stop();
        PlaybackTicker.Dispose();
        IsDisposed = true;
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~BardPlayDevice()
    {
        ReleaseUnmanagedResources();
    }
}
