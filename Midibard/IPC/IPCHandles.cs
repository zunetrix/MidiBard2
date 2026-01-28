// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System;
using System.Buffers;
using System.IO;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

namespace MidiBard.IPC;

public enum MessageTypeCode
{
    Hello = 1,
    Bye,
    Acknowledge,

    GetMaster,
    SetSlave,
    SetUnslave,

    SyncPlaylist = 10,
    RemoveTrackIndex,
    MoveSongToIndex,
    ChangeSongPlayedStatus,
    ResetAllSongsPlayedStatus,
    LoadPlaybackIndex,

    UpdateMidiFileConfig = 20,
    UpdateEnsembleMember,
    MidiEvent,
    SetInstrument,
    EnsembleStartTime,
    UpdateDefaultPerformer,

    SetOption = 100,
    ShowWindow,
    SyncAllSettings,
    Object,
    SyncPlayStatus,
    PlaybackSpeed,
    GlobalTranspose,
    MoveToTime,
    ReloadLRC,
    SendDownloadedSong,

    ErrPlaybackNull = 1000,
}

enum PlaylistOperation
{
    SyncAll = 1,
    AddIndex,
    CloneIndex,
    RemoveIndex,
    ReorderIndex,
    RenameIndex,
}

static class IPCHandles
{
    [IPCHandle(MessageTypeCode.Hello)]
    private static void HandleHello(IPCEnvelope message)
    {
        ArrayBufferWriter<byte> b = new ArrayBufferWriter<byte>();
    }

    public static void SyncPlaylist()
    {
        var ipcEnvelope = IPCEnvelope.Create(MessageTypeCode.SyncPlaylist);
        ipcEnvelope.PlaylistContainer = Plugin.PlaylistManager.CurrentContainer;
        ipcEnvelope.BroadCast();
    }

    [IPCHandle(MessageTypeCode.SyncPlaylist)]
    private static void HandleSyncPlaylist(IPCEnvelope message)
    {
        Plugin.PlaylistManager.SetContainerPrivate(message.PlaylistContainer);
    }

    //public static void SyncPlayStatus(bool loadPlayback)
    //{
    //    var status = (PlaylistContainerManager.CurrentPlaylistIndex, Plugin.PlaylistManager.CurrentSongIndex, loadPlayback);
    //    var ipcEnvelope = IPCEnvelope.Create(MessageTypeCode.SyncPlayStatus, status);
    //    ipcEnvelope.BroadCast();
    //}

    //[IPCHandle(MessageTypeCode.SyncPlayStatus)]
    //private static void HandleSyncPlayStatus(IPCEnvelope message)
    //{
    //    var (playlistIndex, songIndex, loadPlayback) = message.DataStruct<(int,int,bool)>();
    //    var container = PlaylistContainerManager.Container;
    //    container.CurrentListIndex = playlistIndex;
    //    container.CurrentPlaylist.CurrentSongIndex = songIndex;

    //    if (loadPlayback)
    //    {
    //        Plugin.PlaylistManager.LoadPlayback(null, false, false);
    //    }
    //}

    public static void RemoveTrackIndex(int songIndex)
    {
        IPCEnvelope.Create(MessageTypeCode.RemoveTrackIndex, songIndex).BroadCast();
    }

    [IPCHandle(MessageTypeCode.RemoveTrackIndex)]
    private static void HandleRemoveTrackIndex(IPCEnvelope message)
    {
        var songIndex = message.DataStruct<int>();
        Plugin.PlaylistManager.RemoveLocal(songIndex);
    }

    public static void MoveSongToIndex(int songIndex, int targetIndex)
    {
        IPCEnvelope.Create(MessageTypeCode.MoveSongToIndex, (songIndex, targetIndex)).BroadCast();
    }

    [IPCHandle(MessageTypeCode.MoveSongToIndex)]
    private static void HandleMoveSongToIndex(IPCEnvelope message)
    {
        var tuple = message.DataStruct<(int, int)>();
        Plugin.PlaylistManager.MoveSongToIndexLocal(tuple.Item1, tuple.Item2);
    }

    public static void ChangeSongPlayedStatus(int songIndex, bool newStatus)
    {
        IPCEnvelope.Create(MessageTypeCode.ChangeSongPlayedStatus, (songIndex, newStatus)).BroadCast();
    }

    [IPCHandle(MessageTypeCode.ChangeSongPlayedStatus)]
    private static void HandleChangeSongPlayedStatus(IPCEnvelope message)
    {
        var tuple = message.DataStruct<(int, bool)>();
        Plugin.PlaylistManager.ChangeSongPlayedStatusLocal(tuple.Item1, tuple.Item2);
    }
    public static void ResetAllSongsPlayedStatus()
    {
        IPCEnvelope.Create(MessageTypeCode.ResetAllSongsPlayedStatus).BroadCast();
    }

