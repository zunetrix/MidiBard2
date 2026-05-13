using System;
using System.Collections.Generic;

namespace MidiBard.Control.MidiControl.Preview;

internal sealed class PerformanceSampleProbeStore
{
    internal const int DefaultMaxEntries = 500;

    private readonly int maxEntries;
    private readonly List<PerformanceSampleProbeEntry> entries = new();

    public PerformanceSampleProbeStore(int maxEntries = DefaultMaxEntries)
    {
        this.maxEntries = Math.Max(1, maxEntries);
    }

    public IReadOnlyList<PerformanceSampleProbeEntry> Entries
    {
        get
        {
            lock (entries)
                return entries.ToArray();
        }
    }

    public void Capture(PerformanceSampleProbeEntry entry)
    {
        if (entry.InstrumentId == 0 || !PerformanceSampleCatalog.IsPerformanceInstrumentPath(entry.Path))
            return;

        lock (entries)
        {
            entries.Add(entry);

            if (entries.Count > maxEntries)
                entries.RemoveRange(0, entries.Count - maxEntries);
        }
    }

    public void Clear()
    {
        lock (entries)
            entries.Clear();
    }
}
