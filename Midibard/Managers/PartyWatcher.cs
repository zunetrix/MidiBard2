using System;
using System.Collections.Generic;

using Dalamud.Plugin.Services;

namespace MidiBard.Managers;

public class PartyWatcher : IDisposable
{
    public long[] PartyMemberCIDs { get; private set; } = Array.Empty<long>();
    public event EventHandler<long>? PartyMemberJoin;
    public event EventHandler<long>? PartyMemberLeave;

    public PartyWatcher()
    {
        DalamudApi.Framework.Update += Framework_Update;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= Framework_Update;
    }

    public static long[] GetMemberCIDs()
    {
        var cids = new List<long>();
        foreach (var p in DalamudApi.PartyList)
        {
            if (p is null) continue;
            if (p.EntityId <= 0) continue;
            if (p.GameObject is null || !p.GameObject.IsValid()) continue;
            if (p.World.Value.RowId > 0 && p.Territory.Value.RowId > 0)
                cids.Add(p.ContentId);
        }
        return cids.ToArray();
    }

    private void Framework_Update(IFramework framework)
    {
        var newCIDs = GetMemberCIDs();
        var oldCIDs = PartyMemberCIDs;

        if (!SetEquals(newCIDs, oldCIDs))
        {
            foreach (var cid in newCIDs)
            {
                if (!Contains(oldCIDs, cid))
                {
                    DalamudApi.PluginLog.Debug($"JOIN {cid}");
                    PartyMemberJoin?.Invoke(this, cid);
                }
            }

            foreach (var cid in oldCIDs)
            {
                if (!Contains(newCIDs, cid))
                {
                    DalamudApi.PluginLog.Debug($"LEAVE {cid}");
                    PartyMemberLeave?.Invoke(this, cid);
                }
            }
        }

        PartyMemberCIDs = newCIDs;
    }

    private static bool Contains(long[] arr, long value)
    {
        foreach (var v in arr)
            if (v == value) return true;
        return false;
    }

    private static bool SetEquals(long[] a, long[] b)
    {
        if (a.Length != b.Length) return false;
        foreach (var v in a)
            if (!Contains(b, v)) return false;
        return true;
    }
}
