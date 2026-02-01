namespace MidiBard;

//unsafe class AgentManager
//{
//    internal List<AgentInterface> AgentTable { get; } = new List<AgentInterface>(400);

//    private AgentManager()
//    {
//        try
//        {
//            unsafe
//            {
//                var instance = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
//                var agentModule = instance->UIModule->GetAgentModule();
//                var i = 0;
//                foreach (var pointer in agentModule->Agents)
//                {
//                    AgentTable.Add(new AgentInterface((IntPtr)pointer.Value));
//                }
//            }
//        }
//        catch (Exception e)
//        {
//            DalamudApi.PluginLog.Error(e.ToString());
//        }
//    }

//    public static AgentManager Instance { get; } = new AgentManager();

//    internal AgentInterface FindAgentInterfaceById(int id) => AgentTable[id];

//    internal AgentInterface FindAgentInterfaceByVtable(IntPtr vtbl) => AgentTable.First(i=>i.VTable == vtbl);
//}

//public unsafe class AgentInterface<T> where T : unmanaged
//{
//    public T* Pointer { get; }
//    public void** Vtable { get; }

//    public AgentInterface(IntPtr pointer) : base(pointer)
//    {
//        Pointer = (T*)pointer;
//        Vtable = &(IntPtr*)Pointer;
//    }
//}

//unsafe class AgentPerformance : AgentInterface<AgentPerformance>
//{
//    public AgentPerformance(IntPtr pointer) : base(pointer)
//    {

//    }
//}

//[StructLayout(LayoutKind.Explicit)]
//public struct AgentPerformance
//{
//    [FieldOffset(0x1b0)] public int CurrentGroupTone;
//    [FieldOffset(0x20)] public int InPerformanceMode;
//    [FieldOffset(0x60)] public byte notePressed;
//    [FieldOffset(0x38)] public long PerformanceTimer1;
//    [FieldOffset(0x40)] public long PerformanceTimer2;
//}
