using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Managers;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public class EnsembleWindow : Window
{
    private Plugin Plugin { get; }
    private static bool isOthersClientsMuted = false;

    //  Party list cache
    private List<(ulong Cid, string Name, string World)>? _orderedPartyList;
    private string[]? _partyNamesList;
    private ulong[] _cachedPartyCids = Array.Empty<ulong>();

    private void EnsurePartyCacheValid()
    {
        var partyCids = Plugin.PartyWatcher.PartyMemberCIDs.ToList();
        var partyList = DalamudApi.PartyList.Select(p => p.GetPartyMemberData()).ToList();

        if (Plugin.Config.ShowAllConfiguredMembersInTrackAssign)
        {
            var configMembers = Plugin.Config.EnsembleMemberConfigs
                .SelectMany(cfg =>
                    new[] { (Cid: cfg.Cid, Name: cfg.Name) }
                        .Concat(cfg.LinkedEnsembleMembers?.Select(l => (Cid: l.Cid, Name: l.Name)) ?? Enumerable.Empty<(ulong, string)>()))
                .ToList();

            foreach (var member in configMembers)
            {
                if (!partyCids.Contains(member.Cid))
                {
                    partyCids.Add(member.Cid);
                    partyList.Add((member.Cid, member.Name, ""));
                }
            }
        }

        var newCids = partyCids.ToArray();
        if (_partyNamesList != null && newCids.SequenceEqual(_cachedPartyCids))
            return;

        _cachedPartyCids = newCids;

        var cidToIndexMap = Plugin.Config.EnsembleMemberConfigs
            .SelectMany((cfg, i) =>
                new[] { cfg.Cid }
                    .Concat(cfg.LinkedEnsembleMembers?.Select(l => l.Cid) ?? Enumerable.Empty<ulong>())
                    .Select(cid => new { cid, i }))
            .ToDictionary(x => x.cid, x => x.i);

        _orderedPartyList = new[] { (Cid: 0UL, Name: "", World: "") }
            .Concat(partyList.OrderBy(p =>
                cidToIndexMap.TryGetValue(p.Cid, out var idx) ? idx : int.MaxValue))
            .ToList();

        _partyNamesList = _orderedPartyList
            .Select(p => p.Cid != 0 ? (string.IsNullOrEmpty(p.World) ? p.Name : $"{p.Name}·{p.World}") : "")
            .ToArray();
    }

    public EnsembleWindow(Plugin plugin) : base($"{Language.window_ensemble}###EnsembleWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(300, 200),
            // MaximumSize = ImGuiHelpers.ScaledVector2(357, float.MaxValue)
        };
    }

    public override bool DrawConditions()
    {
        if (!DalamudApi.PartyList.IsPartyLeader()) return false;

        return true;
    }

    public override void Draw()
    {
        DrawEnsemblePannel();
    }

    internal void DrawEnsemblePannel(bool useSmallSize = false, float instrumentIconSize = 33f)
    {
        // fixed header
        using (ImRaii.Group())
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4)))
            {
                DrawEnsembleControlMenu();
            }
        }

        ImGui.Separator();

        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * 2.5f * ImGuiHelpers.GlobalScale, !useSmallSize))
        {
            DrawEnsembleTracks(instrumentIconSize);
        }
    }

    private void DrawEnsembleControlMenu()
    {
        var ensembleRunning = AgentManager.AgentMetronome.EnsembleModeRunning;
        var isEnsembleButtonsDisabled = !Plugin.CurrentBardPlayback.IsLoaded || ensembleRunning || Plugin.CurrentBardPlayback.IsRunning;
        var hasConfigFile = Plugin.MidiFileConfigManager.TrackAssignSource == TrackAssignSource.JsonFile && !(Plugin.Config.playOnMultipleDevices && !Plugin.Config.usingFileSharingServices);

        if (!ensembleRunning)
        {
            using (ImRaii.Disabled(isEnsembleButtonsDisabled))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.UserCheck, "##btnEnsembleStart", Language.ensemble_action_ready_check, size: Style.Dimensions.ButtonLarge))
                {
                    if (Plugin.Config.UpdateInstrumentBeforeReadyCheck)
                    {
                        Plugin.EnsembleManager.BroadcastEquipInstruments();
                        Plugin.EnsembleManager.BeginEnsembleReadyCheck(Plugin.Config.PreReadyCheckDelayMs);
                    }
                    else
                    {
                        Plugin.EnsembleManager.BeginEnsembleReadyCheck();
                    }
                }
            }
        }
        else
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##btnEnsembleStop", Language.ensemble_action_stop, size: Style.Dimensions.ButtonLarge))
            {
                Plugin.EnsembleManager.BroadcastUnequipInstruments();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(isEnsembleButtonsDisabled))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Guitar, "##btnUpdateInstrument", Language.ensemble_action_update_instruments, size: Style.Dimensions.ButtonLarge))
            {
                Plugin.EnsembleManager.BroadcastEquipInstruments();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                Plugin.EnsembleManager.BroadcastUnequipInstruments();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(isEnsembleButtonsDisabled || !Plugin.Config.UseHeartbeatSync))
        {
            bool armed = Plugin.EnsembleManager.HeartbeatSyncArmed;
            // FontAwesomeIcon.StopCircle
            var icon = armed ? FontAwesomeIcon.Wifi : FontAwesomeIcon.PlayCircle;
            var label = armed ? "DisarmHeartbeatSync" : "Arm Heartbeat Sync";
            using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonSuccessNormal, armed)
           .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonSuccessHovered, armed)
           .Push(ImGuiCol.ButtonActive, Style.Components.ButtonSuccessActive, armed))
            {
                if (ImGuiUtil.IconButton(icon, "##ArmAndBroadcastHeartbeatSync", label, size: Style.Dimensions.ButtonLarge))
                {
                    if (armed)
                    {
                        Plugin.EnsembleManager.DisarmHeartbeatSync();
                        return;
                    }
                    Plugin.EnsembleManager.ArmAndBroadcastHeartbeatSync();
                }
            }
        }

        ImGui.SameLine();
        var muteLyricsButtonText = Plugin.Config.playLyrics ? "Disable lyrics" : "Enable lyrics";
        var muteLyricsButtonIcon = Plugin.Config.playLyrics ? FontAwesomeIcon.Microphone : FontAwesomeIcon.MicrophoneSlash;
        if (ImGuiUtil.IconButton(muteLyricsButtonIcon, "##btnMuteLyrics", muteLyricsButtonText, size: Style.Dimensions.ButtonLarge))
        {
            Plugin.Config.playLyrics = !Plugin.Config.playLyrics;
            Plugin.IpcProvider.SyncAllSettings();
        }

        // JSON Config popup button
        ImGui.SameLine();
        using (ImRaii.Disabled(isEnsembleButtonsDisabled || !hasConfigFile))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.SlidersH, "##EnsembleJsonConfig", "JSON Config", size: Style.Dimensions.ButtonLarge))
                ImGui.OpenPopup("##popupEnsembleJsonConfig");
        }

        using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
            {

                if (ImGui.BeginPopup("##popupEnsembleJsonConfig"))
                {
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##popOpenConfigFolder", Language.ensemble_action_open_midi_config_dir, size: Style.Dimensions.ButtonLarge))
                    {
                        var fileInfo = Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath);
                        WindowsApi.OpenFolder(fileInfo.Directory.FullName);
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##popOpenConfigFile", Language.ensemble_action_open_midi_config_file, size: Style.Dimensions.ButtonLarge))
                    {
                        var fileInfo = Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath);
                        WindowsApi.OpenFile(fileInfo.FullName);
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Cogs, "##popInstrumentCompensation", "Instrument Delay Compensation", size: Style.Dimensions.ButtonLarge))
                    {
                        Plugin.Ui.InstrumentCompensationWindow.Toggle();
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "##popDeleteConfig", Language.ensemble_action_delete_config, size: Style.Dimensions.ButtonLarge))
                    {
                        if (Plugin.CurrentBardPlayback.IsLoaded)
                        {
                            Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath).Delete();
                            Plugin.CurrentBardPlayback.MidiFileConfig = Plugin.MidiFileConfigManager.GetMidiConfigFromTrack(Plugin.CurrentBardPlayback.TrackInfos);
                            Plugin.CurrentBardPlayback.MidiFileConfig = Plugin.CurrentBardPlayback.ReloadMidiFileConfig(Plugin.CurrentBardPlayback.MidiFileConfig);
                            Plugin.IpcProvider.UpdateInstrument(false);
                        }
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.WindowMinimize, "##popBtnWindowMinimize", Language.ensemble_action_minimize_clients, size: Style.Dimensions.ButtonLarge))
        {
            Plugin.IpcProvider.ShowWindow(WindowsApi.nCmdShow.SW_MINIMIZE);

        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Plugin.IpcProvider.ShowWindow(WindowsApi.nCmdShow.SW_RESTORE);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.EllipsisH, "##EnsembleMoreOptions", "More options", size: Style.Dimensions.ButtonLarge))
            ImGui.OpenPopup("##popupEnsembleMoreOptions");

        using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
            {
                if (ImGui.BeginPopup("##popupEnsembleMoreOptions"))
                {
                    using (ImRaii.Disabled(isEnsembleButtonsDisabled || !hasConfigFile))
                    {
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##popExportDefaultPerformer", Language.ensemble_action_save_default_performers, size: Style.Dimensions.ButtonLarge))
                        {
                            Plugin.MidiFileConfigManager.ExportToDefaultPerformer();
                            ImGui.CloseCurrentPopup();
                        }
                    }
                    ImGui.SameLine();

                    if (Plugin.MidiFileConfigManager.TrackAssignSource == TrackAssignSource.DefaultPerformer)
                    {
                        using (ImRaii.Disabled(isEnsembleButtonsDisabled))
                        {
                            if (ImGuiUtil.IconButton(FontAwesomeIcon.Redo, "##popResetDefaultPerformer", "Reset default performer", size: Style.Dimensions.ButtonLarge))
                            {
                                Plugin.MidiFileConfigManager.ResetDefaultPerformer();
                                ImGui.CloseCurrentPopup();
                            }
                        }
                        ImGui.SameLine();
                    }

                    var muteButtonText = isOthersClientsMuted ? Language.ensemble_action_unmute_clients : Language.ensemble_action_mute_clients;
                    var muteButtonIcon = isOthersClientsMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
                    if (ImGuiUtil.IconButton(muteButtonIcon, "##popMuteOtherClients", muteButtonText, size: Style.Dimensions.ButtonLarge))
                    {
                        Plugin.IpcProvider.SetOption("IsSndMaster", isOthersClientsMuted ? 0 : 1, false);
                        DalamudApi.GameConfig.System.Set("IsSndMaster", 0);
                        isOthersClientsMuted ^= true;
                    }

                    ImGui.SameLine();
                    var showAllText = Plugin.Config.ShowAllConfiguredMembersInTrackAssign ? "Displays all ensemble members registered in the track selection list" : "Show Party Members";
                    var showAllIcon = Plugin.Config.ShowAllConfiguredMembersInTrackAssign ? FontAwesomeIcon.Users : FontAwesomeIcon.UserFriends;
                    if (ImGuiUtil.IconButton(showAllIcon, "##popShowAllConfiguredMembers", showAllText, size: Style.Dimensions.ButtonLarge))
                    {
                        Plugin.Config.ShowAllConfiguredMembersInTrackAssign ^= true;
                        Plugin.Config.Save();
                        Plugin.IpcProvider.SyncAllSettings();
                    }

                    ImGui.EndPopup();
                }
            }
        }

        var assignSource = Plugin.MidiFileConfigManager.TrackAssignSource;
        if (assignSource != TrackAssignSource.None)
        {
            ImGui.SameLine();
            var (label, color) = assignSource switch
            {
                TrackAssignSource.JsonFile => ("[JSON]", Style.Colors.Yellow),
                TrackAssignSource.DefaultPerformer => ("[Default Performer]", Style.Colors.Yellow),
                TrackAssignSource.Rules => ("[Rules]", Style.Colors.Yellow),
                TrackAssignSource.TrackStatus => ("[Track Status]", Style.Colors.Yellow),
                _ => ("", Style.Colors.White),
            };
            if (!string.IsNullOrEmpty(label))
                ImGui.TextColored(color, label);
        }
    }

    internal void DrawEnsembleTracks(float instrumentIconSize = 33f)
    {
        using var childItem = ImRaii.Child("##EnsembleScrollableContent", new Vector2(-1, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        if (!childItem) return;

        if (Plugin.Config.playOnMultipleDevices && !Plugin.Config.usingFileSharingServices)
        {
            ImGui.Button($"You are NOT using file sharing services to sync settings.\nTrack assign is disabled.\nPlease choose the tracks on clients individually.", new Vector2(-1, 100));
        }
        else if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            ImGui.Button(Language.ensemble_label_select_song, new Vector2(-1, ImGui.GetFrameHeight()));
        }
        else
        {
            try
            {
                var changed = false;
                var fileConfig = Plugin.CurrentBardPlayback.MidiFileConfig;

                EnsurePartyCacheValid();
                var orderedPartyList = _orderedPartyList!;
                var partyNamesList = _partyNamesList!;

                if (ImGui.BeginTable("fileConfig.Tracks", 4, ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("checkbox", ImGuiTableColumnFlags.WidthStretch, 1);
                    ImGui.TableSetupColumn("instrument", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("transpose", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("playername", ImGuiTableColumnFlags.WidthStretch, 1.2f);

                    foreach (var dbTrack in fileConfig.Tracks)
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, dbTrack.Enabled ? ThemeManager.CurrentTheme.Text : ThemeManager.CurrentTheme.TextDisabled))
                        {
                            ImGui.PushID(dbTrack.Index);
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            changed |= ImGui.Checkbox($"{dbTrack.Index + 1:00} {dbTrack.Name}", ref dbTrack.Enabled);

                            ImGui.TableNextColumn();
                            changed |= UiComponents.InstrumentPicker($"##ensembleInstrumentPicker", ref dbTrack.Instrument, ImGuiHelpers.ScaledVector2(instrumentIconSize));

                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 3.4f);
                            changed |= ImGuiUtil.InputIntWithReset($"##ensembleTransposeTrack", ref dbTrack.Transpose, 12, () => 0);

                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(-1);

                            var firstMidiFileCid = MidiFileConfig.GetFirstCidInParty(dbTrack, Plugin.Config.EnsembleMemberConfigs);
                            var selectedIdx = firstMidiFileCid == 0 ? 0 : orderedPartyList.FindIndex(i => i.Cid != 0 && i.Cid == firstMidiFileCid);

                            if (ImGui.Combo("##partymemberSelect", ref selectedIdx, partyNamesList, partyNamesList.Length))
                            {
                                if (selectedIdx >= 1)
                                {
                                    var currentCid = orderedPartyList[selectedIdx].Cid;
                                    if (firstMidiFileCid is ulong cid && cid > 0 && currentCid != cid)
                                    {
                                        // character changed, delete the old one
                                        dbTrack.AssignedCids.Remove(cid);
                                        changed = true;
                                    }

                                    if (currentCid > 0)
                                    {
                                        // add character
                                        if (!dbTrack.AssignedCids.Contains(currentCid))
                                        {
                                            dbTrack.AssignedCids.Insert(0, currentCid);
                                            changed = true;
                                        }
                                    }
                                }
                                else
                                {
                                    // choose empty, remove all the characters in the same party
                                    foreach (var member in orderedPartyList)
                                    {
                                        if (member.Cid != 0 && dbTrack.AssignedCids.Contains(member.Cid))
                                        {
                                            dbTrack.AssignedCids.Remove(member.Cid);
                                        }
                                    }

                                    changed = true;
                                }
                            }

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                // choose empty, remove all the characters in the same party
                                foreach (var member in orderedPartyList)
                                {
                                    if (member.Cid != 0 && dbTrack.AssignedCids.Contains(member.Cid))
                                    {
                                        dbTrack.AssignedCids.Remove(member.Cid);
                                    }
                                }
                                changed = true;
                            }

                            ImGuiUtil.ToolTip(Language.ensemble_tooltip_assign_track);
                            ImGui.PopID();
                        }
                    }

                    ImGui.EndTable();
                }

                if (changed)
                {
                    Plugin.MidiFileConfigManager.Save(fileConfig, Plugin.CurrentBardPlayback.FilePath);
                    Plugin.IpcProvider.UpdateMidiFileConfig(fileConfig);
                }
            }
            catch (Exception e)
            {
                ImGui.Text(e.ToString());
            }
        }
    }
}
