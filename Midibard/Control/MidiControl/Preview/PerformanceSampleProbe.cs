using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Hooking;
using Dalamud.Utility;

using FFXIVClientStructs.FFXIV.Client.Sound;
using InteropGenerator.Runtime;

namespace MidiBard.Control.MidiControl.Preview;

internal sealed record PerformanceSampleProbeEntry(
    DateTimeOffset TimestampUtc,
    uint InstrumentId,
    string Path,
    float Volume,
    uint FadeInDuration,
    float Speed,
    int A9,
    uint SoundNumber,
    bool AutoRelease,
    SoundVolumeCategory VolumeCategory,
    bool A13,
    int MidiNote,
    bool A15,
    bool DefaultFadeOut,
    bool IsPositional,
    bool A18);

internal sealed unsafe class PerformanceSampleProbe : IDisposable
{
    private const int MaxEntries = 500;

    private delegate SoundData* PlaySoundDelegate(
        SoundManager* soundManager,
        CStringPointer path,
        float volume,
        uint fadeInDuration,
        float posX,
        float posY,
        float posZ,
        float speed,
        int a9,
        uint soundNumber,
        bool autoRelease,
        SoundVolumeCategory volumeCategory,
        bool a13,
        int midiNote,
        bool a15,
        bool defaultFadeOut,
        bool isPositional,
        bool a18);

    private readonly List<PerformanceSampleProbeEntry> entries = new();
    private Hook<PlaySoundDelegate>? hook;
    private bool enabled;

    public bool IsEnabled => enabled;
    public string? LastError { get; private set; }

    public IReadOnlyList<PerformanceSampleProbeEntry> Entries
    {
        get
        {
            lock (entries)
                return entries.ToArray();
        }
    }

    public void Enable()
    {
        if (enabled)
            return;

        try
        {
            hook ??= DalamudApi.GameInteropProvider.HookFromAddress<PlaySoundDelegate>(
                (nint)SoundManager.MemberFunctionPointers.PlaySound,
                Detour);

            hook.Enable();
            enabled = true;
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            DalamudApi.PluginLog.Error(e, "[PerformanceSampleProbe] Failed to enable SoundManager.PlaySound hook.");
        }
    }

    public void Disable()
    {
        if (!enabled)
            return;

        hook?.Disable();
        enabled = false;
    }

    public void Clear()
    {
        lock (entries)
            entries.Clear();
    }

    public string BuildSourceRows()
        => PerformanceSampleCatalog.BuildSourceRows(Entries);

    private SoundData* Detour(
        SoundManager* soundManager,
        CStringPointer path,
        float volume,
        uint fadeInDuration,
        float posX,
        float posY,
        float posZ,
        float speed,
        int a9,
        uint soundNumber,
        bool autoRelease,
        SoundVolumeCategory volumeCategory,
        bool a13,
        int midiNote,
        bool a15,
        bool defaultFadeOut,
        bool isPositional,
        bool a18)
    {
        Capture(path, volume, fadeInDuration, speed, a9, soundNumber, autoRelease, volumeCategory, a13, midiNote, a15, defaultFadeOut, isPositional, a18);

        return hook!.Original(
            soundManager,
            path,
            volume,
            fadeInDuration,
            posX,
            posY,
            posZ,
            speed,
            a9,
            soundNumber,
            autoRelease,
            volumeCategory,
            a13,
            midiNote,
            a15,
            defaultFadeOut,
            isPositional,
            a18);
    }

    private void Capture(
        CStringPointer path,
        float volume,
        uint fadeInDuration,
        float speed,
        int a9,
        uint soundNumber,
        bool autoRelease,
        SoundVolumeCategory volumeCategory,
        bool a13,
        int midiNote,
        bool a15,
        bool defaultFadeOut,
        bool isPositional,
        bool a18)
    {
        try
        {
            var pathText = path.ExtractText();
            if (!PerformanceSampleCatalog.IsPerformanceInstrumentPath(pathText))
                return;

            var instrumentId = GetCurrentInstrumentId();
            lock (entries)
            {
                entries.Add(new PerformanceSampleProbeEntry(
                    DateTimeOffset.UtcNow,
                    instrumentId,
                    pathText,
                    volume,
                    fadeInDuration,
                    speed,
                    a9,
                    soundNumber,
                    autoRelease,
                    volumeCategory,
                    a13,
                    midiNote,
                    a15,
                    defaultFadeOut,
                    isPositional,
                    a18));

                if (entries.Count > MaxEntries)
                    entries.RemoveRange(0, entries.Count - MaxEntries);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Verbose(e, "[PerformanceSampleProbe] Failed to capture PlaySound call.");
        }
    }

    private static uint GetCurrentInstrumentId()
    {
        try
        {
            var instrument = PerformanceState.CurrentInstrumentWithTone;
            return instrument > 0 ? (uint)instrument : 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        Disable();
        hook?.Dispose();
        hook = null;
    }
}
