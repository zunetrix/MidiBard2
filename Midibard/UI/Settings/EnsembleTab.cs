using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using MidiBard.Resources;

namespace MidiBard;

public partial class PluginUI
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
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_sync_clients);

        ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.GetFrameHeightWithSpacing() - ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.ExchangeAlt).X);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "##btnSyncSettings", Language.icon_button_tooltip_sync_settings))
        {
            IPCHandles.SyncAllSettings();
            IPCHandles.SyncPlaylist();
            ImGuiUtil.AddNotification(NotificationType.Info, "Synced settings and playlist");
        }

        //-------------------

        if (ImGui.Checkbox(Language.setting_label_monitor_ensemble, ref Plugin.Config.MonitorOnEnsemble))
        {
            IPCHandles.SyncAllSettings();
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
        ImGuiUtil.ToolTip("Choose this if your bards are spread between different devices.");

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
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Using File Sharing Services like Google Drive to sync songs and performer settings.");
            ImGui.Unindent();
        }

        if (ImGui.Checkbox(Language.setting_label_ignore_default_performer, ref Plugin.Config.lockTracks))
        {
            IPCHandles.SyncAllSettings();
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
        ImGui.TextUnformatted(Language.ensemble_compensation_mode);
        if (ImGuiUtil.EnumCombo($"##comboCompensationMode", ref Plugin.Config.CompensationMode, labelsOverride: GetCompensationModeLabels()))
        {
            IPCHandles.SyncAllSettings();
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

        DrawLyricsOptions();

        ImGuiUtil.Spacing(3);

        DrawDefaultPerformerOptions();

        ImGuiUtil.Spacing(3);

        DrawDefaultPlaylistOptions();

        ImGuiUtil.Spacing(3);

        DrawEnsembleMembersSettings();
    }

    private void DrawLyricsOptions()
    {
        if (ImGui.CollapsingHeader(Language.lyrics, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(Language.setting_tooltip_play_lyrics, ref Plugin.Config.playLyrics))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker(Language.display_lyrics_tooltip);

            var btnNameReferencesize = ImGuiHelpers.GetButtonSize(Language.button_export_lrc_template);
            // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X); // end of line
            ImGui.SameLine();
            if (ImGui.Button(Language.button_export_lrc_template))
            {
                LyricsPlayer.ExportLrcTemplate();
                Util.Extensions.OpenFolder(Plugin.Config.defaultPerformerFolder);
                ImGuiUtil.AddNotification(NotificationType.Success, $"Lrc template exported");
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.TextUnformatted(Language.select_chat_to_send_lyrics);
            if (ImGuiUtil.EnumCombo($"##comboLyricsChatTarget", ref Plugin.Config.LyricsChatTarget, labelsOverride: GetLyricsChatTargetLabels()))
            {
                IPCHandles.SyncAllSettings();
            }

            ImGui.Unindent();
        }
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

            ImGui.TextUnformatted(Path.ChangeExtension(Plugin.Config.defaultPerformerFolder, null).EllipsisString(40));

            ImGui.SameLine();
            ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenDefaultPerformerFolder", Language.open_folder))
            {
                Util.Extensions.OpenFolder(Plugin.Config.defaultPerformerFolder);
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##BtnChangeDefaultPerformerFolder", Language.change_folder))
            {
                RunSetDefaultPerformerFolderImGui();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##BtnResetDefaultPerformerFolder", "Reset default performer"))
            {
                MidiFileConfigManager.ResetDefaultPerformer();
            }

            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(Language.settin_label_default_performer_tracks);

            var partyMembers = DalamudApi.PartyList
                .Select(partyMember => partyMember.GetPartyMemberData())
                .Where(partyMember => MidiFileConfigManager.defaultPerformer.TrackMappingDict.ContainsKey(partyMember.Cid))
                .ToList();

            if (partyMembers.Count == 0)
            {
                ImGui.Indent();
                ImGui.TextUnformatted(Language.setting_label_empty);
                ImGui.Unindent();
            }

            foreach (var partyMember in partyMembers)
            {
                var playerInfo = $"{partyMember.Name}@{partyMember.World}";
                var playerTrackList = MidiFileConfigManager.defaultPerformer.TrackMappingDict.GetValueOrDefault(partyMember.Cid).ToList();
                var playerTracks = string.Join(", ", playerTrackList.Select(n => n + 1));
                ImGui.TextUnformatted($"{playerInfo}");
                ImGui.Indent();
                ImGui.TextUnformatted($"Tracks: {playerTracks}");
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


            ImGui.TextUnformatted(Path.ChangeExtension(Plugin.Config.defaultPlaylistFolder, null).EllipsisString(40));

            ImGui.SameLine();
            ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenDefaultPlaylistFolder", Language.open_folder))
            {
                Util.Extensions.OpenFolder(Plugin.Config.defaultPlaylistFolder);
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
        fileDialogManager.OpenFolderDialog("Set Default Performer Folder", (result, filePath) =>
        {
            // DalamudApi.PluginLog.Debug($"dialog result: {result}\n{string.Join("\n", filePath)}");
            if (result)
            {
                MidiFileConfigManager.SetDefaultPerformerFolder(filePath);
                Plugin.SaveConfig();
                IPCHandles.SyncAllSettings();
                IPCHandles.UpdateDefaultPerformer();
            }
        }, Plugin.Config.defaultPerformerFolder);
    }

    private void RunSetDefaultPlaylistFolderImGui()
    {
        fileDialogManager.OpenFolderDialog("Set Default Playlist Folder", (result, filePath) =>
        {
            if (result)
            {
                Plugin.Config.defaultPlaylistFolder = filePath;
                Plugin.SaveConfig();
                IPCHandles.SyncAllSettings();
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
                    ImGui.Image(instrument.IconTextureWrap.GetWrapOrEmpty().Handle, new Vector2(ImGui.GetFrameHeight()));
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(SanitizeIntrumentName(instrument.FFXIVDisplayName));
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var compensationMs = Plugin.Config.ManualInstrumentCompensation[(int)instrument.Row.RowId];
                    if (ImGui.InputInt($"##{instrument.Row.RowId}", ref compensationMs, 1, 1))
                    {
                        compensationMs = compensationMs.Clamp(0, 500);
                        Plugin.Config.ManualInstrumentCompensation[(int)instrument.Row.RowId] = compensationMs;
                        IPCHandles.SyncAllSettings();
                    }
                }
                ImGui.EndTable();
            }

            if (ImGui.Button("Reset to default values"))
            {
                Plugin.Config.ManualInstrumentCompensation = EnsembleManager.GetCompensationAver();
                IPCHandles.SyncAllSettings();
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
            ImGui.TextUnformatted(Language.display_order);
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
                    ImGui.TextUnformatted($"{i + 1:00}");

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
                        ImGui.TextUnformatted($"{Plugin.Config.EnsembleMemberConfigs[i].LinkedEnsembleMembers[j].Name}");
                        ImGui.SameLine();
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.Unlink, $"##UnlinkEnsembleMemberConfig_{j}", "Unlink Ensemble Member"))
                        {
                            Plugin.Config.UnlinkEnsembleMember(
                                Plugin.Config.EnsembleMemberConfigs[i].Cid,
                                Plugin.Config.EnsembleMemberConfigs[i].LinkedEnsembleMembers[j].Cid
                            );
                            IPCHandles.SyncAllSettings();
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
                        ImGui.TextUnformatted("Associate with:");
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
                                IPCHandles.SyncAllSettings();
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
            ImGui.TextUnformatted(Language.available_party_members);
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
                            IPCHandles.SyncAllSettings();
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
