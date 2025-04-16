using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;

using static Dalamud.api;
using static MidiBard.MidiBard;

namespace MidiBard
{
    internal class PartyChatCommand
    {
        internal static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            if (isHandled || type != XivChatType.Party)
                return;

            string[] parts = message.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1)
                return;

            string cmd = parts[0].ToLower();
            string[] args = parts.Skip(1).ToArray();

            // PluginLog.Debug($"OnChatMessage [{cmd}] ({args.JoinString(", ")})");

            var commands = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase)
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
                // ["reportplayback"] = HandleReportLoadedPlaybackInfo,
            };

            if (commands.TryGetValue(cmd, out var action))
            {
                action.Invoke(args);
            }
        }

        internal static void SendPlayOnMultipleDevices(bool isOn)
        {
            if (api.PartyList.Length < 2)
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
                MidiBard.config.playOnMultipleDevices = true;
            else if (value == "off")
                MidiBard.config.playOnMultipleDevices = false;
        }

        // -------------------------

        internal static void SendUseChatPlaylistSync(bool isOn)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            var str = isOn ? "on" : "off";
            Chat.SendMessage($"/p usechatplaylistsync {str}");
        }

        private static void HandleSendUseChatPlaylistSync(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices) return;

            if (args.Length < 1)
                return;

            var value = args[0].ToLower();
            if (value == "on")
                MidiBard.config.useChatPlaylistSync = true;
            else if (value == "off")
                MidiBard.config.useChatPlaylistSync = false;
        }

        // -------------------------

        internal static void SendSwitchTo(int songIndex)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p switchto {songIndex + 1}");
        }

        private static void HandleSwitchTo(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (int.TryParse(args[0], out int songIndex))
            {
                MidiPlayerControl.StopLrc();
                PlaylistManager.LoadPlayback(songIndex - 1);
                Ui.OpenMainWindow();
            }
        }

        // -------------------------

        internal static void SendRemoveSong(int songIndex)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || !api.PartyList.IsPartyLeader())
            {
                return;
            }

            Chat.SendMessage($"/p playlistremove {songIndex + 1}");
        }

        private static void HandleRemoveSong(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (int.TryParse(args[0], out int songIndex))
            {
                PlaylistManager.RemoveLocal(songIndex - 1);
            }
        }

        // -------------------------

        internal static void SendChangeSongOrder(int songIndex, int targetIndex)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || !api.PartyList.IsPartyLeader())
            {
                return;
            }

            Chat.SendMessage($"/p playlistmove {songIndex + 1} {targetIndex + 1}");
        }

        private static void HandleChangeSongOrder(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || args.Length < 2)
                return;

            if (int.TryParse(args[0], out int fromIndex) && int.TryParse(args[1], out int toIndex))
            {
                PlaylistManager.MoveSongToIndexLocal(fromIndex - 1, toIndex - 1);
            }
        }

        // -------------------------

        internal static void SendChangeSpeed(float speed)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || !api.PartyList.IsPartyLeader())
            {
                return;
            }

            Chat.SendMessage($"/p speed {speed}");
        }

        private static void HandleChangeSpeed(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (float.TryParse(args[0], out float speed))
            {
                MidiBard.config.PlaySpeed = Math.Max(0.1f, speed);
            }
        }

        // -------------------------

        internal static void SendSetGlobalTranspose(int transpose)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || !api.PartyList.IsPartyLeader())
            {
                return;
            }

            Chat.SendMessage($"/p transpose {transpose}");
        }

        private static void HandleSetGlobalTranspose(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (int.TryParse(args[0], out int transpose))
            {
                MidiBard.config.SetTransposeGlobal(transpose);
            }
        }

        // -------------------------

        internal static void SendClose()
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
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
            if (api.PartyList.Length < 2)
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
            if (api.PartyList.Length < 2)
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
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p updateinstrument");
        }

        private static void HandleUpdateInstrument(string[] args)
        {
            if (MidiBard.CurrentPlayback == null)
            {
                return;
            }

            MidiBard.CurrentPlayback.SyncTrackStatusWithMidiFileConfig();
            uint instrumentId = MidiBard.CurrentPlayback.GetInstrumentId();

            SwitchInstrument.SwitchToContinue(instrumentId);
        }

        // public static void HandleReportLoadedPlaybackInfo(string[] args)
        // {
        //     if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2 || args.Length < 1)
        //         return;

        //     if (MidiBard.CurrentPlayback == null)
        //     {
        //         Chat.SendMessage($"/p CurrentPlayback null");
        //         return;
        //     }

        //     // MidiBard.config.TrackStatus[0].Enabled;
        //     var instrumentName = GetInstrumentName(MidiBard.CurrentPlayback.GetInstrumentId());
        //     string tracks = string.Join(", ", MidiBard.config.TrackStatus
        //     .Select((t, i) => new { t, i })
        //     .Where(x => x.t.Enabled)
        //     .Select(x => x.i + 1));

        //     Chat.SendMessage($"/p {instrumentName}: [{tracks}]");

        //     static string GetInstrumentName(uint id)
        //     {
        //         var instrumentNames = new System.Collections.Generic.Dictionary<uint, string>
        //     {
        //         { 1, "harp" },
        //         { 2, "piano" },
        //         { 3, "lute" },
        //         { 4, "fiddle" },
        //         { 5, "flute" },
        //         { 6, "oboe" },
        //         { 7, "clarinet" },
        //         { 8, "fife" },
        //         { 9, "panpipes" },
        //         { 10, "timpani" },
        //         { 11, "bongo" },
        //         { 12, "bassdrum" },
        //         { 13, "snaredrum" },
        //         { 14, "cymbal" },
        //         { 15, "trumpet" },
        //         { 16, "trombone" },
        //         { 17, "tuba" },
        //         { 18, "horn" },
        //         { 19, "saxophone" },
        //         { 20, "violin" },
        //         { 21, "viola" },
        //         { 22, "cello" },
        //         { 23, "doublebass" },
        //         { 24, "electricguitaroverdriven" },
        //         { 25, "electricguitarclean" },
        //         { 26, "electricguitarmuted" },
        //         { 27, "electricguitarpowerchords" },
        //         { 28, "electricguitarspecial" }
        //     };

        //         return instrumentNames.TryGetValue(id, out var name) ? name : "unknown";
        //     }
        // }
    }
}
