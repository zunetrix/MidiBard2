using System.Runtime.InteropServices;

using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace MidiBard.Structs;

// Client::UI::Agent::AgentPerformanceMetronome
[StructLayout(LayoutKind.Explicit)]
public unsafe struct AgentPerformanceMetronome
{
    // Agents live for the whole process lifetime, so the pointer is cached once resolved.
    private static AgentPerformanceMetronome* _instance;

    public static AgentPerformanceMetronome* Instance()
    {
        if (_instance == null)
            _instance = (AgentPerformanceMetronome*)AgentModule.Instance()->GetAgentByInternalId(AgentId.PerformanceMetronome);
        return _instance;
    }

    // Shorthand for callers outside unsafe context.
    public static bool IsEnsembleModeRunning => Instance()->EnsembleModeRunning;

    [FieldOffset(0x00)] public AgentInterface AgentInterface;
    [FieldOffset(0x48)] public long MetronomeTimer1;
    [FieldOffset(0x50)] public long MetronomeTimer2;
    [FieldOffset(0x60)] public long MetronomePPQN;
    [FieldOffset(0x72)] public byte MetronomeBeatsPerBar;
    [FieldOffset(0x73)] public byte MetronomeRunningFlag;
    [FieldOffset(0x78)] public int MetronomeBeatsElapsed;
    [FieldOffset(0x80)] public byte EnsembleModeRunningFlag;

    public readonly bool MetronomeRunning => MetronomeRunningFlag == 1;
    public readonly bool EnsembleModeRunning => EnsembleModeRunningFlag == 1;
}
