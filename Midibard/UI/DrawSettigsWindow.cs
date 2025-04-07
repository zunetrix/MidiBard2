using System;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Util;
using MidiBard.Managers.Ipc;

using static Dalamud.api;
using static MidiBard2.Resources.Language;

namespace MidiBard;

public partial class PluginUI
{
    private bool settingsWindowOpen = false;
    private bool compensationEditWindowOpen = false;
    private bool nameReferenceWindowOpen = false;

    private readonly string[] toneModeToolTips = {
        "Off: Does not take over game's guitar tone control.",
        "Standard: Standard midi channel and ProgramChange handling, each channel will keep it's program state separately.",
        "Simple: Simple ProgramChange handling, ProgramChange event on any channel will change all channels' program state. (This is BardMusicPlayer's default behavior.)",
        "Override by track: Assign guitar tone manually for each track and ignore ProgramChange events.",
    };

    public void ToggleSettingsWindow()
    {
        if (settingsWindowOpen)
            CloseSettingsWindow();
        else
            OpenSettingsWindow();
    }

    public void OpenSettingsWindow()
    {
        settingsWindowOpen = true;
    }

    public void CloseSettingsWindow()
    {
        settingsWindowOpen = false;
    }

    private void DrawSettigsWindow()
    {
        if (!settingsWindowOpen) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(610, 650) * ImGuiHelpers.GlobalScale, ImGuiHelpers.MainViewport.Size);

