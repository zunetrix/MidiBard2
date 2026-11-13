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
#pragma warning restore CS0649

    private static Signatures _sigs = null!;

    public static nint PerformanceStructPtr => _sigs.PerformanceStructPtr;
    public static nint DoPerformAction => _sigs.DoPerformAction;
    public static nint NetworkEnsembleStart => _sigs.NetworkEnsembleStart;
    public static nint EnsembleReceivedHandler => _sigs.EnsembleReceivedHandler;

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
    }
}
