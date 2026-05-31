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
        public readonly uint EntityId;
        public readonly byte[] Notes;   // raw NoteNumbers[60]

        public PerformerSnapshot(EnsembleCharacterData d)
        {
            EntityId = d.EntityId;
            Notes = d.NoteNumbers ?? Array.Empty<byte>();
        }

        // filter (0xFE) (254 note number) represent end of notes segment
        public int ActiveNoteCount => Notes.Count(n => n != 0xFF && n != 0xFE);
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
