using System;
using System.Collections.Generic;
using System.Linq;
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

internal sealed class TimerMidiEditorPreviewScheduler : IMidiEditorPreviewScheduler, IDisposable
{
    private readonly object sync = new();
    private readonly List<ScheduledAction> actions = new();
    private Timer? timer;
    private long nextSequence;
    private bool disposed;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var delayMs = Math.Max(0, (long)Math.Round(delay.TotalMilliseconds));
            var scheduled = new ScheduledAction(
                this,
                Environment.TickCount64 + delayMs,
                nextSequence++,
                callback);
            actions.Add(scheduled);
            ArmTimerLocked();
            return scheduled;
        }
    }

    private void TimerFired(object? state)
    {
        List<ScheduledAction> dueActions;
        lock (sync)
        {
            if (disposed)
                return;

            var nowMs = Environment.TickCount64;
            dueActions = new List<ScheduledAction>();
            for (var i = actions.Count - 1; i >= 0; i--)
            {
                var action = actions[i];
                if (action.Cancelled)
                {
                    actions.RemoveAt(i);
                    continue;
                }

                if (action.DueMs <= nowMs)
                {
                    actions.RemoveAt(i);
                    dueActions.Add(action);
                }
            }

            dueActions.Sort(static (a, b) =>
            {
                var dueCompare = a.DueMs.CompareTo(b.DueMs);
                return dueCompare != 0 ? dueCompare : a.Sequence.CompareTo(b.Sequence);
            });
            ArmTimerLocked();
        }

        foreach (var action in dueActions)
            action.InvokeIfNotCancelled();
    }

    private void Cancel(ScheduledAction action)
    {
        lock (sync)
        {
            action.Cancel();
            if (disposed)
                return;

            actions.Remove(action);
            ArmTimerLocked();
        }
    }

    private void ArmTimerLocked()
    {
        actions.RemoveAll(static action => action.Cancelled);

        if (actions.Count == 0)
        {
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var nextDueMs = actions.Min(static action => action.DueMs);
        var delayMs = Math.Max(0, nextDueMs - Environment.TickCount64);
        timer ??= new Timer(TimerFired);
        timer.Change(TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;

            disposed = true;
            actions.Clear();
            timer?.Dispose();
            timer = null;
        }
    }

    private sealed class ScheduledAction(
        TimerMidiEditorPreviewScheduler owner,
        long dueMs,
        long sequence,
        Action callback) : IDisposable
    {
        private int cancelled;

        public long DueMs { get; } = dueMs;
        public long Sequence { get; } = sequence;
        public bool Cancelled => Volatile.Read(ref cancelled) != 0;

        public void InvokeIfNotCancelled()
        {
            if (Interlocked.Exchange(ref cancelled, 1) == 0)
            {
                callback();
            }
        }

        public void Cancel()
            => Interlocked.Exchange(ref cancelled, 1);

        public void Dispose()
            => owner.Cancel(this);
    }
}

internal sealed class PluginMidiEditorPreviewCompensationProvider(Plugin plugin) : IMidiEditorPreviewCompensationProvider
{
    public int GetCompensationMs(uint instrumentId, int gameNote)
        => plugin.EnsembleManager.GetCompensationNew((int)instrumentId, gameNote);
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
            instrumentId > 0;
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
