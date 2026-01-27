using System;
using System.Collections.Generic;
using System.Linq;

using BardMusicPlayer.XIVMIDI;
using BardMusicPlayer.XIVMIDI.IO;

using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;

namespace MidiBard;

internal static class PartyChatCommand
{
    private static readonly Dictionary<string, Action<string[]>> CommandHandlers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["playonmultipledevices"] = HandlePlayOnMultipleDevices,
            ["pmd"] = HandlePlayOnMultipleDevices,
            ["switchto"] = HandleSwitchTo,
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
        };

    internal static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
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

        // api.DalamudApi.PluginLog.Warning($"OnChatMessage [{cmd}] ({args.JoinString(", ")})");

        if (CommandHandlers.TryGetValue(cmd, out var action))
        {
            action.Invoke(args);
        }
    }

    internal static void SendPlayOnMultipleDevices(bool isOn)
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        var str = isOn ? "on" : "off";
        Chat.SendMessage($"/p pmd {str}");
    }

    private static void HandlePlayOnMultipleDevices(string[] args)
    {
        if (args.Length < 1)
            return;

        var value = args[0].ToLower();
        if (value == "on")
            Plugin.Config.playOnMultipleDevices = true;
        else if (value == "off")
            Plugin.Config.playOnMultipleDevices = false;
    }

    // -------------------------

    internal static void SendUseChatPlaylistSync(bool isOn)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        var str = isOn ? "on" : "off";
        Chat.SendMessage($"/p usechatplaylistsync {str}");
    }

    private static void HandleSendUseChatPlaylistSync(string[] args)
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

    internal static void SendSwitchTo(int songIndex)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p switchto {songIndex + 1}");
    }

    private static void HandleSwitchTo(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (int.TryParse(args[0], out int songIndex))
        {
            MidiPlayerControl.StopLrc();
            PlaylistManager.LoadPlayback(songIndex - 1);
            Plugin.Ui.OpenMainWindow();
        }
    }

    // -------------------------

    internal static void SendRemoveSong(int songIndex)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p playlistremove {songIndex + 1}");
    }

    private static void HandleRemoveSong(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (int.TryParse(args[0], out int songIndex))
        {
            PlaylistManager.RemoveLocal(songIndex - 1);
        }
    }

    // -------------------------

    internal static void SendChangeSongOrder(int songIndex, int targetIndex)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p playlistmove {songIndex + 1} {targetIndex + 1}");
    }

    private static void HandleChangeSongOrder(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || args.Length < 2)
            return;

        if (int.TryParse(args[0], out int fromIndex) && int.TryParse(args[1], out int toIndex))
        {
            PlaylistManager.MoveSongToIndexLocal(fromIndex - 1, toIndex - 1);
        }
    }

    // -------------------------

    internal static void SendChangeSpeed(float speed)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p speed {speed}");
    }

    private static void HandleChangeSpeed(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (float.TryParse(args[0], out float speed))
        {
            Plugin.Config.PlaySpeed = Math.Max(0.1f, speed);
        }
    }

    // -------------------------

    internal static void SendSetGlobalTranspose(int transpose)
    {
        if (!Plugin.Config.playOnMultipleDevices || !Plugin.Config.useChatPlaylistSync || DalamudApi.PartyList.Length < 2 || !DalamudApi.PartyList.IsPartyLeader())
        {
            return;
        }

        Chat.SendMessage($"/p transpose {transpose}");
    }

    private static void HandleSetGlobalTranspose(string[] args)
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2 || args.Length < 1)
            return;

        if (int.TryParse(args[0], out int transpose))
        {
            Plugin.Config.SetTransposeGlobal(transpose);
        }
    }

    // -------------------------

    internal static void SendClose()
    {
        if (!Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage("/p close");
    }

    private static void HandleClose(string[] args)
    {
        MidiPlayerControl.Stop();
        SwitchInstrument.SwitchToAsync(0);
    }

    // -------------------------

    internal static void SendReloadPlaylist()
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p reloadplaylist");
    }

    private static void HandleReloadPlaylist(string[] args)
    {
        PlaylistManager.CurrentContainer = PlaylistManager.LoadLastPlaylist();
    }

    // -------------------------

    internal static void SendUpdateDefaultPerformer()
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p updatedefaultperformer");
    }

    private static void HandleUpdateDefaultPerformer(string[] args)
    {
        MidiFileConfigManager.LoadDefaultPerformer();
    }

    // -------------------------

    internal static void SendUpdateInstrument()
    {
        if (DalamudApi.PartyList.Length < 2)
        {
            return;
        }

        Chat.SendMessage($"/p updateinstrument");
    }

    private static void HandleUpdateInstrument(string[] args)
    {
        if (Plugin.CurrentBardPlayback == null)
        {
            return;
        }

        Plugin.CurrentBardPlayback.SyncTrackStatusWithMidiFileConfig();
        uint instrumentId = Plugin.CurrentBardPlayback.GetInstrumentId();

        SwitchInstrument.SwitchToContinue(instrumentId);
    }

    // -------------------------

    internal static void SendDownloadSong(string url)
    {
        if (!DalamudApi.PartyList.IsPartyLeader() || !Plugin.Config.playOnMultipleDevices || DalamudApi.PartyList.Length < 2)
            return;
        Chat.SendMessage($"/p downloadsong {url}");
    }

    private static void HandleDownloadSong(string[] args)
    {
        if (!args[0].IsNullOrEmpty())
        {
            DalamudApi.LogDebug("download");
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

