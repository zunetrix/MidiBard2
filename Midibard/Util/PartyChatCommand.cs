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
                ["reloadconfig"] = _ => IPCHandles.SyncAllSettings(),
                ["reloadplaylist"] = _ => PlaylistManager.CurrentContainer = PlaylistManager.LoadLastPlaylist(),
                ["updatedefaultperformer"] = _ => MidiFileConfigManager.LoadDefaultPerformer(),
                ["updateinstrument"] = _ => UpdateInstrument(),
                ["close"] = _ =>
                {
                    MidiPlayerControl.Stop();
                    SwitchInstrument.SwitchToAsync(0);
                },
                ["speed"] = HandleSpeed,
                ["transpose"] = HandleTranspose,
            };

            if (commands.TryGetValue(cmd, out var action))
            {
                action.Invoke(args);
            }
        }

        // -------------------------
        // Handlers
        // -------------------------

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

        private static void HandleRemoveSong(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (int.TryParse(args[0], out int songIndex))
            {
                PlaylistManager.RemoveLocal(songIndex - 1);
            }
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

        private static void HandleSpeed(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (float.TryParse(args[0], out float speed))
            {
                MidiBard.config.PlaySpeed = Math.Max(0.1f, speed);
            }
        }

        private static void HandleTranspose(string[] args)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2 || args.Length < 1)
                return;

            if (int.TryParse(args[0], out int transpose))
            {
                MidiBard.config.SetTransposeGlobal(transpose);
            }
        }

        // -------------------------
        // Commands
        // -------------------------

        internal static void SendClose()
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage("/p close");
        }

        internal static void SendSwitchTo(int songNumber)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p switchto {songNumber}");
        }

        internal static void SendPMD(bool isOn)
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            var str = isOn ? "on" : "off";
            Chat.SendMessage($"/p pmd {str}");
        }

        internal static void SendUseChatPlaylistSync(bool isOn)
        {
            if (!MidiBard.config.playOnMultipleDevices || api.PartyList.Length < 2)
            {
                return;
            }

            var str = isOn ? "on" : "off";
            Chat.SendMessage($"/p usechatplaylistsync {str}");
        }

        internal static void SendReloadPlaylist()
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p reloadplaylist");
        }

        internal static void SendUpdateDefaultPerformer()
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p updatedefaultperformer");
        }

        internal static void SendUpdateInstrument()
        {
            if (api.PartyList.Length < 2)
            {
                return;
            }

            Chat.SendMessage($"/p updateinstrument");
        }

        internal static void SendRemoveSong(int songIndex)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || !api.PartyList.IsPartyLeader())
            {
                return;
            }

            Chat.SendMessage($"/p playlistremove {songIndex + 1}");
        }

        internal static void SendChangeSongOrder(int songIndex, int targetIndex)
        {
            if (!MidiBard.config.playOnMultipleDevices || !MidiBard.config.useChatPlaylistSync || api.PartyList.Length < 2 || !api.PartyList.IsPartyLeader())
            {
                return;
            }

            Chat.SendMessage($"/p playlistmove {songIndex + 1} {targetIndex + 1}");
        }

        private static void UpdateInstrument()
        {
            // updates midifile config and instruments
            // code copied from IPCHandles.cs
            if (CurrentPlayback == null)
            {
                return;
            }

            var dbTracks = MidiBard.CurrentPlayback.MidiFileConfig.Tracks;
            var trackStatus = MidiBard.config.TrackStatus;
            for (var i = 0; i < dbTracks.Count; i++)
            {
                try
                {
                    trackStatus[i].Enabled = dbTracks[i].Enabled && MidiFileConfig.GetFirstCidInParty(dbTracks[i]) == (long)api.ClientState.LocalContentId;
                    trackStatus[i].Transpose = dbTracks[i].Transpose;
                    trackStatus[i].Tone = Util.InstrumentHelper.GetGuitarTone(dbTracks[i].Instrument);
                }
                catch (Exception e)
                {
                    PluginLog.Error(e, $"error when updating track {i}");
                }
            }

            uint? instrument = null;
            foreach (var track in MidiBard.CurrentPlayback.MidiFileConfig.Tracks)
            {
                if (track.Enabled && MidiFileConfig.IsCidOnTrack((long)api.ClientState.LocalContentId, track))
                {
                    instrument = (uint?)track.Instrument;
                    break;
                }
            }

            if (instrument != null)
                SwitchInstrument.SwitchToContinue((uint)instrument);

            PluginLog.Debug($"Instrument: {instrument}");
        }
    }
}
