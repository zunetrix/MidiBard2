using System;
using System.Collections.Generic;
using System.Reflection;

using Dalamud.Utility.Signatures;

namespace MidiBard.Managers;

// Game addresses not covered by FFXIVClientStructs, resolved via Dalamud's [Signature] scanner.
public static class Offsets
{
    // Dalamud's InitializeFromAttributes only populates instance members, so the sigs live here.
#pragma warning disable CS0649 // assigned via reflection by InitializeFromAttributes
    private sealed class Signatures
    {
        [Signature("48 8B C2 0F B6 15 ?? ?? ?? ?? F6 C2 01", ScanType = ScanType.StaticAddress, Fallibility = Fallibility.Fallible)]
        public nint PerformanceStructPtr;

        [Signature("48 89 6C 24 10 48 89 74 24 18 57 48 83 EC ?? 48 83 3D ?? ?? ?? ?? ?? 41 8B E8", Fallibility = Fallibility.Fallible)]
        public nint DoPerformAction;

        [Signature("40 53 48 83 EC 20 48 8B D9 48 83 C1 78 E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 53 20", Fallibility = Fallibility.Fallible)]
        public nint NetworkEnsembleStart;

        [Signature("4C 8B C2 8B D1 48 8D 0D ?? ?? ?? ?? E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 40 53 48 83 EC 20 48 8B D9", Fallibility = Fallibility.Fallible)]
        public nint EnsembleReceivedHandler;
    }

    // Diagnostic-only signatures: a miss disables the feature using them instead of
    // failing plugin load. Recovered from the pre-refactor DEBUG offsets (unverified
    // on the current game build - validate with the Network debug widget's Solo tab).
    private sealed class OptionalSignatures
    {
        // Builds/sends this client's solo performance packet (10 note slots).
        [Signature("4C 8B DC 49 89 6B 20 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 71 09", Fallibility = Fallibility.Fallible)]
        public nint SoloSendHandler;

        // Handles another player's solo performance packet. Generic prologue: may resolve to the wrong function.
        [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B DA 8B F9", Fallibility = Fallibility.Fallible)]
        public nint SoloReceivedHandler;
    }
#pragma warning restore CS0649

    private static Signatures _sigs = null!;
    private static OptionalSignatures _optionalSigs = new();

    public static nint PerformanceStructPtr => _sigs.PerformanceStructPtr;
    public static nint DoPerformAction => _sigs.DoPerformAction;
    public static nint NetworkEnsembleStart => _sigs.NetworkEnsembleStart;
    public static nint EnsembleReceivedHandler => _sigs.EnsembleReceivedHandler;

    /// <summary>0 when the signature was not found.</summary>
    public static nint SoloSendHandler => _optionalSigs.SoloSendHandler;

    /// <summary>0 when the signature was not found.</summary>
    public static nint SoloReceivedHandler => _optionalSigs.SoloReceivedHandler;

    // Offset of the equipped instrument byte inside the performance struct (PerformanceStructPtr + 3).
    public const byte InstrumentOffset = 0x14;

    // Resolves all signatures; throws listing every sig that could not be found.
    public static void Setup()
    {
        var sigs = new Signatures();
        DalamudApi.GameInteropProvider.InitializeFromAttributes(sigs);

        var baseAddress = DalamudApi.SigScanner.Module.BaseAddress;
        var missing = new List<string>();
        foreach (var field in typeof(Signatures).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var address = (nint)field.GetValue(sigs)!;
            if (address == 0)
            {
                var sig = field.GetCustomAttribute<SignatureAttribute>()?.Signature;
                DalamudApi.PluginLog.Error($"[{nameof(Offsets)}][{field.Name}] failed to find sig : {sig}");
                missing.Add(field.Name);
                continue;
            }

            DalamudApi.PluginLog.Information($"[{nameof(Offsets)}][{field.Name}] +{address - baseAddress:X} {address:X}");
        }

        if (missing.Count > 0)
            throw new InvalidOperationException($"[{nameof(Offsets)}] signatures not found: {string.Join(", ", missing)}");

        _sigs = sigs;

        var optional = new OptionalSignatures();
        try
        {
            DalamudApi.GameInteropProvider.InitializeFromAttributes(optional);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Warning(e, $"[{nameof(Offsets)}] optional signature scan failed.");
        }

        foreach (var field in typeof(OptionalSignatures).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var address = (nint)field.GetValue(optional)!;
            if (address == 0)
                DalamudApi.PluginLog.Warning($"[{nameof(Offsets)}][{field.Name}] optional sig not found; feature disabled.");
            else
                DalamudApi.PluginLog.Information($"[{nameof(Offsets)}][{field.Name}] +{address - baseAddress:X} {address:X} (optional)");
        }

        _optionalSigs = optional;
    }
}
