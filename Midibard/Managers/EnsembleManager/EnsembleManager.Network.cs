using System;
using System.Collections.Generic;
using System.Linq;

using MidiBard.Structs;

namespace MidiBard.Managers;

internal partial class EnsembleManager
{
    // Lightweight snapshot of one valid performer slot captured from a performance packet.
    internal readonly struct PerformerSnapshot
    {
        public readonly uint ActorId;
        public readonly byte[] Notes;   // raw NoteNumbers[60]

        public PerformerSnapshot(EnsembleCharacterData d)
        {
            ActorId = d.CharacterId;
            Notes = d.NoteNumbers ?? Array.Empty<byte>();
        }

        public int ActiveNoteCount => Notes.Count(n => n != 0xFF);
    }

    // One captured performance packet (holds only valid performer slots).
    internal readonly struct PerformancePacketSnapshot
    {
        public readonly DateTime Timestamp;
        public readonly uint SourceId;
        public readonly PerformerSnapshot[] Performers;

        public PerformancePacketSnapshot(uint sourceId, EnsemblePerformanceIpc ipc)
        {
            Timestamp = DateTime.Now;
            SourceId = sourceId;
            Performers = ipc.EnsembleCharacterDatas
                .Where(d => d.IsValid)
                .Select(d => new PerformerSnapshot(d))
                .ToArray();
        }
    }

    public bool NetworkDebugEnabled = false;
    private readonly List<PerformancePacketSnapshot> _networkDebugLog = new();
    private const int NetworkDebugMaxEntries = 100;
    public IReadOnlyList<PerformancePacketSnapshot> NetworkDebugLog => _networkDebugLog;
    public void ClearNetworkDebugLog() { lock (_networkDebugLog) _networkDebugLog.Clear(); }
}
