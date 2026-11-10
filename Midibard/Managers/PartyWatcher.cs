using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace MidiBard.Managers;

public class PartyWatcher : IDisposable
{
    /// <summary>
    /// Snapshot of a party member's data at the time of the last update.
    /// </summary>
    public record PartyMemberInfo(
        string Name, uint EntityId, ulong ContentId,
        uint WorldId, string World, uint ClassJobId, byte Level);

    private PartyMemberInfo[] _partyMembers = Array.Empty<PartyMemberInfo>();
    private ulong[] _partyMemberCIDs = Array.Empty<ulong>();
    private bool _isInParty;
    private bool _isPartyLeader;
    private ulong _cachedPartyLeaderCID;
    private volatile bool _needsUpdate = true;

    /// <summary>
    /// Rich member data for each party member.
    /// </summary>
    public static IReadOnlyList<PartyMemberInfo> PartyMembers => Volatile.Read(ref Plugin.PartyWatcher._partyMembers);

    /// <summary>
    /// Content IDs of all party members. Used extensively by ensemble and config systems.
    /// </summary>
    public static ulong[] PartyMemberCIDs => Volatile.Read(ref Plugin.PartyWatcher._partyMemberCIDs);

    /// <summary>
    /// Whether the local player is currently in a party (2+ members).
    /// </summary>
    public static bool IsInParty => Volatile.Read(ref Plugin.PartyWatcher._isInParty);

    /// <summary>
    /// Whether the local player is the party leader.
    /// </summary>
    public static bool IsPartyLeader => Volatile.Read(ref Plugin.PartyWatcher._isPartyLeader);

    /// <summary>
    /// Content ID of the current party leader, or 0 if not in a party.
    /// </summary>
    public static ulong CachedPartyLeaderCID => Volatile.Read(ref Plugin.PartyWatcher._cachedPartyLeaderCID);

    /// <summary>
    /// Raised when a new member joins the party.
    /// </summary>
    public event Action<PartyMemberInfo>? MemberJoined;

    /// <summary>
    /// Raised when a member leaves the party. Provides the Content ID of the departed member.
    /// </summary>
    public event Action<ulong>? MemberLeft;

    private static readonly ushort[] PartyEventMessageIds =
    {
        60,  // join party (others)
        61,  // join party (leader)
        64,  // now the party leader
        65,  // has promoted you to party leader
        66,  // the new party leader
        68,  // leave party (leader)
        69,  // left party (others)
        70,  // You leave your party
        71,  // left the party / promoted to party leader
        72,  // dissolve party (leader)
        73,  // party dissolved (others)
    };

    public PartyWatcher()
    {
        DalamudApi.Framework.Update += Framework_Update;
        DalamudApi.ClientState.Login += OnLogin;
        DalamudApi.ChatGui.LogMessage += ChatOnLogMessage;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= Framework_Update;
        DalamudApi.ClientState.Login -= OnLogin;
        DalamudApi.ChatGui.LogMessage -= ChatOnLogMessage;
    }

    /// <summary>
    /// Marks the cached party data as stale so it will be refreshed on the next framework tick.
    /// </summary>
    public void Invalidate() => _needsUpdate = true;

    /// <summary>
    /// Requests a refresh of the cached party data from any context.
    /// </summary>
    public static void RequestRefresh() => Plugin.PartyWatcher?.Invalidate();

    private void OnLogin() => _needsUpdate = true;

    private void ChatOnLogMessage(ILogMessage message)
    {
        if (PartyEventMessageIds.Contains((ushort)message.LogMessageId))
            _needsUpdate = true;
    }

    private void Framework_Update(IFramework framework)
    {
        if (!_needsUpdate)
            return;

        // Build rich member list
        var newMembers = new List<PartyMemberInfo>();
        var newCIDList = new List<ulong>();
        foreach (var m in DalamudApi.PartyList)
        {
            if (m is null) continue;
            var cid = m.ContentId;
            if (cid == 0) continue;
            newMembers.Add(new PartyMemberInfo(
                m.Name.TextValue,
                m.EntityId,
                cid,
                m.World.RowId,
                m.World.ValueNullable?.Name.ToDalamudString().TextValue ?? "",
                m.ClassJob.RowId,
                m.Level));
            newCIDList.Add(cid);
        }

        var newCIDs = newCIDList.ToArray();

        // Guard against partial data: if the party list reports members but
        // CIDs haven't resolved yet, defer the update to avoid a false empty snapshot.
        if (DalamudApi.PartyList.Length > 0 && newCIDs.Length == 0)
            return;

        _needsUpdate = false;

        // Detect joins and leaves
        var oldCIDSet = new HashSet<ulong>(_partyMemberCIDs);
        var newCIDSet = new HashSet<ulong>(newCIDs);

        foreach (var member in newMembers)
        {
            if (!oldCIDSet.Contains(member.ContentId))
                MemberJoined?.Invoke(member);
        }

        foreach (var cid in _partyMemberCIDs)
        {
            if (!newCIDSet.Contains(cid))
                MemberLeft?.Invoke(cid);
        }

        // Update state with Volatile writes for cross-thread visibility
        bool isInParty = DalamudApi.PartyList.Length > 1;
        ulong leaderCid = isInParty
            ? (DalamudApi.PartyList[(int)DalamudApi.PartyList.PartyLeaderIndex]?.ContentId ?? 0)
            : 0;

        Volatile.Write(ref _partyMembers, newMembers.ToArray());
        Volatile.Write(ref _partyMemberCIDs, newCIDs);
        Volatile.Write(ref _isInParty, isInParty);
        Volatile.Write(ref _cachedPartyLeaderCID, leaderCid);
        Volatile.Write(ref _isPartyLeader,
            isInParty && DalamudApi.PlayerState.ContentId == leaderCid);
    }
}
