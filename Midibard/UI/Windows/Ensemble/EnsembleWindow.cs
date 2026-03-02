using System;
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

    public EnsembleWindow(Plugin plugin) : base($"{Language.window_title_ensemble_panel}###EnsembleWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;
    }

    public override bool DrawConditions()
    {
        if (!DalamudApi.PartyList.IsPartyLeader()) return false;

        return true;
    }

    public override void Draw()
    {
        // ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, ImGui.GetStyle().ItemSpacing.Y));
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * 2.5f * ImGuiHelpers.GlobalScale))
        {
            // using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, ImGuiHelpers.ScaledVector2(ImGui.GetStyle().CellPadding.Y)))
            DrawEnsemblePannel();
        }
    }

    public override void PreDraw()
    {
        var WindowSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(300, 200),
            // MaximumSize = ImGuiHelpers.ScaledVector2(357, float.MaxValue)
        };

        SizeConstraints = WindowSizeConstraints;
        base.PreDraw();
    }

    private void DrawEnsemblePannel()
    {
        // fixed header
        // float headerStartY = ImGui.GetCursorPosY();
        using (ImRaii.Group())
        {
            DrawEnsembleControlMenu();
        }

        ImGui.Separator();

        using var childItem = ImRaii.Child("##EnsembleScrollableContent", new Vector2(-1, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        if (!childItem) return;

        if (Plugin.Config.playOnMultipleDevices && !Plugin.Config.usingFileSharingServices)
        {
            ImGui.Button($"You are NOT using file sharing services to sync settings.\nTrack assign is disabled.\nPlease choose the tracks on clients individually.", new Vector2(-1, 100));
        }
        else if (!Plugin.CurrentBardPlayback.IsLoaded)
        {
            if (ImGui.Button(Language.ensemble_select_a_song_from_playlist, new Vector2(-1, ImGui.GetFrameHeight())))
            {
                //try
                //{
                //    FilePlayback.LoadPlayback(new Random().Next(0, Plugin.PlaylistManager.FilePathList.Count));
                //}
                //catch (Exception e)
                //{
                //    //
                //}
            }
        }
        else
        {
            try
            {
                var changed = false;
                var fileConfig = Plugin.CurrentBardPlayback.MidiFileConfig;

                // use ensemble members config to define party selectbox order
                var partyList = DalamudApi.PartyList
                    .Select(p => p.GetPartyMemberData())
                    .ToList();

                // CID -> order index
                var cidToIndexMap = Plugin.Config.EnsembleMemberConfigs
                    .SelectMany((config, index) =>
                        new[] { config.Cid }
                            .Concat(
                                config.LinkedEnsembleMembers?.Select(l => l.Cid)
                                ?? Enumerable.Empty<long>()
                            )
                            .Select(cid => new { cid, index })
                    )
                    .ToDictionary(x => x.cid, x => x.index);

                var orderedPartyList =
                    new[] { (Cid: 0L, Name: "", World: "") }
                    .Concat(
                        partyList.OrderBy(p =>
                            cidToIndexMap.TryGetValue(p.Cid, out var idx)
                                ? idx
                                : int.MaxValue
                        )
                    )
                    .ToList();

                var partyNamesList = orderedPartyList
                    .Select(p => p.Cid != 0 ? $"{p.Name}·{p.World}" : "")
                    .ToArray();

                if (ImGui.BeginTable("fileConfig.Tracks", 4, ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("checkbox", ImGuiTableColumnFlags.WidthStretch, 1);
                    ImGui.TableSetupColumn("instrument", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("transpose", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("playername", ImGuiTableColumnFlags.WidthStretch, 1.2f);

                    var id = 125687;
                    foreach (var dbTrack in fileConfig.Tracks)
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, dbTrack.Enabled ? ThemeManager.CurrentTheme.Text : ThemeManager.CurrentTheme.TextDisabled))
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.PushID(id++);
                            // ImGui.PushStyleColor(ImGuiCol.Text, dbTrack.Enabled ? Style.Components.Text : Style.Components.TextDisabled);
                            //var colUprLeft = dbTrack.Enabled ? Style.Colors.Orange : Style.Colors.Violet;
                            //var pMin = GetWindowPos() + GetCursorPos();
                            //var pMax = GetWindowPos() + GetCursorPos() + new Vector2(GetWindowContentRegionWidth(), GetFrameHeight());
                            //GetWindowDrawList().AddRectFilledMultiColor(pMin, pMax, colUprLeft, 0, 0, colUprLeft);
                            ImGui.AlignTextToFramePadding();
                            changed |= ImGui.Checkbox($"{dbTrack.Index + 1:00} {dbTrack.Name}", ref dbTrack.Enabled);

                            ImGui.TableNextColumn(); //1
                            changed |= UiComponents.InstrumentPicker($"##ensembleInstrumentPicker", ref dbTrack.Instrument, ImGuiHelpers.ScaledVector2(33));

                            ImGui.TableNextColumn(); //2
                            ImGui.SetNextItemWidth(ImGui.GetFrameHeight() * 3.3f);
                            changed |= ImGuiUtil.InputIntWithReset($"##ensembleTransposeTrack", ref dbTrack.Transpose, 12, () => 0);

                            ImGui.TableNextColumn(); //3
                            ImGui.SetNextItemWidth(-1);

                            var firstMidiFileCid = MidiFileConfig.GetFirstCidInParty(dbTrack, Plugin.Config.EnsembleMemberConfigs);
                            var selectedIdx = firstMidiFileCid == -1 ? 0 : orderedPartyList.FindIndex(i => i.Cid != 0 && i.Cid == firstMidiFileCid);

                            if (ImGui.Combo("##partymemberSelect", ref selectedIdx, partyNamesList, partyNamesList.Length))
                            {
                                if (selectedIdx >= 1)
                                {
                                    var currentCid = orderedPartyList[selectedIdx].Cid;
                                    if (firstMidiFileCid > 0 && currentCid != firstMidiFileCid)
                                    {
                                        // character changed, delete the old one
                                        dbTrack.AssignedCids.Remove(firstMidiFileCid);
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
                                    foreach (var member in DalamudApi.PartyList)
                                    {
                                        if (dbTrack.AssignedCids.Contains(member.ContentId))
                                        {
                                            dbTrack.AssignedCids.Remove(member.ContentId);
                                        }
                                    }

                                    changed = true;
                                }
                            }

                            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                            {
                                // choose empty, remove all the characters in the same party
                                foreach (var member in DalamudApi.PartyList)
                                {
                                    if (dbTrack.AssignedCids.Contains(member.ContentId))
                                    {
                                        dbTrack.AssignedCids.Remove(member.ContentId);
                                    }
                                }
                                changed = true;
                            }

                            ImGuiUtil.ToolTip(Language.ensemble_combo_tooltip_assign_track_character);
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

    private void DrawEnsembleControlMenu()
    {
        var ensembleRunning = Plugin.AgentMetronome.EnsembleModeRunning;
        var isEnsembleButtonsDisabled = !Plugin.CurrentBardPlayback.IsLoaded || ensembleRunning || Plugin.CurrentBardPlayback.IsRunning;
        // if (!MidiBard.Plugin.Config.playOnMultipleDevices || (MidiBard.Plugin.Config.playOnMultipleDevices && MidiBard.Plugin.Config.usingFileSharingServices))

        if (!ensembleRunning)
        {
            ImGui.BeginDisabled(isEnsembleButtonsDisabled);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.UserCheck, "##btnEnsembleStart", Language.ensemble_begin_ensemble_ready_check, size: Style.Dimensions.EnsembleButton))
            {
                if (Plugin.Config.UpdateInstrumentBeforeReadyCheck)
                {
                    if (Plugin.CurrentBardPlayback?.MidiFileConfig is { } config)
                    {
                        Plugin.IpcProvider.UpdateMidiFileConfig(config);
                    }

                    if (!Plugin.Config.playOnMultipleDevices)
                    {
                        Plugin.IpcProvider.UpdateInstrument(true);
                    }
                }

                Plugin.EnsembleManager.BeginEnsembleReadyCheck();
            }
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Stop, "##btnEnsembleStop", Language.ensemble_stop_ensemble, size: Style.Dimensions.EnsembleButton))
            {
                if (!Plugin.Config.playOnMultipleDevices)
                {
                    Plugin.IpcProvider.UpdateInstrument(false);
                }
                else
                {
                    Plugin.PartyChatCommand.SendClose();
                }
            }
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(isEnsembleButtonsDisabled);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Guitar, "##btnUpdateInstrument", Language.ensemble_update_instruments, size: Style.Dimensions.EnsembleButton))
        {
            if (Plugin.CurrentBardPlayback?.MidiFileConfig is { } config)
            {
                Plugin.IpcProvider.UpdateMidiFileConfig(config);
            }

            if (!Plugin.Config.playOnMultipleDevices)
            {
                Plugin.IpcProvider.UpdateInstrument(true);
            }
            else
            {
                Plugin.PartyChatCommand.SendUpdateInstrument();
            }
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (!Plugin.Config.playOnMultipleDevices)
            {
                Plugin.IpcProvider.UpdateInstrument(false);
            }
            else
            {
                Plugin.PartyChatCommand.SendClose();
            }
        }
        ImGui.EndDisabled();

        //-------------------

        ImGui.SameLine();
        var muteButtonText = isOthersClientsMuted ? Language.ensemble_unmute_other_clients : Language.ensemble_mute_other_clients;
        var muteButtonIcon = isOthersClientsMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
        if (ImGuiUtil.IconButton(muteButtonIcon, muteButtonText, muteButtonText, size: Style.Dimensions.EnsembleButton))
        {
            // IsSndMaster => 0 = ON
            // IsSndMaster => 1 = OFF
            Plugin.IpcProvider.SetOption("IsSndMaster", isOthersClientsMuted ? 0 : 1, false);
            DalamudApi.GameConfig.System.Set("IsSndMaster", 0);
            isOthersClientsMuted ^= true;
        }

        //-------------------

        ImGui.SameLine();
        var muteLyricsButtonText = Plugin.Config.playLyrics ? "Disable lyrics" : "Enable lyrics";
        var muteLyricsButtonIcon = Plugin.Config.playLyrics ? FontAwesomeIcon.Microphone : FontAwesomeIcon.MicrophoneSlash;
        if (ImGuiUtil.IconButton(muteLyricsButtonIcon, "##btnMuteLyrics", muteLyricsButtonText, size: Style.Dimensions.EnsembleButton))
        {
            Plugin.Config.playLyrics = !Plugin.Config.playLyrics;
            Plugin.IpcProvider.SyncAllSettings();
        }

        //-------------------

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.WindowMinimize, "##btnWindowMinimize", Language.ensemble_minimize_other_clients, size: Style.Dimensions.EnsembleButton))
        {
            Plugin.IpcProvider.ShowWindow(WindowsApi.nCmdShow.SW_MINIMIZE);
        }

        //-------------------

        if (!Plugin.MidiFileConfigManager.UsingDefaultPerformer && !(Plugin.Config.playOnMultipleDevices && !Plugin.Config.usingFileSharingServices))
        {
            ImGui.SameLine();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                Plugin.IpcProvider.ShowWindow(WindowsApi.nCmdShow.SW_RESTORE);
            }

            //-------------------

            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(10);

            ImGui.SameLine();
            ImGui.BeginDisabled(isEnsembleButtonsDisabled);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##btnOpenConfigFolder", Language.ensemble_open_midi_config_directory, size: Style.Dimensions.EnsembleButton))
            {
                if (!Plugin.CurrentBardPlayback.IsLoaded) return;

                var fileInfo = Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath);
                var configDirectoryFullName = fileInfo.Directory.FullName;
                // DalamudApi.PluginLog.Debug(fileInfo.FullName);
                // DalamudApi.PluginLog.Debug(MidiBard.CurrentPlayback.FilePath);
                // DalamudApi.PluginLog.Debug(configDirectoryFullName);

                WindowsApi.OpenFolder(configDirectoryFullName);
            }
            ImGui.EndDisabled();

            //-------------------

            ImGui.SameLine();
            ImGui.BeginDisabled(isEnsembleButtonsDisabled);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##btnOpenConfigFile", Language.ensemble_open_midi_config_file, size: Style.Dimensions.EnsembleButton))
            {
                if (!Plugin.CurrentBardPlayback.IsLoaded) return;

                var fileInfo = Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath);
                // DalamudApi.PluginLog.Debug(fileInfo.FullName);
                // DalamudApi.PluginLog.Debug(MidiBard.CurrentPlayback.FilePath);

                WindowsApi.OpenFile(fileInfo.FullName);
            }
            ImGui.EndDisabled();

            //-------------------

            ImGui.SameLine();
            ImGui.BeginDisabled(isEnsembleButtonsDisabled);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "##btnDeleteConfig", Language.ensemble_delete_and_reset_current_file_config, size: Style.Dimensions.EnsembleButton))
            {
                if (Plugin.CurrentBardPlayback.IsLoaded)
                {
                    Plugin.MidiFileConfigManager.GetMidiConfigFileInfo(Plugin.CurrentBardPlayback.FilePath).Delete();
                    Plugin.CurrentBardPlayback.MidiFileConfig = Plugin.MidiFileConfigManager.GetMidiConfigFromTrack(Plugin.CurrentBardPlayback.TrackInfos);
                    Plugin.CurrentBardPlayback.MidiFileConfig = Plugin.CurrentBardPlayback.ReloadMidiFileConfig(Plugin.CurrentBardPlayback.MidiFileConfig);
                    Plugin.IpcProvider.UpdateInstrument(false);
                }
            }
            ImGui.EndDisabled();

            //-------------------

            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(10);

            ImGui.SameLine();
            ImGui.BeginDisabled(isEnsembleButtonsDisabled);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FileExport, "##btnExportDefaultPerformer", Language.ensemble_save_default_performers, size: Style.Dimensions.EnsembleButton))
            {
                Plugin.MidiFileConfigManager.ExportToDefaultPerformer();
            }
            ImGui.EndDisabled();
        }

        //-------------------

        ImGui.BeginDisabled(isEnsembleButtonsDisabled);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Redo, "##btnResetDefaultPerformer", "Reset default performer", size: Style.Dimensions.EnsembleButton))
        {
            Plugin.MidiFileConfigManager.ResetDefaultPerformer();
        }
        ImGui.EndDisabled();

        if (Plugin.MidiFileConfigManager.UsingDefaultPerformer)
        {
            ImGui.SameLine();
            ImGui.Text("[Using Default Performer]");
        }
    }
}
