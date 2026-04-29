using System.Collections.Generic;

namespace MidiBard.Managers;

internal class DbTrack
{
    public int Index;
    public bool Enabled = true;
    public string Name;
    public int Transpose;
    public uint Instrument;
    public List<ulong> AssignedCids = new List<ulong>();
}

// internal class DbChannel
// {
//     public int Transpose;
//     public int Instrument;
//     public List<long> AssignedCids = new List<long>();
// }
