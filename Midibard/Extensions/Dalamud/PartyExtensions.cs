using System.Linq;

using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace MidiBard.Extensions.Dalamud.Party;

public static class PartyExtensions
{
    public static IPartyMember? GetMeAsPartyMember(this IPartyList partyList) => partyList.IsInParty() ? partyList.FirstOrDefault(i => i.ContentId == (long)DalamudApi.PlayerState.ContentId) : null;
    public static IPartyMember? GetPartyLeader(this IPartyList partyList) => partyList.IsInParty() ? partyList[(int)partyList.PartyLeaderIndex] : null;
    public static bool IsInParty(this IPartyList partyList) => partyList?.Length > 1;
    public static bool IsPartyLeader(this IPartyMember member) => DalamudApi.PartyList.IsInParty() && member != null && member.ContentId == DalamudApi.PartyList.GetPartyLeader()?.ContentId;
    public static bool IsPartyLeader(this IPartyList partyList) => partyList.IsInParty() && (long)DalamudApi.PlayerState.ContentId == partyList.GetPartyLeader()?.ContentId;
    public static IPartyMember? GetPartyMemberFromCid(this IPartyList partyList, long cid) => partyList.FirstOrDefault(i => i.ContentId == cid);
    public static string NameAndWorld(this IPartyMember member) => $"{member?.Name}·{member?.World.ValueNullable?.Name.ToDalamudString().TextValue}";

    public static (long Cid, string Name, string World) GetPartyMemberData(this IPartyMember member)
    {
        var name = member?.Name.ToString() ?? "";
        var world = member?.World.ValueNullable?.Name.ToDalamudString().TextValue ?? "";
        var cid = member.ContentId;

        return (cid, name, world);
    }
}
