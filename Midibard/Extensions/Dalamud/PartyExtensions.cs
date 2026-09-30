using System.Collections.Generic;
using System.Linq;

using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace MidiBard.Extensions.Dalamud.Party;

public static class PartyExtensions
{
    public static IPartyMember? GetMeAsPartyMember(this IPartyList partyList) => partyList.IsInParty() ? partyList.FirstOrDefault(i => i.ContentId == DalamudApi.PlayerState.ContentId) : null;
    public static IPartyMember? GetPartyLeader(this IPartyList partyList) => partyList.IsInParty() ? partyList[(int)partyList.PartyLeaderIndex] : null;
    public static bool IsInParty(this IPartyList partyList) => Managers.PartyWatcher.IsInParty;
    public static bool IsPartyLeader(this IPartyMember member) => Managers.PartyWatcher.IsInParty && member != null && member.ContentId == Managers.PartyWatcher.CachedPartyLeaderCID;
    public static bool IsPartyLeader(this IPartyList partyList) => Managers.PartyWatcher.IsPartyLeader;
    public static IPartyMember? GetPartyMemberFromCid(this IPartyList partyList, ulong cid) => partyList.FirstOrDefault(i => i.ContentId == cid);
    public static string GetNameAndWorld(this IPartyMember member) => $"{member?.Name}·{member?.World.ValueNullable?.Name.ToDalamudString().TextValue}";

    public static (ulong Cid, string Name, string World) GetPartyMemberData(this IPartyMember member)
    {
        var name = member?.Name.ToString() ?? "";
        var world = member?.World.ValueNullable?.Name.ToDalamudString().TextValue ?? "";
        var cid = member.ContentId;

        return (cid, name, world);
    }

    public static ulong[] GetMemberCIDs(this IPartyList partyList)
    {
        var cids = new List<ulong>();
        foreach (var p in partyList)
        {
            if (p is null) continue;
            if (p.EntityId <= 0) continue;
            if (p.GameObject is null || !p.GameObject.IsValid()) continue;
            if (p.World.Value.RowId > 0 && p.Territory.Value.RowId > 0)
                cids.Add(p.ContentId);
        }
        return cids.ToArray();
    }
}
