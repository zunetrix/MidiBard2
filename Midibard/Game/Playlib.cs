using System;

using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MidiBard;

public static class Playlib
{
    private static unsafe IntPtr GetWindowByName(string s) => (IntPtr)AtkStage.Instance()->RaptureAtkUnitManager->GetAddonByName(s);

    // [Signature("83 FA 04 77 4E", ScanType = ScanType.Text, UseFlags = SignatureUseFlags.Pointer)]
    // private static unsafe delegate* unmanaged<IntPtr, uint, void> SetToneUI;

    // public static unsafe void init()
    // {
    //     var mainModule = Process.GetCurrentProcess().MainModule;
    //     var mainModuleBaseAddress = mainModule.BaseAddress;
    //     //SignatureHelper.Initialise(new playlib());
    //     var scan = SigScanner.Scan(mainModuleBaseAddress, mainModule.ModuleMemorySize, "83 FA 04 77 4E");
    //     SetToneUI = (delegate* unmanaged<IntPtr, uint, void>)scan;
    // }

    public static void SendAction(nint ptr, params ulong[] param)
    {
        if (param.Length % 2 != 0) throw new ArgumentException("The parameter length must be an integer multiple of 2.");
        if (ptr == IntPtr.Zero) throw new ArgumentException("input pointer is null");
        uint paircount = (uint)param.Length / 2;
        unsafe
        {
            fixed (ulong* u = param)
            {
                AtkUnitBase.MemberFunctionPointers.FireCallback((AtkUnitBase*)ptr, paircount, (AtkValue*)u, true);
            }
        }
    }

    public static bool SendAction(string name, params ulong[] param)
    {
        var ptr = GetWindowByName(name);
        if (ptr == IntPtr.Zero) return false;
        SendAction(ptr, param);
        return true;
    }

    private const ulong ActionType = 3;
    private const ulong ActionPress = 1;
    private const ulong ActionRelease = 2;
    private const ulong ParamKey = 4;

    public static bool PressKey(int keynumber, ref int offset, ref int octave)
    {
        if (TargetWindowPtr(out var miniMode, out var targetWindowPtr))
        {
            int newOffset = 0, newOctave = 0;

            if (miniMode)
                keynumber = ConvertMiniKeyNumber(keynumber, ref newOffset, ref newOctave);

            offset = newOffset;
            octave = newOctave;
            SendAction(targetWindowPtr, ActionType, ActionPress, ParamKey, (ulong)keynumber);
            return true;
        }
        return false;
    }

    public static bool ReleaseKey(int keynumber)
    {
        if (TargetWindowPtr(out var miniMode, out var targetWindowPtr))
        {
            if (miniMode) keynumber = ConvertMiniKeyNumber(keynumber);

            SendAction(targetWindowPtr, ActionType, ActionRelease, ParamKey, (ulong)keynumber);
            return true;
        }

        return false;
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

    private static unsafe bool TargetWindowPtr(out bool miniMode, out IntPtr targetWindowPtr)
    {
        targetWindowPtr = GetWindowByName("PerformanceModeWide");
        if (targetWindowPtr != IntPtr.Zero && ((AtkUnitBase*)targetWindowPtr)->IsVisible)
        {
            miniMode = false;
            return true;
        }

        targetWindowPtr = GetWindowByName("PerformanceMode");
        if (targetWindowPtr != IntPtr.Zero && ((AtkUnitBase*)targetWindowPtr)->IsVisible)
        {
            miniMode = true;
            return true;
        }

        miniMode = false;
        return false;
    }

    public static bool GuitarSwitchTone(int tone)
    {
        var ptr = GetWindowByName("PerformanceToneChange");
        if (ptr == IntPtr.Zero) return false;

        SendAction(ptr, ActionType, 0, 3, (ulong)tone);
        //SetToneUI(ptr, (uint)tone);
        return true;
    }

    public static bool BeginReadyCheck() => SendAction("PerformanceMetronome", 3, 2, 2, 0);
    public static bool ConfirmBeginReadyCheck() => SendAction("PerformanceReadyCheck", 3, 2);
    public static bool ConfirmReceiveReadyCheck() => SendAction("PerformanceReadyCheckReceive", 3, 2);
}
