using System.Runtime.InteropServices;

using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace MidiBard.Structs;

// Client::UI::Agent::AgentPerformanceMode
[StructLayout(LayoutKind.Explicit)]
public unsafe struct AgentPerformanceMode
{
    // Agents live for the whole process lifetime, so the pointer is cached once resolved.
    // Read from the MIDI playback thread on every event, avoid resolving it each time.
    private static AgentPerformanceMode* _instance;

    public static AgentPerformanceMode* Instance()
    {
        if (_instance == null)
            _instance = (AgentPerformanceMode*)AgentModule.Instance()->GetAgentByInternalId(AgentId.PerformanceMode);
        return _instance;
    }

    // Shorthand for callers outside unsafe context.
    public static bool IsInPerformanceMode => Instance()->InPerformanceMode;
    public static int CurrentGroupTone => Instance()->GroupTone;

    public const int NoNotePressed = -100;

    // Game note number of the lowest performance key (C3). CurrentPressingNote ranges 39..75 (C3..C6),
    // while MidiBard key indexes range 0..36: keyIndex = CurrentPressingNote - KeyNoteBase.
    public const int KeyNoteBase = 39;

    [FieldOffset(0x00)] public AgentInterface AgentInterface;
    [FieldOffset(0x20)] public byte InPerformanceModeFlag;
    [FieldOffset(0x38)] public long PerformanceTimer1;
    [FieldOffset(0x40)] public long PerformanceTimer2;
    [FieldOffset(0x5C)] public int NoteOffset;
    [FieldOffset(0x60)] public int CurrentPressingNote; // NoNotePressed (-100) when no key is held
    [FieldOffset(0xFC)] public int OctaveOffset;
    [FieldOffset(0x1D8)] public int GroupTone;

    public readonly bool InPerformanceMode => InPerformanceModeFlag != 0;
    public readonly bool IsNotePressed => CurrentPressingNote != NoNotePressed;
}
