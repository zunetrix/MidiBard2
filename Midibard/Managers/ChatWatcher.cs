using System;
using System.Collections.Generic;
using System.Linq;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.IO;

using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Util;

namespace MidiBard;

internal class ChatWatcher : IDisposable
{
    private Plugin Plugin { get; }
    private readonly Dictionary<string, Action<string[]>> CommandHandlers;

    public ChatWatcher(Plugin plugin)
    {
        Plugin = plugin;

        CommandHandlers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pmd"] = HandlePlayOnMultipleDevices,
            ["switchto"] = HandleSwitchTo,
            ["startensemble"] = HandleStartEnsemble,
            ["stopensemble"] = HandleStopEnsemble,
            ["usechatplaylistsync"] = HandleSendUseChatPlaylistSync,
            ["playlistremove"] = HandleRemoveSong,
            ["playlistmove"] = HandleChangeSongOrder,
            ["reloadplaylist"] = HandleReloadPlaylist,
            ["updatedefaultperformer"] = HandleUpdateDefaultPerformer,
            ["updateinstrument"] = HandleUpdateInstrument,
            ["close"] = HandleClose,
            ["speed"] = HandleChangeSpeed,
            ["transpose"] = HandleSetGlobalTranspose,
            ["downloadsong"] = HandleDownloadSong,
            ["play"] = HandlePlay,
            ["stop"] = HandleStop,
        };

