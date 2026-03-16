using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.List;
using MidiBard.Extensions.String;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public sealed class EnsembleSettingsWidget : Widget
{
    public override string Title => "Ensemble";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Users;

    private static CultureInfo? _labelsCulture;
    private static string[]? _compensationModeLabels;

    private static void EnsureLabelsValid()
    {
        if (_labelsCulture == Language.Culture) return;
        _labelsCulture = Language.Culture;
        _compensationModeLabels =
        [
            Language.compensation_mode_option_none,
            Language.compensation_mode_option_manual,
            Language.compensation_mode_option_default,
        ];
    }

    public EnsembleSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        EnsureLabelsValid();
        var cfg = Context.Plugin.Config;

        //  Sync

        if (ImGui.Checkbox(Language.setting_label_sync_clients, ref cfg.SyncClients))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_sync_clients);

        ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.GetFrameHeightWithSpacing() - ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.ExchangeAlt).X);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "##sw2BtnSyncSettings", Language.icon_button_tooltip_sync_settings))
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
            ImGuiUtil.AddNotification(NotificationType.Info, "Settings synced");
        }

        if (ImGui.Checkbox(Language.setting_label_monitor_ensemble, ref cfg.MonitorOnEnsemble))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_monitor_ensemble);

        //  Multiple devices

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool pmdWasOn = cfg.playOnMultipleDevices;
        if (ImGui.Checkbox(Language.play_on_multiple_devices, ref cfg.playOnMultipleDevices))
        {
            if (pmdWasOn || cfg.playOnMultipleDevices)
                Context.Plugin.ChatWatcher.SendPlayOnMultipleDevices(cfg.playOnMultipleDevices);
        }
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

        if (cfg.playOnMultipleDevices)
        {
            using (ImRaii.PushIndent())
            {
                bool chatWasOn = cfg.useChatPlaylistSync;
                if (ImGui.Checkbox("Use party chat for playlist sync", ref cfg.useChatPlaylistSync))
                {
                    if (chatWasOn || cfg.useChatPlaylistSync)
                        Context.Plugin.ChatWatcher.SendUseChatPlaylistSync(cfg.useChatPlaylistSync);
                }
                ImGuiUtil.HelpMarker("When this option is active, only the party leader can remove and reorder songs from the playlist, these options are blocked for other members.");

                ImGuiUtil.Spacing(2);

                if (ImGui.Checkbox("Using File Sharing Services", ref cfg.usingFileSharingServices))
                    Context.Plugin.IpcProvider.SyncAllSettings();
                ImGuiUtil.ToolTip("Using File Sharing Services like Google Drive to sync songs and performer settings.");
            }
        }

        if (ImGui.Checkbox(Language.setting_label_ignore_default_performer, ref cfg.IgnoreDefaultPerformer))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip("Ignores the default performer settings");

        if (ImGui.Checkbox("Ignore JSON files", ref cfg.IgnoreJsonConfigFile))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip("Allows testing track assignment rules without using the JSON files");

        if (!cfg.playOnMultipleDevices)
        {
            ImGui.Checkbox(Language.ensemble_config_update_instrument_when_begin_ensemble, ref cfg.UpdateInstrumentBeforeReadyCheck);
            ImGuiUtil.ToolTip("Update instruments before start ensemble (Local bards only)");

            if (cfg.UpdateInstrumentBeforeReadyCheck)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
                if (ImGui.SliderInt("Pre-ready check delay (ms)##sw2PreReadyCheckDelay", ref cfg.PreReadyCheckDelayMs, 0, 3000))
                    cfg.PreReadyCheckDelayMs = Math.Clamp(cfg.PreReadyCheckDelayMs, 0, 3000);
                ImGuiUtil.ToolTip("Delay between sending instrument update and triggering the ready check.");
            }
        }

        //  Compensation

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.Text(Language.ensemble_compensation_mode);
        if (ImGuiUtil.EnumCombo("##sw2CompensationMode", ref cfg.CompensationMode, labelsOverride: _compensationModeLabels))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker("""
          Ensemble instrument compensation mode selection:

          None:
          No instrument delay compensation for instruments is performed during ensemble mode.

          Manual:
          Allows you to adjust the delay compensation value for each instrument.

          Default:
          New default instrument delay compensation mode, with different compensation times for notes of different pitches.
          """);

        ImGui.SameLine();
        using (ImRaii.Disabled(cfg.CompensationMode != CompensationModes.ByInstrument))
        {
            if (ImGui.Button("Instrument Compensations"))
                Context.Plugin.Ui.InstrumentCompensationWindow.Toggle();
        }

        ImGuiUtil.Spacing(3);

        DrawDefaultPerformerOptions();
        ImGuiUtil.Spacing(3);
        DrawDefaultPlaylistOptions();
        ImGuiUtil.Spacing(3);
        DrawEnsembleMembersSettings();
    }

    private void DrawDefaultPerformerOptions()
    {
        if (!ImGui.CollapsingHeader(Language.setting_label_default_performer, ImGuiTreeNodeFlags.NoAutoOpenOnLog)) return;

        ImGui.Spacing();
        ImGui.Indent();
        ImGui.Text(Language.default_performer_folder);
        ImGuiUtil.HelpMarker("""
            The default performer is a configuration file used by the ensemble to assign default tracks to bards.
            You can set it up in the ensemble panel by assigning tracks to each bard and then using the Export to Default Performer option.
            This way, every time you load a song, the bards will always have the same tracks assigned. If a specific JSON configuration file exists for the song, it will override this configuration.
            """);

        ImGui.Text(Path.ChangeExtension(Context.Plugin.Config.defaultPerformerFolder, null).EllipsisPath(40));
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(20);
        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##sw2BtnOpenDefaultPerformerFolder", Language.open_folder))
            WindowsApi.OpenFolder(Context.Plugin.Config.defaultPerformerFolder);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##sw2BtnChangeDefaultPerformerFolder", Language.change_folder))
        {
            Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Set Default Performer Folder", (result, filePath) =>
            {
                if (!result) return;
                Context.Plugin.MidiFileConfigManager.SetDefaultPerformerFolder(filePath);
                Context.Plugin.IpcProvider.SyncAllSettings();
                Context.Plugin.IpcProvider.UpdateDefaultPerformer();
            }, Context.Plugin.Config.defaultPerformerFolder);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##sw2BtnResetDefaultPerformerFolder", "Reset default performer"))
            Context.Plugin.MidiFileConfigManager.ResetDefaultPerformer();

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text(Language.settin_label_default_performer_tracks);

        var partyMembers = DalamudApi.PartyList
            .Select(p => p.GetPartyMemberData())
            .Where(p => Context.Plugin.MidiFileConfigManager.defaultPerformer.TrackMappingDict.ContainsKey(p.Cid))
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
            var playerTracks = string.Join(", ", Context.Plugin.MidiFileConfigManager.defaultPerformer.TrackMappingDict
                .GetValueOrDefault(partyMember.Cid).Select(n => n + 1));
            ImGui.Text(playerInfo);
            ImGui.Indent();
            ImGui.Text($"Tracks: {playerTracks}");
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Unindent();
    }

    private void DrawDefaultPlaylistOptions()
    {
        if (!ImGui.CollapsingHeader(Language.setting_label_default_playlist, ImGuiTreeNodeFlags.NoAutoOpenOnLog)) return;

        ImGui.Spacing();
        ImGui.Indent();
        ImGui.Text(Language.default_playlist_folder);
        ImGui.Text(Path.ChangeExtension(Context.Plugin.Config.defaultPlaylistFolder, null).EllipsisPath(40));
        ImGui.SameLine();
        ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));
        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##sw2BtnOpenDefaultPlaylistFolder", Language.open_folder))
            WindowsApi.OpenFolder(Context.Plugin.Config.defaultPlaylistFolder);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##sw2BtnChangeDefaultPlaylistFolder", Language.change_folder))
        {
            Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Set Default Playlist Folder", (result, filePath) =>
            {
                if (result) _ = ChangeDatabaseFolderAsync(filePath);
            }, Context.Plugin.Config.defaultPlaylistFolder);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##sw2BtnResetDefaultPlaylistFolder", "Reset default playlist folder"))
            _ = ChangeDatabaseFolderAsync(DalamudApi.PluginInterface.ConfigDirectory.FullName);

        ImGui.Spacing();
        ImGui.Unindent();
    }

    private Task ChangeDatabaseFolderAsync(string newFolderPath)
    {
        return Task.Run(async () =>
        {
            const string dbFileName = "midibard.db";
            const string logFileName = "midibard-log.db";

            var currentFolder = Context.Plugin.Config.defaultPlaylistFolder ?? DalamudApi.PluginInterface.GetPluginConfigDirectory();

            if (string.Equals(Path.GetFullPath(newFolderPath), Path.GetFullPath(currentFolder), StringComparison.OrdinalIgnoreCase))
            {
                ImGuiUtil.AddNotification(NotificationType.Info, "Database is already in this folder.");
                return;
            }

            var newDbPath = Path.Combine(newFolderPath, dbFileName);
            if (File.Exists(newDbPath))
            {
                ImGuiUtil.AddNotification(NotificationType.Warning, "A database file already exists at the destination folder.");
                return;
            }

            Context.Plugin.IpcProvider.BroadcastDisconnectDatabase();
            await Task.Delay(2500);

            var currentDbPath = Path.Combine(currentFolder, dbFileName);
            var currentLogPath = Path.Combine(currentFolder, logFileName);
            var newLogPath = Path.Combine(newFolderPath, logFileName);

            try
            {
                if (File.Exists(currentDbPath)) File.Move(currentDbPath, newDbPath);
                if (File.Exists(currentLogPath)) File.Move(currentLogPath, newLogPath);

                Context.Plugin.Config.defaultPlaylistFolder = newFolderPath;
                Context.Plugin.IpcProvider.SyncAllSettings();
                await Task.Delay(300);

                ImGuiUtil.AddNotification(NotificationType.Success, $"Database moved to: {newFolderPath}");
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, "[Database] Failed to move database to new folder");
                ImGuiUtil.AddNotification(NotificationType.Error, "Failed to move database. Check log for details.");
            }
            finally
            {
                Context.Plugin.IpcProvider.BroadcastReconnectDatabase();
            }
        });
    }

    private void DrawTrackAssignmentGlobalSettings()
    {
        var cfg = Context.Plugin.Config;

        var enabled = cfg.TrackAssignment.Enabled;
        if (ImGui.Checkbox("Enable track assignment rules##sw2TAGlobalEnabled", ref enabled))
        {
            cfg.TrackAssignment.Enabled = enabled;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.HelpMarker("When enabled, tracks are automatically assigned to ensemble members based on each member's regex rules.");

        ImGui.Spacing();

        using (ImRaii.Disabled(!cfg.TrackAssignment.Enabled))
        {
            var assignUnmatched = cfg.TrackAssignment.AssignUnmatchedTracksSequentially;
            if (ImGui.Checkbox("Assign unmatched tracks sequentially##sw2TAUnmatched", ref assignUnmatched))
            {
                cfg.TrackAssignment.AssignUnmatchedTracksSequentially = assignUnmatched;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("Tracks that match no member's rules are assigned to members in order (1, 2, 3, ...).");

            ImGui.Spacing();

            var compactAbsent = cfg.TrackAssignment.CompactAbsentMembers;
            if (ImGui.Checkbox("Compact absent members##sw2TACompact", ref compactAbsent))
            {
                cfg.TrackAssignment.CompactAbsentMembers = compactAbsent;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("When enabled, members not in the party are skipped and slots are remapped sequentially against only the present members.");

            ImGui.Spacing();

            var stopAfterMax = cfg.TrackAssignment.StopAssignmentAfterMaxPerformers;
            if (ImGui.Checkbox("Stop assignment after max performers##sw2TAStopAfterMax", ref stopAfterMax))
            {
                cfg.TrackAssignment.StopAssignmentAfterMaxPerformers = stopAfterMax;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("When enabled, once all MaxPerformers slots are filled no further tracks are assigned.");

            ImGui.Spacing();

            var maxPerformers = cfg.TrackAssignment.MaxPerformers;
            ImGui.Text("Max performers:");
            ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##sw2TAMaxPerformers", ref maxPerformers, 1, 1, default, ImGuiInputTextFlags.AutoSelectAll))
            {
                cfg.TrackAssignment.MaxPerformers = Math.Clamp(maxPerformers, 1, 32);
                Context.Plugin.IpcProvider.SyncAllSettings();
            }

            ImGui.Spacing();

            var hasCaptureRules = cfg.TrackAssignment.CaptureRules?.Count > 0;
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.GrassGreen, hasCaptureRules))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Globe, "##sw2OpenGlobalCaptureRules", "Edit Global Capture Rules"))
                    Context.Plugin.Ui.TrackAssignmentRulesWindow.OpenForGlobalRules();
            }
            ImGui.SameLine();
            ImGui.Text("Global Capture Rules");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Rules that dynamically group tracks by a captured value and assign them to players in order.");
        }
    }

    private void DrawEnsembleMembersSettings()
    {
        if (!ImGui.CollapsingHeader(Language.ensemble_party_members, ImGuiTreeNodeFlags.NoAutoOpenOnLog)) return;

        ImGui.Indent();

        var partyMembers = DalamudApi.PartyList.Select(p => p.GetPartyMemberData()).ToList();
        ImGui.Text(Language.display_order);
        ImGuiUtil.HelpMarker("""
            The order used to show bards in the ensemble panel (Drag to reorder)

            Linked members let you automatically apply the same JSON configuration to multiple performers.
            Any track assigned to the parent member is also assigned to the linked member.
            """);
        ImGui.Spacing();

        DrawTrackAssignmentGlobalSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var cfg = Context.Plugin.Config;

        if (ImGui.BeginTable("##sw2EnsembleMemberTable", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed);

            for (int i = 0; i < cfg.EnsembleMemberConfigs.Count; i++)
            {
                var member = cfg.EnsembleMemberConfigs[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text($"{i + 1:00}");

                ImGui.TableNextColumn();
                ImGui.Selectable($"{member.Name}");

                if (ImGui.BeginDragDropSource())
                {
                    unsafe
                    {
                        ImGui.SetDragDropPayload("DND_ENSEMBLE_MEMBER", new System.ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                        ImGui.Button($"({i + 1}) {member.Name}");
                    }
                    ImGui.EndDragDropSource();
                }

                using (ImRaii.PushColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget))
                {
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("DND_ENSEMBLE_MEMBER");
                        bool isDropping;
                        unsafe { isDropping = !payload.IsNull; }

                        if (isDropping && payload.IsDelivery())
                        {
                            int originalIndex;
                            unsafe { originalIndex = *(int*)payload.Data; }
                            int offset = i - originalIndex;
                            if (offset != 0 && originalIndex + offset >= 0)
                            {
                                cfg.EnsembleMemberConfigs.MoveItemToIndex(originalIndex, originalIndex + offset);
                                Context.Plugin.IpcProvider.SyncAllSettings();
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                }

                ImGui.Indent(20);
                for (int j = 0; j < member.LinkedEnsembleMembers.Count; j++)
                {
                    ImGui.Text($"{member.LinkedEnsembleMembers[j].Name}");
                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Unlink, $"##sw2UnlinkEnsembleMember_{j}", "Unlink Ensemble Member"))
                    {
                        Context.Plugin.Config.UnlinkEnsembleMember(member.Cid, member.LinkedEnsembleMembers[j].Cid);
                        Context.Plugin.IpcProvider.SyncAllSettings();
                    }
                }
                ImGui.Unindent();

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(member.LinkedEnsembleMembers.Count != 0))
                {
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Link, $"##sw2LinkEnsembleMember_{i}", "Link Ensemble Member"))
                        ImGui.OpenPopup("sw2LinkEnsembleMember");

                    if (ImGui.BeginPopup("sw2LinkEnsembleMember"))
                    {
                        ImGui.Text("Associate with:");
                        ImGui.Separator();
                        for (int t = 0; t < cfg.EnsembleMemberConfigs.Count; t++)
                        {
                            if (t == i) continue;
                            var target = cfg.EnsembleMemberConfigs[t];
                            if (ImGui.MenuItem(target.Name))
                            {
                                Context.Plugin.Config.LinkEnsembleMember(member.Cid, target.Cid);
                                Context.Plugin.IpcProvider.SyncAllSettings();
                            }
                        }
                        ImGui.EndPopup();
                    }
                }

                ImGui.SameLine();
                var hasActiveRules = member.TrackAssignmentEnabled && member.TrackRules?.Count > 0;
                using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Violet, hasActiveRules))
                {
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.SlidersH, $"##sw2EditTrackRules_{i}", "Edit Track Assignment Rules"))
                    {
                        Context.Plugin.Ui.TrackAssignmentRulesWindow.OpenForMember(member);
                        Context.Plugin.Ui.TrackAssignmentRulesWindow.IsOpen = true;
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button($"↑##sw2MoveUpMember_{i}"))
                {
                    cfg.EnsembleMemberConfigs.MoveItemToIndex(i, i - 1);
                    Context.Plugin.IpcProvider.SyncAllSettings();
                }
                ImGui.SameLine();
                if (ImGui.Button($"↓##sw2MoveDownMember_{i}"))
                {
                    cfg.EnsembleMemberConfigs.MoveItemToIndex(i, i + 1);
                    Context.Plugin.IpcProvider.SyncAllSettings();
                }
                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##sw2RemoveMember_{i}", Language.ConfirmInstructionTooltip))
                {
                    if (ImGui.GetIO().KeyCtrl)
                    {
                        cfg.EnsembleMemberConfigs.SafeRemoveAt(i);
                        Context.Plugin.IpcProvider.SyncAllSettings();
                    }
                }

                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        ImGui.Spacing();

        bool allPartyMembersInConfig = partyMembers.All(p => ContainsCidDeep(cfg.EnsembleMemberConfigs, p.Cid));
        using (ImRaii.Disabled(allPartyMembersInConfig))
        {
            ImGui.Text(Language.available_party_members);
            if (ImGui.BeginCombo("##sw2PartyMemberSelectList", "Select"))
            {
                foreach (var partyMember in partyMembers)
                {
                    if (!ContainsCidDeep(cfg.EnsembleMemberConfigs, partyMember.Cid))
                    {
                        var playerInfo = $"{partyMember.Name}@{partyMember.World}";
                        if (ImGui.Selectable($"{playerInfo}##{partyMember.Cid}", false))
                        {
                            cfg.AddEnsembleMemberConfig(new EnsembleMemberConfig
                            {
                                Cid = partyMember.Cid,
                                Name = playerInfo,
                            });
                            Context.Plugin.IpcProvider.SyncAllSettings();
                        }
                    }
                }
                ImGui.EndCombo();
            }
        }
        ImGui.Unindent();
    }

    public static bool ContainsCidDeep(List<EnsembleMemberConfig> list, long cid)
    {
        foreach (var config in list)
        {
            if (config.Cid == cid) return true;
            if (config.LinkedEnsembleMembers?.Any(m => m.Cid == cid) ?? false) return true;
        }
        return false;
    }
}
