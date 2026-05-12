using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using FFXIVClientStructs.FFXIV.Client.Sound;
using InteropGenerator.Runtime;

using Melanchall.DryWetMidi.Common;

using MidiBard.Util;

namespace MidiBard.Control.MidiControl.Preview;

internal readonly record struct PreviewSoundRequest(
    int TrackIndex,
    int Channel,
    int MidiNote,
    int GameNote,
    uint InstrumentId);

internal interface IMidiEditorPreviewSettings
{
    float PlaySpeed { get; }
    int TransposeGlobal { get; }
    bool AdaptNotesOOR { get; }
    uint DefaultInstrumentId { get; }
    bool ForceDefaultInstrument { get; }
    GuitarToneMode GuitarToneMode { get; }
    AntiStackType AntiStackType { get; }
    TrackStatus[] TrackStatus { get; }
}

internal interface IMidiEditorPreviewInstrumentCatalog
{
    uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument);
    bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId);
    bool IsGuitar(uint instrumentId);
}

internal interface IMidiEditorPreviewSoundPlayer
{
    nint Play(PreviewSoundRequest request, out string? statusMessage);
    void Stop(nint sound, uint fadeOutDuration);
}

internal interface IMidiEditorPreviewScheduler
{
    IDisposable Schedule(TimeSpan delay, Action callback);
}

internal sealed class TimerMidiEditorPreviewScheduler : IMidiEditorPreviewScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var scheduled = new ScheduledTimer(callback);
        scheduled.Start(delay);
        return scheduled;
    }

    private sealed class ScheduledTimer(Action callback) : IDisposable
    {
        private Timer? timer;
        private int disposed;

        public void Start(TimeSpan delay)
            => timer = new Timer(Invoke, null, delay, Timeout.InfiniteTimeSpan);

        private void Invoke(object? state)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            try
            {
                callback();
            }
            finally
            {
                timer?.Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                timer?.Dispose();
        }
    }
}

internal sealed class PluginMidiEditorPreviewSettings(Plugin plugin) : IMidiEditorPreviewSettings
{
    public float PlaySpeed => plugin.Config.PlaySpeed;
    public int TransposeGlobal => plugin.Config.TransposeGlobal;
    public bool AdaptNotesOOR => plugin.Config.AdaptNotesOOR;
    public uint DefaultInstrumentId => plugin.Config.DefaultInstrumentId;
    public bool ForceDefaultInstrument => plugin.Config.ForceDefaultInstrument;
    public GuitarToneMode GuitarToneMode => plugin.Config.GuitarToneMode;
    public AntiStackType AntiStackType => plugin.Config.AntiStackType;
    public TrackStatus[] TrackStatus => plugin.Config.TrackStatus;
}

internal sealed class DefaultMidiEditorPreviewInstrumentCatalog : IMidiEditorPreviewInstrumentCatalog
{
    public uint? ResolveTrackInstrument(string trackName, uint defaultInstrumentId, bool forceDefaultInstrument)
    {
        if (forceDefaultInstrument && defaultInstrumentId > 0)
            return defaultInstrumentId;

        return TrackInfo.GetInstrumentIdByName(trackName, (ushort?)defaultInstrumentId);
    }

    public bool TryResolveProgramInstrument(SevenBitNumber program, out uint instrumentId)
    {
        instrumentId = 0;
        return InstrumentHelper.ProgramInstruments.TryGetValue(program, out instrumentId) &&
            instrumentId > 0 &&
            PerformanceSampleCatalog.TryGet(instrumentId, out _);
    }

    public bool IsGuitar(uint instrumentId)
        => InstrumentHelper.IsGuitar(instrumentId);
}

internal sealed unsafe class DalamudMidiEditorPreviewSoundPlayer : IMidiEditorPreviewSoundPlayer
{
    private readonly HashSet<uint> missingSampleLogged = new();
    private readonly HashSet<uint> missingPathLogged = new();

    public nint Play(PreviewSoundRequest request, out string? statusMessage)
    {
        statusMessage = null;

        if (!PerformanceSampleCatalog.TryGet(request.InstrumentId, out var sample))
        {
            if (missingSampleLogged.Add(request.InstrumentId))
                DalamudApi.PluginLog.Warning($"[MidiEditorPreview] No performance sample definition for instrument {request.InstrumentId}.");
            statusMessage = $"No sample definition for instrument {request.InstrumentId}.";
            return 0;
        }

        if (!PerformanceSampleCatalog.TryResolvePath(sample, out var path))
        {
            if (missingPathLogged.Add(request.InstrumentId))
                DalamudApi.PluginLog.Warning($"[MidiEditorPreview] Could not resolve SCD path for {sample.InstrumentName} ({sample.FileName}). Use the Performance Sample Probe to capture the in-game path.");
            statusMessage = $"Missing sample path: {sample.FileName}";
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
                sample.GetSoundNumber(request.GameNote),
                sample.AutoRelease,
                sample.VolumeCategory,
                sample.A13,
                sample.GetMidiNote(request.GameNote),
                sample.A15,
                sample.DefaultFadeOut,
                sample.IsPositional,
                sample.A18);

            return (nint)soundData;
        }
    }

    public void Stop(nint sound, uint fadeOutDuration)
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
}
