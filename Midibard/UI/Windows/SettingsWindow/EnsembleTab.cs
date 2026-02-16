using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using MidiBard.Managers;

using MidiBard.Resources;
using MidiBard.Util.ImGuiExt;
using MidiBard.Util;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.String;
using MidiBard.Extensions.Dalamud.Texture;
using MidiBard.Extensions.List;
using MidiBard.Extensions.General;

namespace MidiBard;

public partial class SettingsWindow
{
    private static string[] GetCompensationModeLabels()
    {
        string[] compensationModeLabels = [
                Language.compensation_mode_option_none,
                Language.compensation_mode_option_manual,
                Language.compensation_mode_option_default
            ];

        return compensationModeLabels;
    }

    private static string[] GetLyricsChatTargetLabels()
    {
        string[] lyricsChatTargetLabels = [
                Language.chat_target_option_current,
                Language.chat_target_option_say,
                Language.chat_target_option_party
            ];

        return lyricsChatTargetLabels;
    }

    private void DrawEnsembleSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(Language.setting_group_label_ensemble_settings);
        if (ImGui.Checkbox(Language.setting_label_sync_clients, ref Plugin.Config.SyncClients))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_sync_clients);

        ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.GetFrameHeightWithSpacing() - ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.ExchangeAlt).X);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "##btnSyncSettings", Language.icon_button_tooltip_sync_settings))
        {
            Plugin.IpcProvider.SyncAllSettings();
            Plugin.IpcProvider.SyncPlaylist();
            ImGuiUtil.AddNotification(NotificationType.Info, "Synced settings and playlist");
        }

        //-------------------

        if (ImGui.Checkbox(Language.setting_label_monitor_ensemble, ref Plugin.Config.MonitorOnEnsemble))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_monitor_ensemble);

        //-------------------

        bool pmdWasOn = Plugin.Config.playOnMultipleDevices;
        if (ImGui.Checkbox(Language.play_on_multiple_devices, ref Plugin.Config.playOnMultipleDevices))
        {
            if (pmdWasOn || Plugin.Config.playOnMultipleDevices)
            {
                Plugin.PartyChatCommand.SendPlayOnMultipleDevices(Plugin.Config.playOnMultipleDevices);
            }
        }
        ImGui.SameLine();
        ImGuiUtil.HelpMarker("""
        Choose this if your bards are spread between different devices.
        Enables Party Chat Commands:
            pmd => Toggle play on multiple devices
            switchto [song index] => switch to song number
            startensemble => start ensemble
            stopensemble => stop ensemble

            play => start solo play
            stop => stop solo play
            close => stop playing

            speed [speed amount] => set playback speed
            transpose [transpose amount] => transpose global by octaves
            updateinstrument => update instrument
            updatedefaultperformer => updatedefaultperformer

            usechatplaylistsync =>use chat playlist sync, allow control playlist via chat
            playlistmove [song index] [to position] => move
            playlistremove [song index] => remove song from playlist
            reloadplaylist => reload playlist

            downloadsong [song url] => download song from xivmidi.com
        """);
        ImGui.Spacing();

        bool chatPlaylistSyncWasOn = Plugin.Config.useChatPlaylistSync;
        if (Plugin.Config.playOnMultipleDevices)
        {
            ImGui.Indent();
            if (ImGui.Checkbox("Use party chat for playlist sync", ref Plugin.Config.useChatPlaylistSync))
            {
                if (chatPlaylistSyncWasOn || Plugin.Config.useChatPlaylistSync)
                {
                    Plugin.PartyChatCommand.SendUseChatPlaylistSync(Plugin.Config.useChatPlaylistSync);
                }
            }
            ImGuiUtil.HelpMarker("When this option is active, only the party leader can remove and reorder songs from the playlist, these options are blocked for other members.");

            ImGuiUtil.Spacing(2);

            if (ImGui.Checkbox("Using File Sharing Services", ref Plugin.Config.usingFileSharingServices))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Using File Sharing Services like Google Drive to sync songs and performer settings.");
            ImGui.Unindent();
        }

        if (ImGui.Checkbox(Language.setting_label_ignore_default_performer, ref Plugin.Config.lockTracks))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Ignores the default performer settings");

        if (!Plugin.Config.playOnMultipleDevices)
        {
            ImGui.Checkbox(Language.ensemble_config_update_instrument_when_begin_ensemble, ref Plugin.Config.UpdateInstrumentBeforeReadyCheck);
            ImGuiUtil.ToolTip("Update instruments before start ensemble (Local bards only)");
        }

        //-------------------

        ImGui.Checkbox(Language.ensemble_config_draw_ensemble_progress_indicator_on_visualizer, ref Plugin.Config.UseEnsembleIndicator);

        //-------------------

        ImGui.AlignTextToFramePadding();
        ImGui.Text(Language.ensemble_compensation_mode);
        if (ImGuiUtil.EnumCombo($"##comboCompensationMode", ref Plugin.Config.CompensationMode, labelsOverride: GetCompensationModeLabels()))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }

        ImGuiUtil.HelpMarker("""
          Ensemble instrument compensation mode selection:

          None:
          No instrument delay compensation for instruments is performed during ensemble mode, which may result a lack of alignment between instruments during ensemble play.Choose this option only if your MIDI file already has instrument delay compensation.

          Manual:
          Allows you to adjust the delay compensation value for each instrument, but notes of different pitches for the same instrument may not align perfectly.

          Default:
          New default instrument delay compensation mode, with different compensation times for notes of different pitches, useful for instruments such as clarinet and bass drum.
          """);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Indent();
        if (Plugin.Config.CompensationMode == CompensationModes.ByInstrument)
        {
            if (ImGui.Button("Edit Instrument Compensations"))
            {
                showCompensationEditWindow ^= true;
            }
        }
        ImGui.Unindent();

        //-------------------

        ImGuiGroupPanel.EndGroupPanel();

        ImGuiUtil.Spacing(3);

        DrawDefaultPerformerOptions();

        ImGuiUtil.Spacing(3);

        DrawDefaultPlaylistOptions();

        ImGuiUtil.Spacing(3);

        DrawEnsembleMembersSettings();
    }

    private void DrawDefaultPerformerOptions()
    {
        if (ImGui.CollapsingHeader(Language.setting_label_default_performer, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();
            ImGui.Text(Language.default_performer_folder);
            ImGuiUtil.HelpMarker("""
            The default performer is a configuration file used by the ensemble to assign default tracks to bards.
            You can set it up in the ensemble panel by assigning tracks to each bard and then using the Export to Default Performer option.
            This way, every time you load a song, the bards will always have the same tracks assigned. If a specific JSON configuration file exists for the song, it will override this configuration.
            """);

            ImGui.Text(Path.ChangeExtension(Plugin.Config.defaultPerformerFolder, null).EllipsisPath(40));

            ImGui.SameLine();
            ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenDefaultPerformerFolder", Language.open_folder))
            {
                WindowsApi.OpenFolder(Plugin.Config.defaultPerformerFolder);
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##BtnChangeDefaultPerformerFolder", Language.change_folder))
            {
                RunSetDefaultPerformerFolderImGui();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##BtnResetDefaultPerformerFolder", "Reset default performer"))
            {
                Plugin.MidiFileConfigManager.ResetDefaultPerformer();
            }

            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(Language.settin_label_default_performer_tracks);

            var partyMembers = DalamudApi.PartyList
                .Select(partyMember => partyMember.GetPartyMemberData())
                .Where(partyMember => Plugin.MidiFileConfigManager.defaultPerformer.TrackMappingDict.ContainsKey(partyMember.Cid))
                .ToList();

            if (partyMembers.Count == 0)
            {
                ImGui.Indent();
                ImGui.Text(Language.setting_label_empty);
                ImGui.Unindent();
            }

            foreach (var partyMember in partyMembers)
            {
                var playerInfo = $"{partyMember.Name}@{partyMember.World}";
                var playerTrackList = Plugin.MidiFileConfigManager.defaultPerformer.TrackMappingDict.GetValueOrDefault(partyMember.Cid).ToList();
                var playerTracks = string.Join(", ", playerTrackList.Select(n => n + 1));
                ImGui.Text($"{playerInfo}");
                ImGui.Indent();
                ImGui.Text($"Tracks: {playerTracks}");
                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Unindent();
        }
    }

    private void DrawDefaultPlaylistOptions()
    {
        if (ImGui.CollapsingHeader(Language.setting_label_default_playlist, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();
            ImGui.Text(Language.default_playlist_folder);


            ImGui.Text(Path.ChangeExtension(Plugin.Config.defaultPlaylistFolder, null).EllipsisPath(40));

            ImGui.SameLine();
            ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenDefaultPlaylistFolder", Language.open_folder))
            {
                WindowsApi.OpenFolder(Plugin.Config.defaultPlaylistFolder);
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##BtnChangeDefaultPlaylistFolder", Language.change_folder))
            {
                RunSetDefaultPlaylistFolderImGui();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##BtnResetDefaultPlaylistFolder", "Reset default playlist"))
            {
                Plugin.Config.defaultPlaylistFolder = DalamudApi.PluginInterface.ConfigDirectory.FullName;
                ImGuiUtil.AddNotification(NotificationType.Info, $"Default playlist folder reseted");
            }

            ImGui.Spacing();
            ImGui.Unindent();
        }
    }

    private void RunSetDefaultPerformerFolderImGui()
    {
        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Set Default Performer Folder", (result, filePath) =>
        {
            // DalamudApi.PluginLog.Debug($"dialog result: {result}\n{string.Join("\n", filePath)}");
            if (result)
            {
                Plugin.MidiFileConfigManager.SetDefaultPerformerFolder(filePath);
                Plugin.SaveConfig();
                Plugin.IpcProvider.SyncAllSettings();
                Plugin.IpcProvider.UpdateDefaultPerformer();
            }
        }, Plugin.Config.defaultPerformerFolder);
    }

    private void RunSetDefaultPlaylistFolderImGui()
    {
        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Set Default Playlist Folder", (result, filePath) =>
        {
            if (result)
            {
                Plugin.Config.defaultPlaylistFolder = filePath;
                Plugin.SaveConfig();
                Plugin.IpcProvider.SyncAllSettings();
            }
        }, Plugin.Config.defaultPlaylistFolder);
    }

    private void DrawCompensationEditWindow()
    {
        if (!showCompensationEditWindow) return;

        if (ImGui.Begin("Instrument Delay Compensation", ref showCompensationEditWindow))
        {
            if (ImGui.BeginTable("ins", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Instrument", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Compensation(ms)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var instrument in Plugin.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight()));
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(SanitizeIntrumentName(instrument.FFXIVDisplayName));
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var compensationMs = Plugin.Config.ManualInstrumentCompensation[(int)instrument.Row.RowId];
                    if (ImGui.InputInt($"##{instrument.Row.RowId}", ref compensationMs, 1, 1))
                    {
                        compensationMs = compensationMs.Clamp(0, 500);
                        Plugin.Config.ManualInstrumentCompensation[(int)instrument.Row.RowId] = compensationMs;
                        Plugin.IpcProvider.SyncAllSettings();
                    }
                }
                ImGui.EndTable();
            }

            if (ImGui.Button("Reset to default values"))
            {
                Plugin.Config.ManualInstrumentCompensation = EnsembleManager.GetCompensationAver();
                Plugin.IpcProvider.SyncAllSettings();
            }
        }
        ImGui.End();
    }

    private void DrawEnsembleMembersSettings()
    {
        if (ImGui.CollapsingHeader(Language.ensemble_party_members, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Indent();

            var partyMembers = DalamudApi.PartyList.Select((partyMember) => partyMember.GetPartyMemberData()).ToList();
            ImGui.Text(Language.display_order);
            ImGuiUtil.HelpMarker("""
            The order used to show bards in the ensemble panel (Drag to reorder)

            Linked members let you automatically apply the same JSON configuration to multiple performers.
            Any track assigned to the parent member is also assigned to the linked member (handy when you are running  band across different regions)
            """);
            ImGui.Spacing();

            if (ImGui.BeginTable("##EnsembleMemberTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed);

                for (int i = 0; i < Plugin.Config.EnsembleMemberConfigs.Count; i++)
                {
                    ImGui.PushID(i);
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text($"{i + 1:00}");

                    ImGui.TableNextColumn();
                    ImGui.Selectable($"{Plugin.Config.EnsembleMemberConfigs[i].Name}");
                    if (ImGui.BeginDragDropSource())
                    {
                        unsafe
                        {
                            ImGui.SetDragDropPayload("DND_ENSEMBLE_MEMBER", new ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                            ImGui.Button($"({i + 1}) {Plugin.Config.EnsembleMemberConfigs[i].Name}");
                        }

                        // DalamudApi.PluginLog.Warning($"Drag start [{i}]: {MidiBard.Plugin.Config.EnsembleMemberConfigs[i].Name}");
                        ImGui.EndDragDropSource();
                    }

                    ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget);
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_ENSEMBLE_MEMBER");

                        bool isDropping = false;
                        unsafe
                        {
                            isDropping = !dragDropPayload.IsNull;
                        }

                        if (isDropping && dragDropPayload.IsDelivery())
                        {
                            unsafe
                            {
                                int originalIndex = *(int*)dragDropPayload.Data;

                                int offset = i - originalIndex;
                                if (offset != 0 && originalIndex + offset >= 0)
                                {
                                    int targetIndex = originalIndex + offset;
                                    // DalamudApi.PluginLog.Warning($"Drag end [{i}]: [{originalIndex}, {targetIndex}] {offset}");
                                    Plugin.Config.EnsembleMemberConfigs.MoveItemToIndex(originalIndex, targetIndex);
                                }
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.PopStyleColor();

                    ImGui.Indent(20);
                    for (int j = 0; j < Plugin.Config.EnsembleMemberConfigs[i].LinkedEnsembleMembers.Count; j++)
                    {
                        ImGui.Text($"{Plugin.Config.EnsembleMemberConfigs[i].LinkedEnsembleMembers[j].Name}");
                        ImGui.SameLine();
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.Unlink, $"##UnlinkEnsembleMemberConfig_{j}", "Unlink Ensemble Member"))
                        {
                            Plugin.Config.UnlinkEnsembleMember(
                                Plugin.Config.EnsembleMemberConfigs[i].Cid,
                                Plugin.Config.EnsembleMemberConfigs[i].LinkedEnsembleMembers[j].Cid
                            );
                            Plugin.IpcProvider.SyncAllSettings();
                        }
                    }
                    ImGui.Unindent();

                    ImGui.TableNextColumn();

                    bool isLinkDisabled = Plugin.Config.EnsembleMemberConfigs[i].LinkedEnsembleMembers.Count != 0;
                    ImGui.BeginDisabled(isLinkDisabled);
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Link, $"##LinkEnsembleMemberConfig_{i}", "Link Ensemble Member"))
                    {
                        ImGui.OpenPopup($"LinkEnsembleMember");
                    }

                    if (ImGui.BeginPopup($"LinkEnsembleMember"))
                    {
                        ImGui.Text("Associate with:");
                        ImGui.Separator();

                        for (int t = 0; t < Plugin.Config.EnsembleMemberConfigs.Count; t++)
                        {
                            if (t == i) continue; // cannot link to itself

                            var target = Plugin.Config.EnsembleMemberConfigs[t];

                            if (ImGui.MenuItem(target.Name))
                            {
                                Plugin.Config.LinkEnsembleMember(
                                    Plugin.Config.EnsembleMemberConfigs[i].Cid,
                                    target.Cid
                                );
                                Plugin.IpcProvider.SyncAllSettings();
                            }
                        }

                        ImGui.EndPopup();
                    }
                    ImGui.EndDisabled();

                    ImGui.SameLine();
                    if (ImGui.Button($"↑##MoveUpEnsembleMemberConfig_{i}"))
                        Plugin.Config.EnsembleMemberConfigs.MoveItemToIndex(i, i - 1);

                    ImGui.SameLine();
                    if (ImGui.Button($"↓##MoveDownEnsembleMemberConfig_{i}"))
                        Plugin.Config.EnsembleMemberConfigs.MoveItemToIndex(i, i + 1);

                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemoveEnsembleMemberConfig_{i}", "Delete"))
                        Plugin.Config.EnsembleMemberConfigs.SafeRemoveAt(i);

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            bool allPartyMembersInConfig = partyMembers.All(partyMember => ContainsCidDeep(Plugin.Config.EnsembleMemberConfigs, partyMember.Cid));

            ImGui.BeginDisabled(allPartyMembersInConfig);
            ImGui.Text(Language.available_party_members);
            if (ImGui.BeginCombo("##partyMemberSelectList", "Select"))
            {
                foreach (var partyMember in partyMembers)
                {
                    bool isCidUsed = ContainsCidDeep(Plugin.Config.EnsembleMemberConfigs, partyMember.Cid);
                    if (!isCidUsed)
                    {
                        var playerInfo = $"{partyMember.Name}@{partyMember.World}";
                        if (ImGui.Selectable($"{playerInfo}##{partyMember.Cid}", false))
                        {
                            var newMember = new EnsembleMemberConfig
                            {
                                Cid = partyMember.Cid,
                                Name = playerInfo,
                                TrackAssignmentRegex = "",
                                LinkedEnsembleMembers = new()
                            };

                            Plugin.Config.AddEnsembleMemberConfig(newMember);
                            Plugin.IpcProvider.SyncAllSettings();
                        }
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            ImGui.Unindent();
        }
    }

    public static bool ContainsCidDeep(
    List<EnsembleMemberConfig> list,
    long cid)
    {
        foreach (var config in list)
        {
            if (config.Cid == cid)
                return true;

            if (config.LinkedEnsembleMembers?.Any(m => m.Cid == cid) ?? false)
                return true;
        }

        return false;
    }
}