        DalamudApi.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        DalamudApi.ChatGui.ChatMessage -= OnChatMessage;
    }

    internal void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (isHandled || type != XivChatType.Party)
            return;

        var messageString = message.ToString();
        if (!CommandHandlers.Keys.Any(cmd => messageString.StartsWith(cmd, StringComparison.OrdinalIgnoreCase)))
            return;

        string[] parts = message.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1)
            return;

        string cmd = parts[0].ToLower();
        string[] args = parts.Skip(1).ToArray();

        DalamudApi.PluginLog.Warning($"OnChatMessage [{cmd}] ({string.Join(", ", args)})");

        if (CommandHandlers.TryGetValue(cmd, out var action))
        {
            action.Invoke(args);
        }
    }

    internal void SendPlayOnMultipleDevices(bool isOn)
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        var str = isOn ? "on" : "off";
        Chat.SendMessage($"/p pmd {str}");
    }

    private void HandlePlayOnMultipleDevices(string[] args)
    {
        if (args.Length < 1)
            return;

        var value = args[0].ToLower();
        DalamudApi.PluginLog.Warning($"HandlePlayOnMultipleDevices {value}");
        if (value == "on")
            Plugin.Config.playOnMultipleDevices = true;
        else if (value == "off")
            Plugin.Config.playOnMultipleDevices = false;
    }

    // -------------------------

    internal void SendUseChatPlaylistSync(bool isOn)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        var str = isOn ? "on" : "off";
        Chat.SendMessage($"/p usechatplaylistsync {str}");
    }

    private void HandleSendUseChatPlaylistSync(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices) return;

        if (args.Length < 1)
            return;

        var value = args[0].ToLower();
        if (value == "on")
            Plugin.Config.useChatPlaylistSync = true;
        else if (value == "off")
            Plugin.Config.useChatPlaylistSync = false;
    }

    // -------------------------

    internal void SendSwitchTo(int songIndex)
    {
        if (!Plugin.Config.playOnMultipleDevices || !DalamudApi.PartyList.IsInParty())
        {
            return;
        }

        Chat.SendMessage($"/p switchto {songIndex + 1}");
    }

    private void HandleSwitchTo(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !DalamudApi.PartyList.IsInParty() || args.Length < 1)
            return;

        if (int.TryParse(args[0], out int songIndex))
        {
            Plugin.MidiPlayerControl.StopLrc();
            Plugin.PlaylistManager.LoadPlayback(songIndex - 1);
            Plugin.Ui.MainWindow.IsOpen = true;
        }
    }

    // -------------------------

    internal void SendStartEnsemble()
    {
        if (!Plugin.Config.playOnMultipleDevices)
        {
            return;
        }

        Chat.SendMessage($"/p startensemble");
    }

    private void HandleStartEnsemble(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !DalamudApi.PartyList.IsPartyLeader())
            return;

        Plugin.EnsembleManager.BeginEnsembleReadyCheck();
    }

    // -------------------------

    internal void SendStopEnsemble()
    {
        if (!Plugin.Config.playOnMultipleDevices)
        {
            return;
        }

        Chat.SendMessage($"/p stopensemble");
    }

    private void HandleStopEnsemble(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !DalamudApi.PartyList.IsPartyLeader())
            return;

        // StopEnsemble();
        if (Plugin.Config.playOnMultipleDevices && DalamudApi.PartyList.Length > 1)
        {
            Plugin.ChatWatcher.SendClose();
        }
        else if (DalamudApi.PartyList.Length <= 1)
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(0);
            Plugin.MidiPlayerControl.Stop();
            return;
        }
        else
        {
            Plugin.IpcProvider.UpdateInstrument(false);
        }
    }

    // -------------------------

    internal void SendPlay()
    {
        if (!Plugin.Config.playOnMultipleDevices)
        {
            return;
        }

        Chat.SendMessage($"/p play");
    }

    private void HandlePlay(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices)
            return;

        Plugin.MidiPlayerControl.PlayPause();
    }

    // -------------------------

    internal void SendStop()
    {
        if (!Plugin.Config.playOnMultipleDevices)
        {
            return;
        }

        Chat.SendMessage($"/p stop");
    }

    private void HandleStop(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices)
            return;

        if (Plugin.FilePlayback.IsWaiting)
        {
            Plugin.FilePlayback.CancelWaiting();
        }
        else
        {
            Plugin.MidiPlayerControl.Stop();
            Plugin.InstrumentSwitcher.SwitchToAsync(0);
        }
    }

    // -------------------------

    internal void SendRemoveSong(int songIndex)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p playlistremove {songIndex + 1}");
    }

    private void HandleRemoveSong(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (int.TryParse(args[0], out int songIndex))
        {
            Plugin.PlaylistManager.RemoveSongLocal(songIndex - 1);
        }
    }

    // -------------------------

    internal void SendChangeSongOrder(int songIndex, int targetIndex)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p playlistmove {songIndex + 1} {targetIndex + 1}");
    }

    private void HandleChangeSongOrder(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || args.Length < 2)
            return;

        if (int.TryParse(args[0], out int fromIndex) && int.TryParse(args[1], out int toIndex))
        {
            Plugin.PlaylistManager.MoveSongToIndexLocal(fromIndex - 1, toIndex - 1);
        }
    }

    // -------------------------

    internal void SendChangeSpeed(float speed)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p speed {speed}");
    }

    private void HandleChangeSpeed(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (float.TryParse(args[0], out float speed))
        {
            Plugin.Config.PlaySpeed = Math.Max(0.1f, speed);
        }
    }

    // -------------------------

    internal void SendSetGlobalTranspose(int transpose)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p transpose {transpose}");
    }

    private void HandleSetGlobalTranspose(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (int.TryParse(args[0], out int transpose))
        {
            Plugin.Config.SetTransposeGlobal(transpose, Plugin);
        }
    }

    // -------------------------

    internal void SendClose()
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage("/p close");
    }

    private void HandleClose(string[] args)
    {
        Plugin.MidiPlayerControl.Stop();
        Plugin.InstrumentSwitcher.SwitchToAsync(0);
    }

    // -------------------------

    internal void SendReloadPlaylist()
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p reloadplaylist");
    }

    private void HandleReloadPlaylist(string[] args)
    {
        Plugin.PlaylistManager.LoadLastPlaylist();
    }

    // -------------------------

    internal void SendUpdateDefaultPerformer()
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p updatedefaultperformer");
    }

    private void HandleUpdateDefaultPerformer(string[] args)
    {
        Plugin.MidiFileConfigManager.LoadDefaultPerformer();
    }

    // -------------------------

    internal void SendUpdateInstrument()
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p updateinstrument");
    }

    private void HandleUpdateInstrument(string[] args)
    {
        if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            return;
        }

        Plugin.CurrentBardPlayback.SyncTrackStatusWithMidiFileConfig();
        uint instrumentId = Plugin.CurrentBardPlayback.GetInstrumentId();

        Plugin.InstrumentSwitcher.SwitchToContinue(instrumentId);
    }

    // -------------------------

    internal void SendDownloadSong(string url)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || !Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
            return;
        Chat.SendMessage($"/p downloadsong {url}");
    }

    private void HandleDownloadSong(string[] args)
    {
        if (!args[0].IsNullOrEmpty())
        {
            DalamudApi.PluginLog.Debug("download");
            XIVMIDI.Instance.AddToQueue(new GetRequest()
            {
                Url = args[0],
                Host = "xivmidi.com",
                Accept = "audio/midi",
                Requester = Requester.DOWNLOAD
            });
        }
    }
}