        ImGui.Begin("MidiBard Settings", ref settingsWindowOpen);

        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("General Settings"))
            {
                DrawGeneralSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Performance Settings"))
            {
                DrawPerformanceSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Ensemble Settings"))
            {
                DrawEnsembleSettings();
                ImGui.EndTabItem();
            }

#if DEBUG
            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebugWindow();
                ImGui.EndTabItem();
            }
#endif

            ImGui.EndTabBar();
        }

        ImGui.End();

        DrawNameReferenceWindow();
    }

    private void DrawGeneralSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_general_settings);
        {
            if (ImGui.Checkbox(setting_label_auto_open_on_startup, ref MidiBard.config.AutoOpenOnStartup))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_auto_open_on_startup);

            //-------------------

            if (ImGui.Checkbox(setting_label_auto_open_when_performing, ref MidiBard.config.AutoOpenPlayerWhenPerforming))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_tooltip_auto_open_when_performing);

            //-------------------

            if (ImGui.Checkbox(setting_label_hide_player_information_from_ui, ref MidiBard.config.hidePlayerInformationFromUi))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_hide_player_information_from_ui);

            //-------------------

            //Checkbox(Low_latency_mode, ref MidiBard.config.LowLatencyMode);
            //ImGuiUtil.ToolTip(low_latency_mode_tooltip);

            //ImGui.Checkbox(checkbox_auto_restart_listening, ref MidiBard.config.autoRestoreListening);
            //ImGuiUtil.ToolTip(checkbox_auto_restart_listening_tooltip);

            //ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2);
            //ImGui.Checkbox("Auto listening new device".Localize(), ref MidiBard.config.autoStartNewListening);
            //ImGuiUtil.ToolTip("Auto start listening new midi input device when idle.".Localize());
            //ImGuiUtil.ColorPickerButton(1000, label_theme_color, ref MidiBard.config.themeColor,
            //    ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
            //if (ImGui.ColorEdit4("Theme color".Localize(), ref MidiBard.config.themeColor,
            //    ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs))

            ImGui.ColorEdit4(setting_label_theme_color, ref MidiBard.config.themeColor,
                ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                var uiColor = 0xFFFFA8A8;
                MidiBard.config.themeColor = ImGui.ColorConvertU32ToFloat4(uiColor);
            }

            //-------------------

            if (ImGui.Combo(setting_label_select_ui_language, ref MidiBard.config.uiLang, uilangStrings,
                    uilangStrings.Length))
            {
                MidiBard.ConfigureLanguage(MidiBard.GetCultureCodeString((MidiBard.CultureCode)MidiBard.config.uiLang));
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ImGui.BeginDisabled(true);
            // if (ImGui.Button("Export Settings"))
            // {
            //     // TODO : implement export settings
            //     ImGuiUtil.AddNotification(NotificationType.Success, $"Settings exported");
            // }
            // ImGui.EndDisabled();

        }

        ImGuiGroupPanel.EndGroupPanel();
    }

    private void DrawPerformanceSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_performance_settings);

        if (ImGui.Checkbox(setting_label_auto_switch_instrument_bmp, ref MidiBard.config.bmpTrackNames))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_switch_transpose_instrument_bmp_trackname);

        var btnNameReferenceText = "View name references";
        var btnNameReferencesize = ImGuiHelpers.GetButtonSize(btnNameReferenceText);
        ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X);
        if (ImGui.Button(btnNameReferenceText))
        {
            nameReferenceWindowOpen ^= true;
        }

        //-------------------

        ImGui.Checkbox(setting_label_auto_switch_instrument_by_file_name, ref MidiBard.config.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_label_auto_switch_instrument_by_file_name);

        //-------------------

        ImGui.Checkbox(setting_label_auto_transpose_by_file_name, ref MidiBard.config.autoTransposeBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_auto_transpose_by_file_name);

        //-------------------

        if (ImGui.Checkbox("Play Lyrics", ref MidiBard.config.playLyrics))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip("Choose this if you want to post lyrics.");

        //-------------------

        if (ImGui.Checkbox(setting_label_auto_adapt_notes, ref MidiBard.config.AdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_adapt_notes);

        //-------------------

        if (ImGui.Checkbox(setting_label_auto_align_loaded_midi, ref MidiBard.config.AlignMidi))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_align_loaded_midi);

        //-------------------

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted(setting_label_tone_mode);
        if (ImGuiUtil.EnumCombo($"##{setting_label_tone_mode}", ref MidiBard.config.GuitarToneMode, toneModeToolTips))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_tone_mode);

        //-------------------

        // var itemWidth = ImGuiHelpers.GlobalScale * 100;
        // SetNextItemWidth(itemWidth);
        ImGui.TextUnformatted($"Delay between songs (solo only)");
        if (ImGui.InputFloat($"##{setting_label_song_delay}", ref MidiBard.config.SecondsBetweenTracks, 0.5f, 0.5f, $" {MidiBard.config.SecondsBetweenTracks:f2} s", ImGuiInputTextFlags.AutoSelectAll))
        {
            MidiBard.config.SecondsBetweenTracks = Math.Max(0, MidiBard.config.SecondsBetweenTracks);
            IPCHandles.SyncAllSettings();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SecondsBetweenTracks = 3;
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_song_delay);

        //-------------------

        // SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
        // SetNextItemWidth(itemWidth);
        ImGui.TextUnformatted($"Global transpose");
        if (ImGui.InputInt($"##{setting_label_transpose_all}", ref MidiBard.config.TransposeGlobal, 12))
        {
            MidiBard.config.SetTransposeGlobal(MidiBard.config.TransposeGlobal);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.SetTransposeGlobal(0);
            IPC.IPCHandles.GlobalTranspose(MidiBard.config.TransposeGlobal);
        }
        ImGuiUtil.ToolTip(setting_tooltip_transpose_all);

        //-------------------

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // if (ImGui.TreeNodeEx("Post song name to chat", ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.DefaultOpen))
        if (ImGui.CollapsingHeader("Post song name to chat", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            // var available = ImGui.GetContentRegionAvail();
            // ImGui.SetNextItemWidth(available.X);

            if (ImGui.Checkbox("Auto send song name to chat on play", ref MidiBard.config.autoPostSongName))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Check this if you want to auto send song name to chat");

            ImGui.TextUnformatted("Song name regex");
            if (ImGui.InputTextWithHint("##songNameRegex", "", ref MidiBard.config.userSongNameRegex, 1000))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGui.SameLine();
            var outlineColor = KnownColor.Black.Vector();
            var iconColor = KnownColor.Orange.Vector();
            ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.ExclamationCircle, outlineColor, iconColor);
            ImGuiUtil.ToolTip("""
            This is used to capture information from file name to post into chat

            Example file naming pattern:
              Artist - Song Name (solo).mid
              Regex: ^(.*?)\s*-\s*(.+?)(?:\s*\([^)]*\))?\s*$

            This capture 2 groups:
              $1 => with artist name
              $2 => with song name

          The easiest way to build this expression is to ask some AI, send your song naming pattern with examples and ask it to generate a regular expression to capture the parts you want

          Example prompt:
          I need a regular expression (only the expression part) to capture the artist and the song name into groups, the song name pattern is as follows:
            Taylor Swift - Shake It Off
            Taylor Swift - You Belong with Me
            Taylor Swift - Love Story

          There may be some optional part in parentheses after the song name that can be ignored like
            Queen - Bohemian Rhapsody (solo)
            Luis Fonsi - Despacito (trio)
          """);
            ImGui.Spacing();

            //-------------------

            ImGui.TextUnformatted("Song name regex output format (capture groups)");
            if (ImGui.InputTextWithHint("##songNameRegexReplace", "♪ Artist: $1 - Song: $2 ♪", ref MidiBard.config.userSongNameRegexCaptureGroups, 1000))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGui.SameLine();

            ImGuiUtil.DrawFontawesomeIconOutlined(FontAwesomeIcon.ExclamationCircle, outlineColor, iconColor);
            // var tooltipBgColor = new Vector4(0.0f, 0.9804f, 1.0f, 0.3f);

            ImGuiUtil.ToolTip("""
                This is where you define how to show the captured information from file name
                (the captured parts are represented by $1, $2, $3 etc)

                Example:
                Artist - Song Name
                $1 - $2

                or to show only song name
                $2
            """);
            ImGui.Spacing();

            //-------------------

            ImGui.TextUnformatted(setting_label_played_song_highlight_color);
            ImGui.ColorEdit4(setting_label_played_song_highlight_color, ref MidiBard.config.playedSongColor, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                var defaultPlayedSongColor = new Vector4(0.0f, 0.9804f, 1.0f, 1.0f);
                MidiBard.config.playedSongColor = defaultPlayedSongColor;
            }

            ImGui.Unindent();
        }

        //-------------------

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text($"Default Performer Folder:");
        ImGui.TextUnformatted(Path.ChangeExtension(MidiBard.config.defaultPerformerFolder, null).EllipsisString(70));

        var btnChangeText = "Change";
        var btnChangeSize = ImGuiHelpers.GetButtonSize(btnChangeText);
        ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnChangeSize.X);
        if (ImGui.Button(btnChangeText))
        {
            RunSetDefaultPerformerFolderImGui();
        }

        ImGui.Spacing();
        ImGuiGroupPanel.EndGroupPanel();
    }

    private void DrawEnsembleSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_ensemble_settings);
        if (ImGui.Checkbox(setting_label_sync_clients, ref MidiBard.config.SyncClients))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_sync_clients);

        //-------------------

        ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.GetFrameHeightWithSpacing() - ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.ExchangeAlt).X);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "syncbtn", icon_button_tooltip_sync_settings))
        {
            IPCHandles.SyncAllSettings();
            IPCHandles.SyncPlaylist();
            ImGuiUtil.AddNotification(NotificationType.Info, "Synced settings and playlist");
        }

        //-------------------

        if (ImGui.Checkbox(setting_label_monitor_ensemble, ref MidiBard.config.MonitorOnEnsemble))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_monitor_ensemble);

        //-------------------

        bool pmdWasOn = MidiBard.config.playOnMultipleDevices;
        if (ImGui.Checkbox("Play on Multiple Devices", ref MidiBard.config.playOnMultipleDevices))
        {
            if (pmdWasOn || MidiBard.config.playOnMultipleDevices)
            {
                PartyChatCommand.SendPMD(MidiBard.config.playOnMultipleDevices);
            }
        }
        ImGuiUtil.ToolTip("Choose this if your bards are spread between different devices.");

        //-------------------

        if (MidiBard.config.playOnMultipleDevices)
        {
            if (ImGui.Checkbox("Using File Sharing Services", ref MidiBard.config.usingFileSharingServices))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Using File Sharing Services like Google Drive to sync songs and performer settings.");
        }

        //-------------------

        var itemWidth = -ImGui.GetCursorPosX() + ImGui.GetWindowContentRegionMin().X;
        ImGui.Checkbox(ensemble_config_Draw_ensemble_progress_indicator_on_visualizer, ref MidiBard.config.UseEnsembleIndicator);

        //-------------------

        string[] values = new string[] { "None", "Legacy", "Default" };
        var currentCompensationMode = (int)MidiBard.config.CompensationMode;
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ensemble Compensation Mode: ");
        ImGui.SetNextItemWidth(itemWidth);
        if (ImGui.Combo("##Compensation Mode", ref currentCompensationMode, values, values.Length))
        {
            MidiBard.config.CompensationMode = (Configuration.CompensationModes)currentCompensationMode;
            IPCHandles.SyncAllSettings();
        }
        ImGui.EndGroup();
        ImGuiUtil.ToolTip("""
            Ensemble instrument compensation mode selection:

          - None: No instrument delay compensation for instruments is performed during ensemble mode, which may result a lack of alignment between instruments during ensemble play.Choose this option only if your MIDI file already has instrument delay compensation.

          - Legacy: Allows you to adjust the delay compensation value for each instrument, but notes of different pitches for the same instrument may not align perfectly.

          - Default: New default instrument delay compensation mode, with different compensation times for notes of different pitches, useful for instruments such as clarinet and bass drum.

          """);

        if (MidiBard.config.CompensationMode == Configuration.CompensationModes.ByInstrument)
        {
            if (ImGui.Button("Edit Instrument Compensations"))
            {
                compensationEditWindowOpen ^= true;
            }
        }

        //-------------------

        // ImGui.Spacing();
        // ImGui.Separator();
        // ImGui.Spacing();

        // if (ImGui.CollapsingHeader("Ensemble party members config", ImGuiTreeNodeFlags.DefaultOpen))
        // {
        //     ImGui.Indent();

        //     DrawPartyListOrderManager();

        //     ImGui.Unindent();
        // }

        ImGuiGroupPanel.EndGroupPanel();
    }

    private void RunSetDefaultPerformerFolderImGui()
    {
        fileDialogManager.OpenFolderDialog("Set Default Performer Folder", (result, filePath) =>
        {
            // PluginLog.Debug($"dialog result: {result}\n{string.Join("\n", filePath)}");
            if (result)
            {
                MidiFileConfigManager.SetDefaultPerformerFolder(filePath);
                MidiBard.SaveConfig();
                IPCHandles.SyncAllSettings();
                IPCHandles.UpdateDefaultPerformer();
            }
        }, MidiBard.config.defaultPerformerFolder);
    }

    private void DrawCompensationEditWindow()
    {
        if (!compensationEditWindowOpen) return;

        if (ImGui.Begin("Instrument Delay Compensation", ref compensationEditWindowOpen))
        {
            if (ImGui.BeginTable("ins", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Instrument", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Compensation(ms)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var instrument in MidiBard.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    ImGui.Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(ImGui.GetFrameHeight()));
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(instrument.FFXIVDisplayName);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var compensationMs = MidiBard.config.LegacyInstrumentCompensation[(int)instrument.Row.RowId];
                    if (ImGui.InputInt($"##{instrument.Row.RowId}", ref compensationMs, 1, 1))
                    {
                        compensationMs = compensationMs.Clamp(0, 500);
                        MidiBard.config.LegacyInstrumentCompensation[(int)instrument.Row.RowId] = compensationMs;
                        IPCHandles.SyncAllSettings();
                    }
                }
                ImGui.EndTable();
            }

            if (ImGui.Button("Reset to default"))
            {
                MidiBard.config.LegacyInstrumentCompensation = EnsembleManager.GetCompensationAver();
                IPCHandles.SyncAllSettings();
            }
        }
        ImGui.End();
    }

    private static string SanitizeIntrumentName(string input)
    {
        return Regex.Replace(input, "[^a-zA-Z]", "");
    }

    private void DrawNameReferenceWindow()
    {
        if (!nameReferenceWindowOpen) return;
        if (ImGui.Begin("Track Name References For Auto-Switch Instruments", ref nameReferenceWindowOpen))
        {
            if (ImGui.BeginTable("ins", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Track Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                foreach (var instrument in MidiBard.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    // Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(ImGui.GetFrameHeight()));
                    ImGui.Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(40, 40));

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGuiUtil.TextCopyable(SanitizeIntrumentName(instrument.FFXIVDisplayName));
                    ImGuiUtil.ToolTip("Click to copy the name");
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }

    // private void DrawPartyListOrderManager()
    // {
    //     var partyMembers = api.PartyList.Select((partyMember) => partyMember.GetPartyMemberData()).ToList();

    //     ImGui.TextUnformatted("Display order and track assign");
    //     ImGui.Columns(2, "EnsemblePlayerConfigList", false);
    //     for (int i = 0; i < MidiBard.config.ensemblePlayersConfig?.Count; i++)
    //     {
    //         ImGui.PushID(i);
    //         ImGui.Text($"#{i + 1}");
    //         ImGui.SameLine();

    //         ImGui.SetNextItemWidth(-1);
    //         ImGui.TextUnformatted($"{MidiBard.config.ensemblePlayersConfig[i].Name}");
    //         // if (ImGui.InputText("##Name", ref bar.Name, 32))
    //         //     QoLBar.Config.Save();

    //         // textsize = ImGui.GetItemRectSize();

    //         ImGui.NextColumn();
    //         if (ImGui.Button("↑"))
    //         {
    //             MidiBard.config.ChangeEnsemblePlayerConfigOrder(MidiBard.config.ensemblePlayersConfig[i].Cid, -1);
    //         }

    //         ImGui.SameLine();
    //         if (ImGui.Button("↓"))
    //         {
    //             MidiBard.config.ChangeEnsemblePlayerConfigOrder(MidiBard.config.ensemblePlayersConfig[i].Cid, 1);
    //         }

    //         ImGui.SameLine();
    //         if (ImGui.Button(" X "))
    //         {
    //             MidiBard.config.RemoveEnsemblePlayerConfig(MidiBard.config.ensemblePlayersConfig[i].Cid);
    //         }

    //         ImGui.Separator();
    //         ImGui.NextColumn();
    //         ImGui.PopID();
    //     }

    //     ImGui.Spacing();
    //     ImGui.Spacing();

    //     ImGui.TextUnformatted("Available party members");
    //     if (ImGui.BeginCombo("##partyMemberSelectList", "Select"))
    //     {
    //         for (int i = 0; i < partyMembers.Count; i++)
    //         {
    //             var partyMember = partyMembers[i];
    //             var isCidInConfigList = MidiBard.config.ensemblePlayersConfig?.Any(p => p.Cid == partyMember.playerCid) ?? false;
    //             if (!isCidInConfigList)
    //             {
    //                 var playerInfo = $"{partyMember.playerName}@{partyMember.playerWorld}";
    //                 if (ImGui.Selectable($"{playerInfo}##{i}", false))
    //                 {
    //                     MidiBard.config.AddEnsemblePlayerConfig(new EnsemblePlayerConfig { Cid = partyMember.playerCid, Name = playerInfo, TrackNameRegexRule = "" });
    //                     IPCHandles.SyncAllSettings();
    //                 }
    //             }
    //         }
    //         ImGui.EndCombo();
    //     }
    // }
}
