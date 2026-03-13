using System;
using System.Collections.Generic;
using System.Linq;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

using MidiBard.Managers.Agents;
using MidiBard.Util;

namespace MidiBard.Control;

public class BardPlayDevice : IOutputDevice
{
    private Plugin Plugin { get; }
    private (MidiPlaybackMetaData metadata, int delayms) lastnoteon;
    // private (MidiPlaybackMetaData metadata, int delayms) lastnoteon = (new MidiPlaybackMetaData(this, -1, -1, -1), 0);
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
    private readonly List<(MidiEvent, MidiPlaybackMetaData)>[] MidiEventsBuffer;
    const int BufferLength = 500;

    public BardPlayDevice(Plugin plugin)
    {
        Plugin = plugin;

        lastnoteon = (
            new MidiPlaybackMetaData(this, -1, -1, -1),
            0
        );

        Channels = new ChannelState[16];
        CurrentChannel = FourBitNumber.MinValue;
        MidiEventsBuffer = new List<(MidiEvent, MidiPlaybackMetaData)>[BufferLength];
        for (var i = 0; i < MidiEventsBuffer.Length; i++)
        {
            MidiEventsBuffer[i] = new List<(MidiEvent, MidiPlaybackMetaData)>();
        }

        PlaybackTicker = new MidiClock(false, new HighPrecisionTickGenerator(), TimeSpan.FromMilliseconds(1));
        PlaybackTicker.Ticked += PlaybackTickerTicked;
        PlaybackTicker.Restart();
    }

    private long CurrentBufferIndex;
    private List<(MidiEvent, MidiPlaybackMetaData)> NotesCurrentTick => MidiEventsBuffer[CurrentBufferIndex];

    private void PlaybackTickerTicked(object sender, EventArgs e)
    {
        if (IsDisposed) return;
        try
        {
            foreach (var (midiEvent, (device, trackIndex, time, eventValue)) in NotesCurrentTick.OrderBy(i => i.Item2.EventValueTransposed))
            {
                try
                {
                    // Actually Play event
                    // DalamudApi.PluginLog.Verbose($"[MidiClockTick] buffer: {CurrentBufferIndex} remain: {NotesCurrentTick.Count} {midiEvent} T{trackIndex}");
                    PlayMidiEvent(midiEvent, trackIndex, false);
                }
                catch (Exception exception)
                {
                    DalamudApi.PluginLog.Error(exception, "exception in dequeue tick method");
                }
            }
        }
        catch (Exception exception)
        {
            DalamudApi.PluginLog.Error(exception, "error when dequeuing midi event");
        }

        NotesCurrentTick.Clear();

        CurrentBufferIndex++;
        if (CurrentBufferIndex >= BufferLength)
        {
            CurrentBufferIndex = 0;
        }
    }

    //int GetNoteDelay(int instrument, int noteNumber)
    //{
    //    if (noteNumber == -1)
    //    {
    //        return EnsembleManager.GetCompensationNew(instrument, noteNumber);
    //    }

    //    var instrumentDelayFromConfig = 0; //switch ... blahblahblah
    //    return instrumentDelayFromConfig;
    //}

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
                //same track and same time
                if (metadata.TrackIndex == lastnoteon.metadata.TrackIndex && metadata.Time == lastnoteon.metadata.Time)
                {
                    var eventValueTransposed = metadata.EventValueTransposed;
                    var lastEventValueTransposed = lastnoteon.metadata.EventValueTransposed;
                    DalamudApi.PluginLog.Debug($"chord note t{metadata.Time,6}/{lastnoteon.metadata.Time,-6} noteNumber:{noteOn.NoteNumber} delay:{delayMs}/{lastnoteon.delayms} eventValue:{eventValueTransposed}/{lastEventValueTransposed}");
                    //new note delay is > previous delay
                    if (delayMs < lastnoteon.delayms && eventValueTransposed > lastEventValueTransposed
                        || delayMs > lastnoteon.delayms && eventValueTransposed < lastEventValueTransposed)
                    {
                        //new note is lower than previous note
                        DalamudApi.PluginLog.Warning($"correct delayms from {delayMs} -> {lastnoteon.delayms}");
                        delayMs = lastnoteon.delayms;
                    }
                }
                lastnoteon = (metadata, delayMs);
            }
        }

        var delayedBufferIndex = (CurrentBufferIndex + delayMs + 1) % BufferLength;

        // DalamudApi.PluginLog.Verbose($"[enqueue] ti{metadata.Time} dt{midiEvent.DeltaTime} event {midiEvent} to: {CurrentBufferIndex}+{delayMs}={delayedBufferIndex} ({EnsembleManager.CompensationMax - delayMs})");
        MidiEventsBuffer[delayedBufferIndex].Add((midiEvent, metadata));
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

    public void SendEventWithMetadata(MidiEvent midiEvent, object metadata)
    {
        if (IsDisposed) return;
        if (!AgentManager.AgentPerformance.InPerformanceMode) return;

        switch (metadata)
        {
            case MidiDeviceMetaData:
                {
                    PlayMidiEvent(midiEvent, 0, true);
                    return;
                }
            case MidiPlaybackMetaData midiPlaybackMeta:
                {
                    if (Plugin.CurrentBardPlayback.TrackInfos[midiPlaybackMeta.TrackIndex].IsPlaying(Plugin.Config.SoloedTrack, Plugin.Config.TrackStatus) != true) return;
                    if (Plugin.EnsembleManager.EnsembleRunning)
                    {
                        QueuePlaybackMidiEvent(midiEvent, midiPlaybackMeta);
                        return;
                    }

                    PlayMidiEvent(midiEvent, midiPlaybackMeta.TrackIndex, false);
                    break;
                }
        }
    }

    private bool PlayMidiEvent(MidiEvent midiEvent, int trackIndex, bool isDevice)
    {
        if (IsDisposed) return false;

        switch (midiEvent)
        {
            case ProgramChangeEvent programChangeEvent:
                if ((bool)(Plugin.CurrentBardPlayback?.TrackInfos[trackIndex].IsProgramElectricGuitar) && Plugin.Config.GuitarToneMode == GuitarToneMode.ProgramElectricGuitarMode)
                    Channels[programChangeEvent.Channel].Program = programChangeEvent.ProgramNumber;
                else
                    ProcessProgramChange(programChangeEvent);
                break;
            case NoteEvent noteEvent:
                var noteNum = isDevice ? GetNoteNumberTranslated(noteEvent.NoteNumber) : GetNoteNumberTranslatedByTrack(noteEvent.NoteNumber, trackIndex);
                if (noteNum is < 0 or > 36) return false;

                if (PerformanceState.PlayingGuitar)
                {
                    if (Plugin.CurrentBardPlayback.IsLoaded && (bool)(Plugin.CurrentBardPlayback?.TrackInfos[trackIndex].IsProgramElectricGuitar) && Plugin.Config.GuitarToneMode == GuitarToneMode.ProgramElectricGuitarMode)
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
        //not holding same note. skip.
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
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return;
    }

    private static bool TryGetToneFromProgram(SevenBitNumber program, out int tone)
    {
        tone = 0;
        if (!InstrumentHelper.ProgramInstruments.TryGetValue(program, out var instrumentId)) return false;
        var instrument = InstrumentHelper.Instruments[instrumentId];
        if (!instrument.IsGuitar) return false;
        tone = instrument.GuitarTone;
        return true;
    }

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
