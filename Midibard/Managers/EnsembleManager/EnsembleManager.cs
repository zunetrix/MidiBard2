using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Dalamud.Hooking;
using Dalamud.Interface.ImGuiNotification;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard.Managers;

internal partial class EnsembleManager : IDisposable
{
    private Plugin Plugin { get; }
    public event Action EnsembleStart;
    public event Action EnsemblePrepare;
    public event Action EnsembleStopped;
    public readonly Stopwatch EnsembleTimer = new Stopwatch();
    internal List<TimeSpan> EnsembleRecvTime { get; } = new();
    public bool EnsembleRunning => EnsembleTimer.IsRunning;
    private delegate long NetworkEnsembleDelegate(IntPtr a1, IntPtr a2);
    private readonly Hook<NetworkEnsembleDelegate> NetworkEnsembleHook;

    private delegate long EnsemblePerformanceDelegate(uint sourceId, IntPtr data);
    private readonly Hook<EnsemblePerformanceDelegate> _ensemblePerformanceHook;

    private long HandleNetworkEnsemble(IntPtr a1, IntPtr a2)
    {
        if (Plugin.Config.MonitorOnEnsemble)
        {
            if (Plugin.Config.UseHeartbeatSync)
            {
                // Party mode + heartbeat sync: each client arms itself when the game packet arrives,
                // then waits for the first performance heartbeat to trigger actual playback.
                // Also broadcast to same-machine non-party clients via IPC.
                ArmHeartbeatSync(0);
                Plugin.IpcProvider.ArmHeartbeatSync(0);
            }
            else
            {
                StartEnsemble();
            }
        }

        return NetworkEnsembleHook.Original(a1, a2);
    }

    internal EnsembleManager(Plugin plugin)
    {
        Plugin = plugin;
        //UpdateMetronomeHook = new Hook<sub_140C87B40>(Offsets.UpdateMetronome, HandleUpdateMetronome);
        //UpdateMetronomeHook.Enable();

        // NetworkEnsembleHook = DalamudApi.GameInteropProvider.HookFromAddress<sub_1410F4EC0>(Offsets.NetworkEnsembleStart, (a1, a2) =>
        // {
        //     if (Plugin.Config.MonitorOnEnsemble) StartEnsemble();
        //     return NetworkEnsembleHook.Original(a1, a2);
        // });
        // NetworkEnsembleHook.Enable();

        NetworkEnsembleHook = DalamudApi.GameInteropProvider.HookFromAddress<NetworkEnsembleDelegate>(Offsets.NetworkEnsembleStart, HandleNetworkEnsemble);
        NetworkEnsembleHook.Enable();

        _ensemblePerformanceHook = DalamudApi.GameInteropProvider.HookFromAddress<EnsemblePerformanceDelegate>(Offsets.EnsembleReceivedHandler, HandlePerformancePacket);
        _ensemblePerformanceHook.Enable();

        EnsembleStopped += () => EnsembleTimer.Reset();
    }

    // Tracks whether ensemble was running on the previous frame to detect the running→stopped transition.
    private bool _wasEnsembleModeRunning = false;

    // Called each framework update when Config.MonitorOnEnsemble is true.
    // Detects when ensemble mode ends and triggers cleanup (stop playback, unequip instrument).
    public void MonitorEnsembleState()
    {
        if (_wasEnsembleModeRunning)
        {
            if (!AgentManager.AgentMetronome.EnsembleModeRunning || !AgentManager.AgentPerformance.InPerformanceMode)
            {
                InvokeEnsembleStop();
                if (Plugin.Config.StopPlayingWhenEnsembleEnds)
                    Plugin.MidiPlayerControl.Pause();

                // Fallback unequip: catches clients that missed the IPC unequip broadcast
                if (Plugin.Config.UnequipInstrumentsOnEnsembleEnd)
                {
                    Plugin.InstrumentSwitcher.SwitchToContinue(0);
                }

                if (Plugin.Config.EnableEnsemblePlayMode && DalamudApi.PartyList.IsPartyLeader())
                {
                    Plugin.FilePlayback.TryEnsembleAutoAdvance();
                }
            }
        }
        _wasEnsembleModeRunning = AgentManager.AgentMetronome.EnsembleModeRunning && AgentManager.AgentPerformance.InPerformanceMode;
    }

    //public SyncHelper(out List<(byte[] notes, byte[] tones)> sendNotes, out List<(byte[] notes, byte[] tones)> recvNotes)
    //{
    //    sendNotes = new List<(byte[] notes, byte[] tones)>();
    //    recvNotes = new List<(byte[] notes, byte[] tones)>();
    //}

