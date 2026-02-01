using System;
using System.Runtime.InteropServices;

namespace MidiBard.Managers.Agents;

public class AgentInterface
{
    public IntPtr Pointer { get; }
    public IntPtr VTable { get; }
    //public FFXIVClientStructs.FFXIV.Component.GUI.AgentInterface* Struct => (FFXIVClientStructs.FFXIV.Component.GUI.AgentInterface*)Pointer;

    public AgentInterface(IntPtr pointer)
    {
        Pointer = pointer;
        VTable = Marshal.ReadIntPtr(Pointer);
    }

    public override string ToString()
    {
        return $"{(long)Pointer:X} {(long)VTable:X}";
    }
}
