using System;

using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MidiBard;

// Drives the game's performance UI by firing addon callbacks
public static unsafe class Playlib
{
    // Callback values understood by PerformanceMode / PerformanceModeWide.
    private const int KeyPress = 1;
    private const int KeyRelease = 2;

    private static AtkUnitBase* GetAddon(ReadOnlySpan<byte> name)
        => RaptureAtkModule.Instance()->RaptureAtkUnitManager.GetAddonByName(name);

    private static AtkUnitBase* GetAddon(string name)
        => RaptureAtkModule.Instance()->RaptureAtkUnitManager.GetAddonByName(name);

    private static AtkValue Int(int value) => new() { Type = AtkValueType.Int, Int = value };
    private static AtkValue Int64(long value) => new() { Type = AtkValueType.Int64, Int64 = value };
    private static AtkValue Bool(bool value) => new() { Type = AtkValueType.Bool, Bool = value };

    public static void FireCallback(AtkUnitBase* addon, ReadOnlySpan<AtkValue> values)
    {
        if (addon == null) throw new ArgumentException("addon pointer is null", nameof(addon));

        fixed (AtkValue* valuesPtr = values)
        {
            addon->FireCallback((uint)values.Length, valuesPtr, true);
        }
    }

    public static bool FireCallback(string addonName, ReadOnlySpan<AtkValue> values)
    {
        var addon = GetAddon(addonName);
        if (addon == null) return false;
        FireCallback(addon, values);
        return true;
    }

    public static bool PressKey(int keynumber, ref int offset, ref int octave)
    {
        if (!TryGetPerformanceWindow(out var miniMode, out var addon))
            return false;

        int newOffset = 0, newOctave = 0;

        if (miniMode)
            keynumber = ConvertMiniKeyNumber(keynumber, ref newOffset, ref newOctave);

        offset = newOffset;
        octave = newOctave;
        FireCallback(addon, [Int(KeyPress), Int64(keynumber)]);
        return true;
    }

    public static bool ReleaseKey(int keynumber)
    {
        if (!TryGetPerformanceWindow(out var miniMode, out var addon))
            return false;

        if (miniMode) keynumber = ConvertMiniKeyNumber(keynumber);

        FireCallback(addon, [Int(KeyRelease), Int64(keynumber)]);
        return true;
    }

    private static int ConvertMiniKeyNumber(int keynumber)
    {
        int offset = 0, octave = 0;
        return ConvertMiniKeyNumber(keynumber, ref offset, ref octave);
    }

    private static int ConvertMiniKeyNumber(int keynumber, ref int offset, ref int octave)
    {
        keynumber -= 12;
        switch (keynumber)
        {
            case < 0:
                keynumber += 12;
                offset = -12;
                octave = -1;
                break;
            case > 12:
                keynumber -= 12;
                offset = 12;
                octave = 1;
                break;
        }

        return keynumber;
    }

    // Prefers the full keyboard (Wide); falls back to the one-octave mini keyboard.
    private static bool TryGetPerformanceWindow(out bool miniMode, out AtkUnitBase* addon)
    {
        addon = GetAddon("PerformanceModeWide"u8);
        if (addon != null && addon->IsVisible)
        {
            miniMode = false;
            return true;
        }

        addon = GetAddon("PerformanceMode"u8);
        if (addon != null && addon->IsVisible)
        {
            miniMode = true;
            return true;
        }

        miniMode = false;
        return false;
    }

    public const float DefaultKeyboardScale = 1f;
    public const float MinKeyboardScale = 0.05f;
    public const float MaxKeyboardScale = 3f;

    // Reads the window scale of the currently open performance keyboard (Wide or mini).
    public static bool TryGetKeyboardScale(out float scale)
    {
        if (!TryGetPerformanceWindow(out _, out var addon))
        {
            scale = DefaultKeyboardScale;
            return false;
        }

        scale = addon->Scale;
        return true;

        // Alternative: read the persisted value from AddonConfig
        // Requires: using FFXIVClientStructs.FFXIV.Client.UI.Misc;
        //   TryGetPerformanceWindow(out var miniMode, out _);
        //   var entry = AddonConfig.Instance()->GetConfigEntryByAddonName(miniMode ? "PerformanceMode" : "PerformanceModeWide");
        //   if (entry != null && entry->HasValue)
        //       scale = entry->Scale;
    }

    // Sets the window scale of the currently open performance keyboard; the game persists it.
    public static bool SetKeyboardScale(float scale)
    {
        if (!TryGetPerformanceWindow(out _, out var addon))
            return false;

        addon->SetScale(Math.Clamp(scale, MinKeyboardScale, MaxKeyboardScale), true);
        return true;
    }

    public static bool GuitarSwitchTone(int tone) => FireCallback("PerformanceToneChange", [Int(0), Int(tone)]);

    public static bool BeginReadyCheck() => FireCallback("PerformanceMetronome", [Int(2), Bool(false)]);
    public static bool ConfirmBeginReadyCheck() => FireCallback("PerformanceReadyCheck", [Int(2)]);
    public static bool ConfirmReceiveReadyCheck() => FireCallback("PerformanceReadyCheckReceive", [Int(2)]);
    public static bool ConfirmSelectYesno() => FireCallback("SelectYesno", [Int(0)]);
}