    //private delegate IntPtr sub_140C87B40(IntPtr agentMetronome, byte beat);
    //   private Hook<sub_140C87B40> UpdateMetronomeHook;

    public void BeginEnsembleReadyCheck(int delayMs = 0)
    {
        if (delayMs > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delayMs);
                await DalamudApi.Framework.RunOnFrameworkThread(DoBeginEnsembleReadyCheck);
            });
            return;
        }

        DoBeginEnsembleReadyCheck();
    }

    private unsafe void DoBeginEnsembleReadyCheck()
    {
        if (AgentManager.AgentMetronome.EnsembleModeRunning) return;

        if (AgentManager.AgentPerformance.InPerformanceMode && !AgentManager.AgentMetronome.Struct->AgentInterface.IsAgentActive())
            AgentManager.AgentMetronome.Struct->AgentInterface.Show();

        Playlib.BeginReadyCheck();
        Playlib.ConfirmBeginReadyCheck();
    }

    internal void StopEnsemble()
    {
        Playlib.BeginReadyCheck();
        Playlib.SendAction("SelectYesno", 3, 0);
    }

    internal void BroadcastEquipInstruments()
    {
        if (Plugin.CurrentBardPlayback?.MidiFileConfig is { } config)
            Plugin.IpcProvider.UpdateMidiFileConfig(config);

        if (!Plugin.Config.playOnMultipleDevices)
            Plugin.IpcProvider.UpdateInstrument(true);
        else
            Plugin.ChatWatcher.SendUpdateInstrument();
    }

    internal void BroadcastUnequipInstruments()
    {
        if (Plugin.Config.playOnMultipleDevices)
        {
            Plugin.ChatWatcher.SendClose();
            return;
        }

        Plugin.IpcProvider.UpdateInstrument(false); // broadcast to other clients
        Plugin.InstrumentSwitcher.SwitchToContinue(0); // always self-unequip directly
    }

    //private unsafe IntPtr HandleUpdateMetronome(IntPtr agentMetronome, byte currentBeat)
    //{
    //    var original = UpdateMetronomeHook.Original(agentMetronome, currentBeat);
    //    try
    //    {
    //        if (MidiBard.Plugin.Config.MonitorOnEnsemble)
    //        {
    //            var metronome = ((AgentMetronome.AgentMetronomeStruct*)agentMetronome);
    //            var beatsPerBar = metronome->MetronomeBeatsPerBar;
    //            var barElapsed = metronome->MetronomeBeatsElapsed;
    //            var ensembleRunning = metronome->EnsembleModeRunning;
    //               DalamudApi.PluginLog.Verbose($"[Metronome] {barElapsed} {currentBeat}/{beatsPerBar}");

    //               if (barElapsed == -2 && currentBeat == 0 && ensembleRunning != 0)
    //               {
    //                   DalamudApi.PluginLog.Warning($"Prepare: ensemble: {ensembleRunning}");
    //                   StartEnsemble();
    //               }
    //           }
    //    }
    //    catch (Exception e)
    //    {
    //        DalamudApi.PluginLog.Error(e, $"error in {nameof(UpdateMetronomeHook)}");
    //    }

    //    return original;
    //}

    private void StartEnsemble()
    {
        EnsembleRecvTime.Clear();
        EnsemblePrepare?.Invoke();

        // if playback is null, cancel ensemble mode.
        if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            if (Plugin.Config.SyncClients)
            {
                StopEnsemble();
                ImGuiUtil.AddNotification(NotificationType.Error, "Please load a song before starting ensemble!");
                Plugin.IpcProvider.ErrPlaybackNull(DalamudApi.PlayerState.CharacterName);
            }
        }
        else
        {
            EnsembleTimer.Restart();
            Plugin.CurrentBardPlayback.Stop();
            Plugin.CurrentBardPlayback.MoveToStart();

            try
            {
                Plugin.MidiPlayerControl.DoPlay(true);
                DalamudApi.PluginLog.Warning($"Start ensemble: sw: {EnsembleTimer.Elapsed.TotalMilliseconds}ms");
                EnsembleStart?.Invoke();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error EnsembleStart");
            }
        }
    }

    internal void InvokeEnsembleStop() => EnsembleStopped?.Invoke();

    public void Dispose()
    {
        NetworkEnsembleHook?.Dispose();
        _ensemblePerformanceHook?.Dispose();
        //UpdateMetronomeHook?.Dispose();
    }
}
