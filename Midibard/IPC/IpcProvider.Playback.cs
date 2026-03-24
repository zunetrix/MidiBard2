using System;
using System.IO;
using System.Threading.Tasks;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.Json;
using MidiBard.Managers;

namespace MidiBard.Ipc;

internal partial class IpcProvider
{
    public void LoadPlayback(int index, bool includeSelf = false)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;

        var message = IpcMessage.Create(IpcMessageType.LoadPlaybackIndex, index).Serialize();
        BroadCast(message, includeSelf);
    }

    [IpcHandle(IpcMessageType.LoadPlaybackIndex)]
    private void HandleLoadPlayback(IpcMessage message)
    {
        Plugin.PlaylistManager.CurrentSongIndex = message.DataStruct<int>();
        Plugin.PlaylistManager.LoadPlayback(null, false, false);
    }

    public void UpdateMidiFileConfig(MidiFileConfig midiFileConfig)
    {
        var message = IpcMessage.Create(IpcMessageType.UpdateMidiFileConfig, midiFileConfig.JsonSerialize()).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.UpdateMidiFileConfig)]
    private void HandleUpdateMidiFileConfig(IpcMessage message)
    {
        var midiFileConfig = message.StringData[0].JsonDeserialize<MidiFileConfig>();
        Plugin.CurrentBardPlayback.MidiFileConfig = midiFileConfig;
        Plugin.CurrentBardPlayback.SyncTrackStatusWithMidiFileConfig();
    }

    public void UpdateInstrument(bool takeout)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;

        var message = IpcMessage.Create(IpcMessageType.SetInstrument, takeout).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.SetInstrument)]
    private void HandleSetInstrument(IpcMessage message)
    {
        var takeout = message.DataStruct<bool>();
        if (!takeout)
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(0);
            Plugin.MidiPlayerControl.Stop();
            return;
        }
        _ = ApplyEquipInstrumentAsync();
    }

    private async Task ApplyEquipInstrumentAsync()
    {
        // Wait up to 5 s for an async LoadPlayback to finish before giving up
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Plugin.CurrentBardPlayback.IsLoaded && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        if (!Plugin.CurrentBardPlayback.IsLoaded || Plugin.CurrentBardPlayback.MidiFileConfig == null)
        {
            var characterName = DalamudApi.PlayerState.CharacterName;
            if (string.IsNullOrEmpty(characterName))
                characterName = "unknown";
            Plugin.IpcProvider.ErrPlaybackNull(characterName);
            return;
        }

        uint? instrument = null;
        foreach (var track in Plugin.CurrentBardPlayback.MidiFileConfig.Tracks)
        {
            if (track.Enabled && MidiFileConfig.IsCidOnTrack((long)DalamudApi.PlayerState.ContentId, track, Plugin.Config.EnsembleMemberConfigs))
            {
                instrument = track.Instrument;
                break;
            }
        }
        if (instrument != null)
            Plugin.InstrumentSwitcher.SwitchToContinue((uint)instrument);
    }

    public void PlaybackSpeed(float playbackSpeed)
    {
        if (!DalamudApi.PartyList.IsPartyLeader()) return;

        var message = IpcMessage.Create(IpcMessageType.PlaybackSpeed, playbackSpeed).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.PlaybackSpeed)]
    private void HandlePlaybackSpeed(IpcMessage message)
    {
        Plugin.Config.PlaySpeed = message.DataStruct<float>();
        if (Plugin.CurrentBardPlayback.IsLoaded)
            Plugin.CurrentBardPlayback.Speed = Plugin.Config.PlaySpeed;
    }

    public void GlobalTranspose(int transpose)
    {
        var message = IpcMessage.Create(IpcMessageType.GlobalTranspose, transpose).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.GlobalTranspose)]
    private void HandleGlobalTranspose(IpcMessage message)
    {
        Plugin.Config.SetTransposeGlobal(message.DataStruct<int>(), Plugin);
    }

    public void SetPlaybackTime(TimeSpan time)
    {
        var message = IpcMessage.Create(IpcMessageType.MoveToTime, time).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.MoveToTime)]
    private void HandleMoveToTime(IpcMessage message)
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded) return;
        Plugin.MidiPlayerControl.SetTime(new MetricTimeSpan(message.DataStruct<TimeSpan>()));
    }

    public void SendDownloadedSong(string filename, byte[] mididata)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;

        var message = IpcMessage.Create(IpcMessageType.SendDownloadedSong, mididata).Serialize();
        BroadCast(message);
    }

    [IpcHandle(IpcMessageType.SendDownloadedSong)]
    private void HandleSendDownloadedSong(IpcMessage message)
    {
        _ = Plugin.FilePlayback.LoadPlayback("NONE", new MemoryStream(message.Data));
    }

    public void ErrPlaybackNull(string characterName)
    {
        var message = IpcMessage.Create(IpcMessageType.ErrPlaybackNull, characterName).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.ErrPlaybackNull)]
    private void HandleErrPlaybackNull(IpcMessage message)
    {
        var characterName = message.StringData[0];
        DalamudApi.PluginLog.Warning($"Playback Null on character: {characterName}");
        DalamudApi.ChatGui.PrintError($"[MidiBard] Error: Load song failed on character: {characterName}, please try to switch the song again.");
    }

    // Broadcasts arm-heartbeat-sync to all same-machine clients (includeSelf: true arms the leader too).
    // targetActorId = 0 means "accept the first heartbeat from any valid performer".
    public void ArmHeartbeatSync(uint targetEntityId)
    {
        var message = IpcMessage.Create(IpcMessageType.ArmHeartbeatSync, targetEntityId).Serialize();
        BroadCast(message, includeSelf: true);
    }

    [IpcHandle(IpcMessageType.ArmHeartbeatSync)]
    private void HandleArmHeartbeatSync(IpcMessage message)
    {
        Plugin.EnsembleManager.ArmHeartbeatSync(message.DataStruct<uint>());
    }
}
