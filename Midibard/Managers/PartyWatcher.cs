using System;
using System.Linq;

using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.Managers;

public class PartyWatcher : IDisposable
{
    public static ulong[] PartyMemberCIDs { get; private set; } = Array.Empty<ulong>();

    public static bool IsInParty { get; private set; }
    public static bool IsPartyLeader { get; private set; }
    public static ulong CachedPartyLeaderCID { get; private set; }

    private static readonly ushort[] PartyEventMessageIds =
    {
        60, // join party (others)
        61, // join party (leader)
        68, // leave party (leader)
        69, // left party (others)
        72, // dissolve party (leader)
        73, // party dissolved (others)
    };
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
        // DalamudApi.PluginLog.Warning($"LogMessage: {message.LogMessageId} -> {message.GameData.Value.Text}");

        if (PartyEventMessageIds.Contains((ushort)message.LogMessageId))
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

        IsInParty = DalamudApi.PartyList.Length > 1;
        CachedPartyLeaderCID = IsInParty ? (DalamudApi.PartyList[(int)DalamudApi.PartyList.PartyLeaderIndex]?.ContentId ?? 0) : 0;
        IsPartyLeader = IsInParty && DalamudApi.PlayerState.ContentId == CachedPartyLeaderCID;
    }
}
