using System;
using System.Runtime.InteropServices;

namespace MidiBard.Managers.Agents;

public sealed unsafe class AgentPerformance : AgentInterface
{
    public AgentPerformance(AgentInterface agentInterface) : base(agentInterface.Pointer) { }
    public AgentPerformance(IntPtr ptr) : base(ptr) { }
    public static AgentPerformance Instance => Plugin.AgentPerformance;
    public AgentPerformanceStruct* Struct => (AgentPerformanceStruct*)Pointer;

    [StructLayout(LayoutKind.Explicit)]
    public struct AgentPerformanceStruct
    {
        [FieldOffset(0)] public FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentInterface AgentInterface;
        [FieldOffset(0x20)] public byte InPerformanceMode;
        [FieldOffset(0x38)] public long PerformanceTimer1;
        [FieldOffset(0x40)] public long PerformanceTimer2;
        [FieldOffset(0x5C)] public int NoteOffset;
        [FieldOffset(0x60)] public int CurrentPressingNote;
        [FieldOffset(0xFC)] public int OctaveOffset;
        [FieldOffset(0x1D8)] public int GroupTone;
    }

    internal int CurrentGroupTone => Struct->GroupTone;
    internal bool InPerformanceMode => Struct->InPerformanceMode != 0;
    internal bool notePressed => Struct->CurrentPressingNote != -100;
    internal int noteNumber => Struct->CurrentPressingNote;
    internal long PerformanceTimer1 => Struct->PerformanceTimer1;
    internal long PerformanceTimer2 => Struct->PerformanceTimer2;
}
