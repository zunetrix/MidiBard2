using System;
using System.Linq;

using Dalamud.Plugin.Services;

namespace MidiBard.Managers;

public class PartyWatcher : IDisposable
{
    public long[] PartyMemberCIDs { get; private set; } = Array.Empty<long>();
    public static event EventHandler<long> PartyMemberJoin;
    public static event EventHandler<long> PartyMemberLeave;

    public PartyWatcher()
    {
        DalamudApi.Framework.Update += Framework_Update;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= Framework_Update;
        PartyMemberJoin = delegate { };
        PartyMemberLeave = delegate { };
    }

    public static long[] GetMemberCIDs()
    {
        System.Collections.Generic.List<long> cids = new();
        foreach (var p in DalamudApi.PartyList)
        {
            try
            {
                if (p.EntityId <= 0 || !p.GameObject.IsValid())
                    continue;
                if (p.World.Value.RowId > 0 && p.Territory.Value.RowId > 0)
                {
                    cids.Add(p.ContentId);
                }
            }
            catch (NullReferenceException) { }
        }
        return cids.ToArray();
    }

    private void Framework_Update(IFramework framework)
    {
        var newMemberCIDs = GetMemberCIDs();
        if (!newMemberCIDs.ToHashSet().SetEquals(PartyMemberCIDs.ToHashSet()))
        {
            //DalamudApi.PluginLog.Warning($"CHANGE {newList.Length - PartyMembers.Length}");
            //DalamudApi.PluginLog.Information("OLD:\n"+string.Join("\n", PartyMembers.Select(i=>$"{i.Name} {i.ContentId:X}")));
            //DalamudApi.PluginLog.Information("NEW:\n"+string.Join("\n", newList.Select(i=>$"{i.Name} {i.ContentId:X}")));

            foreach (var cid in newMemberCIDs)
            {
                if (!PartyMemberCIDs.Any(i => i == cid))
                {
                    DalamudApi.PluginLog.Debug($"JOIN {cid}");
                    PartyMemberJoin?.Invoke(this, cid);
                }
            }

            foreach (var partyMember in PartyMemberCIDs)
            {
                if (!newMemberCIDs.Any(i => i == partyMember))
                {
                    DalamudApi.PluginLog.Debug($"LEAVE {partyMember}");
                    PartyMemberLeave?.Invoke(this, partyMember);
                }
            }
        }

        PartyMemberCIDs = newMemberCIDs;
    }
}

