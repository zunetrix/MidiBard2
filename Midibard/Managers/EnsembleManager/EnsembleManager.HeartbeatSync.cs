using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Dalamud.Utility;

using MidiBard.Structs;

namespace MidiBard.Managers;

// Triggers playback start from the game's performance broadcast packet (~3s heartbeat,
// sent zone-wide) instead of the party-only NetworkEnsembleStart packet.
// Allows groups that span multiple parties or include players outside any party to sync.
internal partial class EnsembleManager
{
    // Heartbeat-sync state: when armed, the next incoming performance packet triggers DoPlay.
    // targetActorId = 0 means "accept first packet from any valid performer".
    private volatile bool _heartbeatSyncArmed = false;
    private uint _heartbeatSyncTargetEntityId = 0;
    public bool HeartbeatSyncArmed => _heartbeatSyncArmed;

    private long HandlePerformancePacket(uint sourceId, IntPtr data)
    {
        var result = _ensemblePerformanceHook.Original(sourceId, data);
        try
        {
            bool needParse = (_heartbeatSyncArmed && Plugin.Config.UseHeartbeatSync) || NetworkDebugEnabled;
            if (!needParse) return result;

            var ipc = Marshal.PtrToStructure<EnsemblePerformanceIpc>(data);

            if (_heartbeatSyncArmed && Plugin.Config.UseHeartbeatSync)
            {
                var ids = ipc.Ids;
                bool leaderPresent = _heartbeatSyncTargetEntityId == 0
                    ? ids.Length > 0
                    : ids.Contains(_heartbeatSyncTargetEntityId);

                if (leaderPresent)
                {
                    _heartbeatSyncArmed = false;
                    _heartbeatSyncTargetEntityId = 0;
                    _ = DalamudApi.Framework.RunOnFrameworkThread(StartHeartbeatSync);
                }
            }

            if (NetworkDebugEnabled)
            {
                var snapshot = new PerformancePacketSnapshot(sourceId, ipc);
                lock (_networkDebugLog)
                {
                    _networkDebugLog.Add(snapshot);
                    if (_networkDebugLog.Count > NetworkDebugMaxEntries)
                        _networkDebugLog.RemoveAt(0);
                }
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error in HandlePerformancePacket");
        }
        return result;
    }

    // Arms this client to start on the next matching performance heartbeat.
    // targetActorId = 0 accepts the first heartbeat from any valid performer.
    public void ArmHeartbeatSync(uint targetEntityId = 0)
    {
        _heartbeatSyncTargetEntityId = targetEntityId;
        _heartbeatSyncArmed = true;
        DalamudApi.PluginLog.Information($"[HeartbeatSync] Armed - target={targetEntityId}");
        // ImGuiUtil.AddNotification(NotificationType.Info, "[MidiBard] Heartbeat sync armed - waiting for next beat...");
    }

    public void DisarmHeartbeatSync()
    {
        _heartbeatSyncArmed = false;
        _heartbeatSyncTargetEntityId = 0;
        DalamudApi.PluginLog.Information("[HeartbeatSync] Disarmed");
    }

    private void StartHeartbeatSync()
    {
        if (AgentManager.AgentMetronome.EnsembleModeRunning)
        {
            // Party member: game's ensemble lock handles the metronome countdown.
            DalamudApi.PluginLog.Warning("[HeartbeatSync] Party path - StartEnsemble immediately");
            StartEnsemble();
        }
        else
        {
            // Non-party member: no game metronome, manually wait abs(EnsembleIndicatorDelay)
            // seconds so notes start at the same real-time moment as party members' metronome-0.
            var countdownMs = (int)(Math.Abs(Plugin.Config.EnsembleIndicatorDelay) * 1000);
            DalamudApi.PluginLog.Warning($"[HeartbeatSync] Non-party countdown {countdownMs}ms before StartEnsemble");
            _ = Task.Run(async () =>
            {
                await Task.Delay(countdownMs);
                await DalamudApi.Framework.RunOnFrameworkThread(StartEnsemble);
            });
        }
    }

    /// <summary>
    /// Arms all same-machine clients via IPC and in-party cross-machine clients via chat.
    /// Resolves the target entity from <see cref="Configuration.HeartbeatSyncListenToCharacterName"/>
    /// when set; falls back to the local player's EntityId.
    /// </summary>
    public void ArmAndBroadcastHeartbeatSync()
    {
        var characterName = Plugin.Config.HeartbeatSyncListenToCharacterName;
        uint targetEntityId = !characterName.IsNullOrEmpty()
            ? ChatWatcher.FindEntityIdByNameWorld(characterName)
            : DalamudApi.PlayerState.EntityId;
        // Arms all same-machine clients (includeSelf: true in IpcProvider).
        Plugin.IpcProvider.ArmHeartbeatSync(targetEntityId);
        // Arms in-party cross-machine clients via /p chat command.
        Plugin.ChatWatcher.SendArmSync();
    }
}
