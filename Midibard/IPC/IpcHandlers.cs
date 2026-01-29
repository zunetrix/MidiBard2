using System;
using System.Buffers;
using System.IO;

using Dalamud.Interface.ImGuiNotification;

using Melanchall.DryWetMidi.Interaction;

using MidiBard.Managers;
using MidiBard.Util2;

namespace MidiBard.Ipc;

[AttributeUsage(AttributeTargets.Method)]
internal class IpcHandleAttribute : Attribute
{
    public IpcMessageType MessageType { get; }

    public IpcHandleAttribute(IpcMessageType messageType)
    {
        MessageType = messageType;
    }
}

internal class IpcHandlers
{
    private readonly Plugin Plugin;

    public IpcHandlers(Plugin plugin)
    {
        Plugin = plugin;
    }

    [IpcHandle(IpcMessageType.Hello)]
    private void HandleHello(IpcMessage message)
    {
        ArrayBufferWriter<byte> b = new ArrayBufferWriter<byte>();
    }

    [IpcHandle(IpcMessageType.SyncAllSettings)]
    private void HandleSyncAllSettings(IpcMessage message)
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


    [IpcHandle(IpcMessageType.SendDownloadedSong)]
    public void HandleSendDownloadedSong(IpcMessage message)
    {
        byte[] data = message.Data;
        _ = Plugin.FilePlayback.LoadPlayback("NONE", new MemoryStream(data));
    }

    [IpcHandle(IpcMessageType.ReloadLRC)]
    public void HandleReloadLRC(IpcMessage message)
    {
        var lrcPath = message.StringData[0];

        try
        {
            Plugin.LyricsPlayer.LoadLyrics(lrcPath);
            ImGuiUtil.AddNotification(NotificationType.Info, "Lrc Reloaded " + lrcPath);
        }
        catch
        {
            ImGuiUtil.AddNotification(NotificationType.Error, "Error when reloading Lrc " + lrcPath);
        }
    }

    [IpcHandle(IpcMessageType.SyncPlaylist)]
    private void HandleSyncPlaylist(IpcMessage message)
    {
        // TODO: check if its working
        // int macroIndex = message.DataStruct<PlaylistContainer>();
        var playlistContainer = message.StringData[0].JsonDeserialize<PlaylistContainer>();
        Plugin.PlaylistManager.SetContainerPrivate(playlistContainer);
    }

    [IpcHandle(IpcMessageType.RemoveTrackIndex)]
    private void HandleRemoveTrackIndex(IpcMessage message)
    {
        var songIndex = message.DataStruct<int>();
        Plugin.PlaylistManager.RemoveLocal(songIndex);
    }

    [IpcHandle(IpcMessageType.MoveSongToIndex)]
    private void HandleMoveSongToIndex(IpcMessage message)
    {
        var tuple = message.DataStruct<(int, int)>();
        Plugin.PlaylistManager.MoveSongToIndexLocal(tuple.Item1, tuple.Item2);
    }

    [IpcHandle(IpcMessageType.ChangeSongPlayedStatus)]
    private void HandleChangeSongPlayedStatus(IpcMessage message)
    {
        var tuple = message.DataStruct<(int, bool)>();
        Plugin.PlaylistManager.ChangeSongPlayedStatusLocal(tuple.Item1, tuple.Item2);
    }

    [IpcHandle(IpcMessageType.ResetAllSongsPlayedStatus)]
    private void HandleResetAllSongsPlayedStatus(IpcMessage message)
    {
        Plugin.PlaylistManager.ResetAllSongsPlayedStatusLocal();
    }

    [IpcHandle(IpcMessageType.UpdateMidiFileConfig)]
    private void HandleUpdateMidiFileConfig(IpcMessage message)
    {
        var midiFileConfig = message.StringData[0].JsonDeserialize<MidiFileConfig>();
        Plugin.CurrentBardPlayback.MidiFileConfig = midiFileConfig;
        Plugin.CurrentBardPlayback.SyncTrackStatusWithMidiFileConfig();
    }