    [IPCHandle(MessageTypeCode.ResetAllSongsPlayedStatus)]
    private static void HandleResetAllSongsPlayedStatus(IPCEnvelope message)
    {
        Plugin.PlaylistManager.ResetAllSongsPlayedStatusLocal();
    }

    public static void UpdateMidiFileConfig(MidiFileConfig config, bool updateInstrumentAfterFinished = false)
    {
        IPCEnvelope.Create(MessageTypeCode.UpdateMidiFileConfig, Plugin.Config.JsonSerialize()).BroadCast(true);
    }

    [IPCHandle(MessageTypeCode.UpdateMidiFileConfig)]
    private static void HandleUpdateMidiFileConfig(IPCEnvelope message)
    {
        var midiFileConfig = message.StringData[0].JsonDeserialize<MidiFileConfig>();
        Plugin.CurrentBardPlayback.MidiFileConfig = midiFileConfig;
        Plugin.CurrentBardPlayback.SyncTrackStatusWithMidiFileConfig();
    }

    public static void LoadPlayback(int index, bool includeSelf = false)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;
        IPCEnvelope.Create(MessageTypeCode.LoadPlaybackIndex, index).BroadCast();
    }

    [IPCHandle(MessageTypeCode.LoadPlaybackIndex)]
    private static void HandleLoadPlayback(IPCEnvelope message)
    {
        var index = message.DataStruct<int>();
        Plugin.Plugin.PlaylistManager.CurrentContainer.CurrentSongIndex = index;

        Plugin.Plugin.PlaylistManager.LoadPlayback(null, false, false);
    }

    public static void UpdateInstrument(bool takeout)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;
        IPCEnvelope.Create(MessageTypeCode.SetInstrument, takeout).BroadCast(true);
    }

    [IPCHandle(MessageTypeCode.SetInstrument)]
    private static void HandleSetInstrument(IPCEnvelope message)
    {
        var takeout = message.DataStruct<bool>();
        if (!takeout)
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(0);
            Plugin.MidiPlayerControl.Stop();
            return;
        }

        if (Plugin.CurrentBardPlayback == null || Plugin.CurrentBardPlayback.MidiFileConfig == null)
        {
            IPCHandles.ErrPlaybackNull(DalamudApi.Player.CharacterName);
            return;
        }

        uint? instrument = null;
        foreach (var cur in Plugin.CurrentBardPlayback.MidiFileConfig.Tracks)
        {
            if (cur.Enabled && MidiFileConfig.IsCidOnTrack((long)DalamudApi.Player.ContentId, cur))
            {
                instrument = cur.Instrument;
                break;
            }
        }

        if (instrument != null)
            Plugin.InstrumentSwitcher.SwitchToContinue((uint)instrument);
    }

    public static void SetOption(string option, int value, bool includeSelf)
    {
        var ipcEnvelope = IPCEnvelope.Create(MessageTypeCode.SetOption, option, value.ToString());
        ipcEnvelope.BroadCast(includeSelf);
    }

    [IPCHandle(MessageTypeCode.SetOption)]
    private static void HandleSetOption(IPCEnvelope message)
    {
        var optionName = message.StringData[0];
        var optionValue = uint.Parse(message.StringData[1]);
        DalamudApi.GameConfig.System.Set(optionName, optionValue);
    }
    public static void ShowWindow(Winapi.nCmdShow option)
    {
        IPCEnvelope.Create(MessageTypeCode.ShowWindow, option).BroadCast();
    }

    [IPCHandle(MessageTypeCode.ShowWindow)]
    private static void HandleShowWindow(IPCEnvelope message)
    {
        var nCmdShow = message.DataStruct<Winapi.nCmdShow>();
        var hWnd = DalamudApi.PluginInterface.UiBuilder.WindowHandlePtr;
        var isIconic = Winapi.IsIconic(hWnd);

        switch (nCmdShow)
        {
            case Winapi.nCmdShow.SW_RESTORE when isIconic:
                Plugin.Ui.OpenMainWindow();
                Winapi.ShowWindow(hWnd, nCmdShow);
                break;
            case Winapi.nCmdShow.SW_MINIMIZE when !isIconic:
                Plugin.Ui.CloseMainWindow();
                Winapi.ShowWindow(hWnd, nCmdShow);
                break;
        }
    }

    public static void SyncAllSettings()
    {
        Plugin.Config.Save();
        IPCEnvelope.Create(
            MessageTypeCode.SyncAllSettings, Plugin.Config.JsonSerialize(),
            Plugin.Config.SaveConfigAfterSync.ToString()
        ).BroadCast(includeself: false);
    }

    [IPCHandle(MessageTypeCode.SyncAllSettings)]
    private static void HandleSyncAllSettings(IPCEnvelope message)
    {
        var configString = message.StringData[0];
        bool saveConfigAfterSync = bool.TryParse(message.StringData[1], out var parsed) && parsed;

        // var jsonConfig = configString.JsonDeserialize<Configuration>();
        // MidiBard.config = jsonDeserialize;
        Plugin.Config.UpdateFromJson(configString);
        ThemeManager.SetTheme(Plugin.Config.CurrentTheme);

        if (saveConfigAfterSync)
            Plugin.Config.Save();
    }

    public static void UpdateDefaultPerformer()
    {
        IPCEnvelope.Create(MessageTypeCode.UpdateDefaultPerformer, MidiFileConfigManager.defaultPerformer.JsonSerialize()).BroadCast(true);
    }

    [IPCHandle(MessageTypeCode.UpdateDefaultPerformer)]
    public static void HandleUpdateDefaultPerformer(IPCEnvelope message)
    {
        var str = message.StringData[0];
        var jsonDeserialize = str.JsonDeserialize<DefaultPerformer>();
        MidiFileConfigManager.defaultPerformer = jsonDeserialize;
        if (Plugin.CurrentBardPlayback != null)
        {
            Plugin.CurrentBardPlayback.MidiFileConfig = BardPlayback.ReloadMidiFileConfig(Plugin.CurrentBardPlayback.MidiFileConfig);
        }
    }

    public static void PlaybackSpeed(float playbackSpeed)
    {
        if (!DalamudApi.PartyList.IsPartyLeader()) return;
        IPCEnvelope.Create(MessageTypeCode.PlaybackSpeed, playbackSpeed).BroadCast();
    }

    [IPCHandle(MessageTypeCode.PlaybackSpeed)]
    public static void HandlePlaybackSpeed(IPCEnvelope message)
    {
        var playbackSpeed = message.DataStruct<float>();
        Plugin.Config.PlaySpeed = playbackSpeed;
        if (Plugin.CurrentBardPlayback != null)
        {
            Plugin.CurrentBardPlayback.Speed = Plugin.Config.PlaySpeed;
        }
    }

    public static void GlobalTranspose(int transpose)
    {
        IPCEnvelope.Create(MessageTypeCode.GlobalTranspose, transpose).BroadCast();
    }

    [IPCHandle(MessageTypeCode.GlobalTranspose)]
    public static void HandleGlobalTranspose(IPCEnvelope message)
    {
        var globalTranspose = message.DataStruct<int>();
        Plugin.Config.SetTransposeGlobal(globalTranspose);
    }

    public static void SetPlaybackTime(TimeSpan time)
    {
        IPCEnvelope.Create(MessageTypeCode.MoveToTime, time).BroadCast();
    }

    [IPCHandle(MessageTypeCode.MoveToTime)]
    public static void HandleMoveToTime(IPCEnvelope message)
    {
        if (Plugin.CurrentBardPlayback == null) return;
        Plugin.MidiPlayerControl.SetTime(new MetricTimeSpan(message.DataStruct<TimeSpan>()));
    }

    public static void ErrPlaybackNull(string characterName)
    {
        IPCEnvelope.Create(MessageTypeCode.ErrPlaybackNull, characterName).BroadCast(true);
    }

    [IPCHandle(MessageTypeCode.ErrPlaybackNull)]
    public static void HandleErrPlaybackNull(IPCEnvelope message)
    {
        var characterName = message.StringData[0];
        DalamudApi.PluginLog.Warning($"ERR: Playback Null on character: {characterName}");
        DalamudApi.ChatGui.PrintError($"[MidiBard 2] Error: Load song failed on character: {characterName}, please try to switch the song again.");
    }

    [IPCHandle(MessageTypeCode.ReloadLRC)]
    public static void HandleReloadLRC(IPCEnvelope message)
    {
        var lrcPath = message.StringData[0];

        try
        {
            LyricsPlayer.PlayingLrc = new LyricsPlayer(lrcPath);
            ImGuiUtil.AddNotification(NotificationType.Info, "Lrc Reloaded " + lrcPath);
        }
        catch
        {
            ImGuiUtil.AddNotification(NotificationType.Error, "Error when reloading Lrc " + lrcPath);
        }
    }

    public static void SendDownloadedSong(string filename, byte[] mididata)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || Plugin.Config.playOnMultipleDevices) return;
        IPCEnvelope.Create(MessageTypeCode.SendDownloadedSong, mididata).BroadCast();
    }

    [IPCHandle(MessageTypeCode.SendDownloadedSong)]
    public static void HandleSendDownloadedSong(IPCEnvelope message)
    {
        byte[] data = message.Data;
        _ = FilePlayback.LoadPlayback("NONE", new MemoryStream(data));
    }
}
