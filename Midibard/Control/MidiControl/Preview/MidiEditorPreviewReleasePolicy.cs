using System;
using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Preview;

internal enum MidiEditorPreviewReleaseMode
{
    DynamicHold,
    NaturalOneShot,
}

internal sealed class MidiEditorPreviewReleasePolicy
{
    public const uint CleanupFadeMs = 50;
    public const uint MinimumDynamicReleaseFadeMs = 300;
    public const uint MaximumDynamicReleaseFadeMs = 1000;
    public const uint DefaultNaturalOneShotCleanupDelayMs = 2500;

    private static readonly IReadOnlySet<uint> DynamicHoldInstruments = new HashSet<uint>
    {
        5,  // Flute
        6,  // Oboe
        7,  // Clarinet
        8,  // Fife
        9,  // Panpipes
        15, // Trumpet
        16, // Trombone
        17, // Tuba
        18, // Horn
        19, // Saxophone
        20, // Violin
        21, // Viola
        22, // Cello
        23, // DoubleBass
        24, // ElectricGuitarOverdriven
        25, // ElectricGuitarClean
        27, // ElectricGuitarPowerChords
    };

    public MidiEditorPreviewReleaseMode GetReleaseMode(uint instrumentId)
        => DynamicHoldInstruments.Contains(instrumentId)
            ? MidiEditorPreviewReleaseMode.DynamicHold
            : MidiEditorPreviewReleaseMode.NaturalOneShot;

    public bool ShouldStopOnMusicalRelease(uint instrumentId)
        => GetReleaseMode(instrumentId) == MidiEditorPreviewReleaseMode.DynamicHold;

    public uint GetMusicalReleaseFadeMs(uint instrumentId, double heldSeconds)
    {
        if (!ShouldStopOnMusicalRelease(instrumentId))
            return 0;

        var heldMs = (uint)Math.Round(Math.Max(0.0, heldSeconds) * 1000.0);
        return Math.Clamp(heldMs, MinimumDynamicReleaseFadeMs, MaximumDynamicReleaseFadeMs);
    }

    public uint GetNaturalOneShotCleanupDelayMs(uint instrumentId)
        => instrumentId switch
        {
            10 => 3000, // Timpani
            11 or 12 or 13 => 1500, // Bongo, Bass Drum, Snare Drum
            14 => 5000, // Cymbal
            _ => DefaultNaturalOneShotCleanupDelayMs,
        };
}
