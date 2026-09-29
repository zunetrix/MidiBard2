using System;

using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.Managers;

public class PartyWatcher : IDisposable
{
    public ulong[] PartyMemberCIDs { get; private set; } = Array.Empty<ulong>();
    public static ulong[] CachedPartyMemberCIDs { get; private set; } = Array.Empty<ulong>();

    private const ushort LogMessageIdJoinParty = 60;
    private const ushort LogMessageIdLeaveParty = 69;
    private bool _needsUpdate = true;

    public PartyWatcher()
    {
        DalamudApi.Framework.Update += Framework_Update;
        DalamudApi.ClientState.Login += OnLogin;
        DalamudApi.ClientState.TerritoryChanged += OnTerritoryChanged;
        DalamudApi.ChatGui.LogMessage += ChatOnLogMessage;
    }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= Framework_Update;
        DalamudApi.ClientState.Login -= OnLogin;
        DalamudApi.ClientState.TerritoryChanged -= OnTerritoryChanged;
        DalamudApi.ChatGui.LogMessage -= ChatOnLogMessage;
    }

    private void OnLogin() => _needsUpdate = true;
    private void OnTerritoryChanged(uint _) => _needsUpdate = true;
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

        _needsUpdate = false;

        var newCIDs = DalamudApi.PartyList.GetMemberCIDs();
        PartyMemberCIDs = newCIDs;
        CachedPartyMemberCIDs = newCIDs;
    }
}
