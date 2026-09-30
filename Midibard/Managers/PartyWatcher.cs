using System;
using System.Linq;

using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.Managers;

public class PartyWatcher : IDisposable
{
    public ulong[] PartyMemberCIDs { get; private set; } = Array.Empty<ulong>();
    public static ulong[] CachedPartyMemberCIDs { get; private set; } = Array.Empty<ulong>();

    public static bool IsInParty { get; private set; }
    public static bool IsPartyLeader { get; private set; }
    public static ulong CachedPartyLeaderCID { get; private set; }

    private const ushort LogMessageIdJoinParty = 60;
    private const ushort LogMessageIdLeaveParty = 69;
    private bool _needsUpdate = true;

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

    private void OnLogin() => _needsUpdate = true;
    private void ChatOnLogMessage(ILogMessage message)
    {
        if (message.LogMessageId == LogMessageIdJoinParty || message.LogMessageId == LogMessageIdLeaveParty)
        {
            _needsUpdate = true;
        }
    }

    private void Framework_Update(IFramework framework)
    {
        if (!_needsUpdate)
            return;

        var newCIDs = DalamudApi.PartyList.GetMemberCIDs();

        if (DalamudApi.PartyList.Length > 0 && newCIDs.Length == 0)
            return;

        _needsUpdate = false;

        PartyMemberCIDs = newCIDs;
        CachedPartyMemberCIDs = newCIDs;

        IsInParty = DalamudApi.PartyList.Length > 1;
        CachedPartyLeaderCID = IsInParty ? (DalamudApi.PartyList[(int)DalamudApi.PartyList.PartyLeaderIndex]?.ContentId ?? 0) : 0;
        IsPartyLeader = IsInParty && DalamudApi.PlayerState.ContentId == CachedPartyLeaderCID;
    }
}
