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

using MidiBard.Extensions.Dalamud;
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

    private readonly KeySequence _code = new();

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
        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.ExchangeAlt, "##BtnSyncSettings", Language.icon_button_tooltip_sync_settings))
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
            ImGuiUtil.AddNotification(NotificationType.Info, "Settings synced");
        }

        if (ImGui.Checkbox(Language.setting_label_monitor_ensemble, ref cfg.MonitorOnEnsemble))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.ToolTip(Language.setting_tooltip_monitor_ensemble);

        ImGui.Text("Ensemble Indicator Delay:");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
        if (ImGui.DragFloat("##EnsembleIndicatorDelay", ref cfg.EnsembleIndicatorDelay, 0.1f, 0, 10, "%.1fs"))
        {
            cfg.EnsembleIndicatorDelay = Math.Clamp(cfg.EnsembleIndicatorDelay, 0.0f, 10.0f);
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            cfg.EnsembleIndicatorDelay = 4.0f;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Metronometer delay start time. Right-click to reset");

        if (ImGui.Checkbox("Use Heartbeat Sync##UseHeartbeatSync", ref cfg.UseHeartbeatSync))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker("""
            When enabled, playback start is triggered by the game's performance broadcast packet
            (~3-second heartbeat sent to all nearby players) instead of the game-party ensemble packet.

            This allows groups of players that span multiple parties - or have members outside any party -
            to synchronise playback. All players must be in the same zone/instance.

            To use:
              1. Enable on all clients.
              2. Leader clicks "Arm Heartbeat Sync" (arms all same-machine clients + party-chat members).
              3. Non-party players on other machines click "Arm" on their own UI.
              4. Leader equips an instrument (needed to generate the heartbeat packets).
              5. The next heartbeat triggers DoPlay on all armed clients simultaneously.

            Party mode hybrid: when a party ensemble ready-check completes, all party members
            arm automatically. Non-party players on other machines still need to arm manually.
            """);

        if (cfg.UseHeartbeatSync)
        {
            using (ImRaii.PushIndent())
            {
                ImGui.Text("Heartbeat Start Delay:");
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
                if (ImGui.DragFloat("##HeartbeatStartDelay", ref cfg.HeartbeatStartDelay, 0.1f, 0f, 10f, "%.1fs"))
                {
                    cfg.HeartbeatStartDelay = Math.Clamp(cfg.HeartbeatStartDelay, 0.0f, 10.0f);
                    Context.Plugin.IpcProvider.SyncAllSettings();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    cfg.HeartbeatStartDelay = 4.0f;
                    Context.Plugin.IpcProvider.SyncAllSettings();
                }
                ImGuiUtil.ToolTip("Non party delay start time (Usually same as Metronometer). Right-click to reset");

                ImGui.SameLine();
                if (ImGuiUtil.PrimaryIconButton(FontAwesomeIcon.SatelliteDish, "##HeartbeatStartDelayFromPing", "Sync from Ping"))
                {
                    Task.Run(async () =>
                    {
                        var offset = await PingHelper.GetFfxivPingOffsetSecondsAsync();
                        if (offset != null)
                        {
                            cfg.HeartbeatStartDelay = 4.0f - offset.Value;
                            Context.Plugin.IpcProvider.SyncAllSettings();
                            DalamudApi.PluginLog.Information($"Ping detected. HeartbeatStartDelay updated to: {cfg.HeartbeatStartDelay:F3}s");
                            ImGuiUtil.AddNotification(NotificationType.Success, $"Ping offset synchronized ({cfg.HeartbeatStartDelay:F3}s)");
                        }
                        else
                        {
                            ImGuiUtil.AddNotification(NotificationType.Error, "Failed to read ping");
                        }
                    });
                }

                bool armed = Context.Plugin.EnsembleManager.HeartbeatSyncArmed;
                if (armed)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(Style.Colors.GrassGreen, "  ARMED - waiting for heartbeat...");
                    ImGui.SameLine();
                    if (ImGuiUtil.DangerButton("Disarm##DisarmHeartbeatSync"))
                        Context.Plugin.EnsembleManager.DisarmHeartbeatSync();
                }
                else
                {
                    ImGui.Spacing();
                    ImGui.Text("Listen To Character Name:");
                    if (ImGui.InputText("##HeartbeatSyncListenToCharacterName", ref cfg.HeartbeatSyncListenToCharacterName))
                    {
                        Context.Plugin.IpcProvider.SyncAllSettings();
                    }
                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Crosshairs, $"##SetFromTargetName", "Set From Target"))
                    {
                        cfg.HeartbeatSyncListenToCharacterName = DalamudApi.ObjectTable.LocalPlayer?.TargetObject?.GetPlayerNameWorld() ?? string.Empty;
                    }

                    ImGui.Spacing();
                    if (ImGuiUtil.PrimaryButton("Arm Heartbeat Sync##ArmHeartbeatSync"))
                    {
                        Context.Plugin.EnsembleManager.ArmAndBroadcastHeartbeatSync();
                    }
                    ImGuiUtil.ToolTip("Arms all same-machine clients and in-party cross-machine clients.\nPlayers on other machines outside the party must click Arm on their own UI.");
                }
            }
        }

        _code.Update();
        if (_code.IsUnlocked || cfg.EnableEnsemblePlayMode)
        {
            if (ImGui.Checkbox("Enable Ensemble PlayMode", ref cfg.EnableEnsemblePlayMode))
            {
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        //  Multiple devices
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

        if (ImGui.Checkbox("Unequip Instruments On Ensemble End", ref cfg.UnequipInstrumentsOnEnsembleEnd))
            Context.Plugin.IpcProvider.SyncAllSettings();

        if (ImGui.Checkbox(Language.ensemble_config_update_instrument_when_begin_ensemble, ref cfg.UpdateInstrumentBeforeReadyCheck))
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Update instruments before start ensemble (Local bards only)");

        if (cfg.UpdateInstrumentBeforeReadyCheck)
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
            if (ImGui.DragInt("Pre-ready check delay (ms)##PreReadyCheckDelay", ref cfg.PreReadyCheckDelayMs, 1, 0, 3000))
            {
                cfg.PreReadyCheckDelayMs = Math.Clamp(cfg.PreReadyCheckDelayMs, 0, 3000);
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                cfg.PreReadyCheckDelayMs = 500;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Delay between sending instrument update and triggering the ready check.");
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
        if (ImGui.DragFloat("Ensemble Stop Delay##EnsembleStopDelay", ref cfg.EnsembleStopDelay, 1, 0, 10, "%.1fs"))
        {
            cfg.EnsembleStopDelay = Math.Clamp(cfg.EnsembleStopDelay, 0, 10);
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            cfg.EnsembleStopDelay = 3;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Automatically stops the ensemble while the metronome is running.");

        //  Compensation
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.Text(Language.ensemble_compensation_mode);
        if (ImGuiUtil.EnumCombo("##CompensationMode", ref cfg.CompensationMode, labelsOverride: _compensationModeLabels))
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
            if (ImGuiUtil.IconButton(FontAwesomeIcon.SlidersH, "##InstrumentCompensations", "Instrument Compensations"))
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

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenDefaultPerformerFolder", Language.open_folder))
            WindowsApi.OpenFolder(Context.Plugin.Config.defaultPerformerFolder);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##BtnChangeDefaultPerformerFolder", Language.change_folder))
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
        if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##BtnResetDefaultPerformerFolder", "Reset default performer"))
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
        if (!ImGui.CollapsingHeader("Playlist", ImGuiTreeNodeFlags.NoAutoOpenOnLog)) return;

        ImGui.Spacing();
        ImGui.Indent();
        ImGui.Text(Language.playlist_folder);
        ImGuiUtil.HelpMarker("""
        Folder where the song database and playlists are stored.
        Clients with separate configuration folders must set the same playlist folder so they can share a single database file.
        """);
        ImGui.Text(Path.ChangeExtension(Context.Plugin.Config.defaultPlaylistFolder, null).EllipsisPath(40));
        ImGui.SameLine();
        ImGui.Dummy(ImGuiHelpers.ScaledVector2(20));
        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenPlaylistFolder", Language.open_folder))
            WindowsApi.OpenFolder(Context.Plugin.Config.defaultPlaylistFolder);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderPlus, "##BtnChangePlaylistFolder", Language.change_folder))
        {
            Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog("Set Playlist Folder", (result, filePath) =>
            {
                if (result) _ = ChangeDatabaseFolderAsync(filePath);
            }, Context.Plugin.Config.defaultPlaylistFolder);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.RedoAlt, "##BtnResetPlaylistFolder", "Reset playlist folder"))
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
        if (ImGui.Checkbox("Enable track assignment rules##TAGlobalEnabled", ref enabled))
        {
            cfg.TrackAssignment.Enabled = enabled;
            Context.Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.HelpMarker("When enabled, tracks are automatically assigned to ensemble members based on each member's regex rules.");

        ImGui.Spacing();

        using (ImRaii.Disabled(!cfg.TrackAssignment.Enabled))
        {
            var assignUnmatched = cfg.TrackAssignment.AssignUnmatchedTracksSequentially;
            if (ImGui.Checkbox("Assign unmatched tracks sequentially##TAUnmatched", ref assignUnmatched))
            {
                cfg.TrackAssignment.AssignUnmatchedTracksSequentially = assignUnmatched;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("Tracks that match no member's rules are assigned to members in order (1, 2, 3, ...).");

            ImGui.Spacing();

            var compactAbsent = cfg.TrackAssignment.CompactAbsentMembers;
            if (ImGui.Checkbox("Compact absent members##TACompact", ref compactAbsent))
            {
                cfg.TrackAssignment.CompactAbsentMembers = compactAbsent;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("When enabled, members not in the party are skipped and slots are remapped sequentially against only the present members.");

            ImGui.Spacing();

            var stopAfterMax = cfg.TrackAssignment.StopAssignmentAfterMaxPerformers;
            if (ImGui.Checkbox("Stop assignment after max performers##TAStopAfterMax", ref stopAfterMax))
            {
                cfg.TrackAssignment.StopAssignmentAfterMaxPerformers = stopAfterMax;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("When enabled, once all MaxPerformers slots are filled no further tracks are assigned.");

            ImGui.Spacing();

            var maxPerformers = cfg.TrackAssignment.MaxPerformers;
            ImGui.Text("Max performers:");
            ImGui.SetNextItemWidth(80f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##TAMaxPerformers", ref maxPerformers, 1, 1, default, ImGuiInputTextFlags.AutoSelectAll))
            {
                cfg.TrackAssignment.MaxPerformers = Math.Clamp(maxPerformers, 1, 32);
                Context.Plugin.IpcProvider.SyncAllSettings();
            }

            ImGui.Spacing();

            var hasCaptureRules = cfg.TrackAssignment.CaptureRules?.Count > 0;
            using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Green, hasCaptureRules))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Globe, "##OpenGlobalCaptureRules", "Edit Global Capture Rules"))
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

        if (ImGui.BeginTable("##EnsembleMemberTable", 3,
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
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Unlink, $"##UnlinkEnsembleMember_{j}", "Unlink Ensemble Member"))
                    {
                        Context.Plugin.Config.UnlinkEnsembleMember(member.Cid, member.LinkedEnsembleMembers[j].Cid);
                        Context.Plugin.IpcProvider.SyncAllSettings();
                    }
                }
                ImGui.Unindent();

                ImGui.TableNextColumn();
                using (ImRaii.Disabled(member.LinkedEnsembleMembers.Count != 0))
                {
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Link, $"##LinkEnsembleMember_{i}", "Link Ensemble Member"))
                        ImGui.OpenPopup("LinkEnsembleMember");

                    if (ImGui.BeginPopup("LinkEnsembleMember"))
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
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.SlidersH, $"##EditTrackRules_{i}", "Edit Track Assignment Rules"))
                    {
                        Context.Plugin.Ui.TrackAssignmentRulesWindow.OpenForMember(member);
                        Context.Plugin.Ui.TrackAssignmentRulesWindow.IsOpen = true;
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button($"↑##MoveUpMember_{i}"))
                {
                    cfg.EnsembleMemberConfigs.MoveItemToIndex(i, i - 1);
                    Context.Plugin.IpcProvider.SyncAllSettings();
                }
                ImGui.SameLine();
                if (ImGui.Button($"↓##MoveDownMember_{i}"))
                {
                    cfg.EnsembleMemberConfigs.MoveItemToIndex(i, i + 1);
                    Context.Plugin.IpcProvider.SyncAllSettings();
                }
                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemoveMember_{i}", Language.ConfirmInstructionTooltip))
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
            if (ImGui.BeginCombo("##PartyMemberSelectList", "Select"))
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