    [IpcHandle(IpcMessageType.LoadPlaybackIndex)]
    private void HandleLoadPlayback(IpcMessage message)
    {
        var index = message.DataStruct<int>();
        Plugin.PlaylistManager.CurrentContainer.CurrentSongIndex = index;

        Plugin.PlaylistManager.LoadPlayback(null, false, false);
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

        if (Plugin.CurrentBardPlayback == null || Plugin.CurrentBardPlayback.MidiFileConfig == null)
        {
            Plugin.IpcProvider.ErrPlaybackNull(DalamudApi.PlayerState.CharacterName);
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

    [IpcHandle(IpcMessageType.SetOption)]
    private void HandleSetOption(IpcMessage message)
    {
        var optionName = message.StringData[0];
        var optionValue = uint.Parse(message.StringData[1]);
        DalamudApi.GameConfig.System.Set(optionName, optionValue);
    }

    [IpcHandle(IpcMessageType.ShowWindow)]
    private void HandleShowWindow(IpcMessage message)
    {
        var nCmdShow = message.DataStruct<WindowsApi.nCmdShow>();
        var hWnd = DalamudApi.PluginInterface.UiBuilder.WindowHandlePtr;
        var isIconic = WindowsApi.IsIconic(hWnd);

        switch (nCmdShow)
        {
            case WindowsApi.nCmdShow.SW_RESTORE when isIconic:
                Plugin.Ui.MainWindow.IsOpen = true;
                WindowsApi.ShowWindow(hWnd, nCmdShow);
                break;
            case WindowsApi.nCmdShow.SW_MINIMIZE when !isIconic:
                Plugin.Ui.MainWindow.IsOpen = false;
                WindowsApi.ShowWindow(hWnd, nCmdShow);
                break;
        }
    }

    [IpcHandle(IpcMessageType.UpdateDefaultPerformer)]
    public void HandleUpdateDefaultPerformer(IpcMessage message)
    {
        var str = message.StringData[0];
        var jsonDeserialize = str.JsonDeserialize<DefaultPerformer>();
        Plugin.MidiFileConfigManager.defaultPerformer = jsonDeserialize;

        if (Plugin.CurrentBardPlayback != null)
        {
            Plugin.CurrentBardPlayback.MidiFileConfig = Plugin.CurrentBardPlayback.ReloadMidiFileConfig(Plugin.CurrentBardPlayback.MidiFileConfig);
        }
    }

    [IpcHandle(IpcMessageType.PlaybackSpeed)]
    public void HandlePlaybackSpeed(IpcMessage message)
    {
        var playbackSpeed = message.DataStruct<float>();
        Plugin.Config.PlaySpeed = playbackSpeed;
        if (Plugin.CurrentBardPlayback != null)
        {
            Plugin.CurrentBardPlayback.Speed = Plugin.Config.PlaySpeed;
        }
    }

    [IpcHandle(IpcMessageType.GlobalTranspose)]
    public void HandleGlobalTranspose(IpcMessage message)
    {
        // TODO: remove Plugin dependencia, move SetTransposeGlobal to other local
        var globalTranspose = message.DataStruct<int>();
        Plugin.Config.SetTransposeGlobal(globalTranspose, Plugin);
    }

    [IpcHandle(IpcMessageType.MoveToTime)]
    public void HandleMoveToTime(IpcMessage message)
    {
        if (Plugin.CurrentBardPlayback == null) return;
        Plugin.MidiPlayerControl.SetTime(new MetricTimeSpan(message.DataStruct<TimeSpan>()));
    }

    [IpcHandle(IpcMessageType.ErrPlaybackNull)]
    public void HandleErrPlaybackNull(IpcMessage message)
    {
        var characterName = message.StringData[0];
        DalamudApi.PluginLog.Warning($"ERR: Playback Null on character: {characterName}");
        DalamudApi.ChatGui.PrintError($"[MidiBard 2] Error: Load song failed on character: {characterName}, please try to switch the song again.");
    }
}
