using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using static Dalamud.api;
using static MidiBard2.Resources.Language;

namespace MidiBard;

public partial class PluginUI
{
    private bool showSettingsWindow = false;
    private bool showCompensationEditWindow = false;
    private bool showInstrumentNameReferenceWindow = false;

    private readonly string[] toneModeToolTips = {
        "Off: Does not take over game's guitar tone control.",
        "Standard: Standard midi channel and ProgramChange handling, each channel will keep it's program state separately.",
        "Simple: Simple ProgramChange handling, ProgramChange event on any channel will change all channels' program state. (This is BardMusicPlayer's default behavior.)",
        "Override by track: Assign guitar tone manually for each track and ignore ProgramChange events.",
    };

    public void ToggleSettingsWindow()
    {
        if (showSettingsWindow)
            CloseSettingsWindow();
        else
            OpenSettingsWindow();
    }

    public void OpenSettingsWindow()
    {
        showSettingsWindow = true;
    }

    public void CloseSettingsWindow()
    {
        showSettingsWindow = false;
    }

    private void DrawSettigsWindow()
    {
        if (!showSettingsWindow) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(350, 100) * ImGuiHelpers.GlobalScale, ImGuiHelpers.MainViewport.Size);
        ImGui.Begin("MidiBard Settings", ref showSettingsWindow);

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
            ImGui.EndTabBar();
        }

        ImGui.End();

        DrawInstrumentNameReferenceWindow();
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
            ImGuiUtil.ToolTip(setting_label_auto_open_when_performing);

            if (ImGui.Checkbox(setting_label_auto_close_when_performing, ref MidiBard.config.AutoClosePlayerWhenPerforming))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_auto_close_when_performing);

            //-------------------

            if (ImGui.Checkbox(setting_label_show_now_playing_info, ref MidiBard.config.showNowPlayingInfo))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(setting_label_show_now_playing_info);

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
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.TextUnformatted(setting_label_theme_color);
            ImGui.Spacing();
            ImGui.ColorEdit4("##{setting_label_theme_color}", ref MidiBard.config.themeColor,
                ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.AlphaBar);

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##btnResetUIColor", "Reset"))
            {
                MidiBard.config.themeColor = Theme.Colors.Lavender;
                IPCHandles.SyncAllSettings();
            }
            //-------------------

            ImGui.Spacing();
            ImGui.TextUnformatted(setting_label_played_song_highlight_color);
            ImGui.Spacing();
            ImGui.ColorEdit4(setting_label_played_song_highlight_color, ref MidiBard.config.playedSongColor, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel);
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##btnResetSongHighlightColor", "Reset"))
            {
                MidiBard.config.playedSongColor = Theme.Colors.Cyan;
                IPCHandles.SyncAllSettings();
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Combo(setting_label_select_ui_language, ref MidiBard.config.uiLang, uilangStrings,
                    uilangStrings.Length))
            {
                MidiBard.ConfigureLanguage(MidiBard.GetCultureCodeString((MidiBard.CultureCode)MidiBard.config.uiLang));
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

            //-------------------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Spacing();

            if (ImGui.Button("Open Settings Folder"))
            {
                Util.Extensions.OpenFolder(api.PluginInterface.ConfigDirectory.FullName);
                ImGuiUtil.AddNotification(NotificationType.Success, $"Settings exported");
            }

            ImGui.Spacing();
            ImGui.Spacing();
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

        ImGui.SameLine();
        // var btnNameReferencesize = ImGuiHelpers.GetButtonSize(btnNameReferenceText);
        // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X);
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Current.Button.InfoNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Current.Button.InfoHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Current.Button.InfoActive);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.InfoCircle, "btnInstrumentsNameReference", "Click to show instruments name reference"))
        {
            showInstrumentNameReferenceWindow ^= true;
        }
        ImGui.PopStyleColor(3);

        //-------------------

        ImGui.Checkbox(setting_label_auto_switch_instrument_by_file_name, ref MidiBard.config.autoSwitchInstrumentBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_label_auto_switch_instrument_by_file_name);

        //-------------------

        ImGui.Checkbox(setting_label_auto_transpose_by_file_name, ref MidiBard.config.autoTransposeBySongName);
        ImGuiUtil.ToolTip(setting_tooltip_auto_transpose_by_file_name);

        //-------------------

        if (ImGui.Checkbox(setting_label_auto_align_loaded_midi, ref MidiBard.config.AlignMidi))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_align_loaded_midi);

        //-------------------

        if (ImGui.Checkbox(setting_label_auto_adapt_notes, ref MidiBard.config.AdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_auto_adapt_notes);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowAdaptNotesOOR", "Show/Hide in main window", ref MidiBard.config.UiShowAdaptNotesOOR))
        {
            IPCHandles.SyncAllSettings();
        }

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

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowGuitarToneMode", "Show/Hide in main window", ref MidiBard.config.UiShowGuitarToneMode))
        {
            IPCHandles.SyncAllSettings();
        }

        //-------------------

        ImGui.TextUnformatted(setting_label_set_play_speed);
        if (ImGui.InputFloat($"##{setting_label_set_play_speed}", ref MidiBard.config.PlaySpeed, 0.1f, 0.5f, GetBpmString(), ImGuiInputTextFlags.AutoSelectAll))
        {
            SetSpeed();
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            MidiBard.config.PlaySpeed = 1;
            SetSpeed();
        }
        ImGuiUtil.ToolTip(setting_tooltip_set_speed);

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowPlaySpeed", "Show/Hide in main window", ref MidiBard.config.UiShowPlaySpeed))
        {
            IPCHandles.SyncAllSettings();
        }

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

        ImGui.SameLine();
        if (ImGuiUtil.ToggleShowHideButton("##btnUiShowTransposeGlobal", "Show/Hide in main window", ref MidiBard.config.UiShowTransposeGlobal))
        {
            IPCHandles.SyncAllSettings();
        }

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

        ImGuiGroupPanel.EndGroupPanel();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        DrawLyricsOptions();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Spacing();

        DrawPostSongOptions();

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawLyricsOptions()
    {
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Current.Header.Normal);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Current.Header.Hovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Current.Header.Active);

        if (ImGui.CollapsingHeader("Lyrics", ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(setting_tooltip_play_lyrics, ref MidiBard.config.playLyrics))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            To display lyrics, place a .lrc file with the same name as the MIDI file in the same folder.
            """);

            var btnLabelExportLrc = "Export Lyrics File Template";
            var btnNameReferencesize = ImGuiHelpers.GetButtonSize(btnLabelExportLrc);
            // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X); // end of line
            ImGui.SameLine();
            if (ImGui.Button(btnLabelExportLrc))
            {
                Lrc.ExportLrcTemplate();
                Util.Extensions.OpenFolder(MidiBard.config.defaultPerformerFolder);
                ImGuiUtil.AddNotification(NotificationType.Success, $"Lrc template exported");
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.TextUnformatted("Select chat to send lyrics");
            if (ImGuiUtil.EnumCombo($"##LyricsChatTarget", ref MidiBard.config.LyricsChatTarget))
            {
                IPCHandles.SyncAllSettings();
            }

            ImGui.Unindent();
        }

        ImGui.PopStyleColor(3);
    }

    private void DrawPostSongOptions()
    {
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Current.Header.Normal);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Current.Header.Hovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Current.Header.Active);

        if (ImGui.CollapsingHeader("Post song to chat", ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();
            // var available = ImGui.GetContentRegionAvail();
            // ImGui.SetNextItemWidth(available.X);

            if (ImGui.Checkbox("Auto send song name to chat on play", ref MidiBard.config.autoPostSongName))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Check this if you want to auto send song name to chat on play");

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.TextUnformatted("Select chat to send song name");
            if (ImGuiUtil.EnumCombo($"##SongNameChatTarget", ref MidiBard.config.SongNameChatTarget))
            {
                IPCHandles.SyncAllSettings();
            }

            // --------- Capture Regex ----------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted("Song name regex & output format");
            ImGui.Spacing();

            ImGui.BeginGroup();
            ImGui.TextUnformatted("Capture regex");
            ImGui.SetNextItemWidth(250f);
            if (ImGui.InputTextWithHint("##postSongNameCaptureRegex", "", ref MidiBard.config.postSongNameCaptureRegex, 1000))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            Use this to capture information from file name to post into chat

                Example file naming pattern:
                    Artist - Song Name.mid
                    Taylor Swift - Shake It Off.mid
                Regex:
                    ^(.*?) - (.*?)

                This captures 2 groups:
                $1 => artist name (Taylor Swift)
                $2 => song name (Shake It Off)

            All files must follow same pattern to it work, if you have variations you need add these variations to the expression to it work properly
                Example:
                    Taylor Swift - Shake It Off (trio).mid
                    Taylor Swift - Shake It Off (duo).mid
                    Taylor Swift - Shake It Off (quartet).mid

                Regex need to be adjusted to:
                    ^(.*?) - (.*?)(?:\(.*)?$

            This will capture artist and song name and ignore anything after first parentesis (

            The easiest way to build this expression is to ask an AI, send your song naming pattern with examples and ask it to generate a regex to capture the parts you want.
            """);
            ImGui.EndGroup();

            ImGui.SameLine();

            // --------- Output Format ----------

            ImGui.BeginGroup();
            ImGui.TextUnformatted("Output format");
            ImGui.SetNextItemWidth(250f);
            if (ImGui.InputTextWithHint("##postSongNameOutputFormat", "♪ Artist: $1 - Song: $2 ♪", ref MidiBard.config.postSongNameCaptureOutputFormat, 1000))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            Define the output format for the captured information from the file name.
            Captured parts are represented by $1 $2 $3 etc and this is where song info will be placed and you may insert any text between it

            Examples:
                Format:  $1 - $2
                Display: Artist - Song Name

                Format:  Now playing: ♪ $1 - $2 ♪
                Display: Now playing: ♪ Artist - Song Name ♪

                Format:  $2
                Display: Song Name
            """);
            ImGui.EndGroup();

            ImGui.Spacing();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Asterisk, "##CopyRegexRxample", "Copy regex example for pattern: Arist - Song Name"))
            {
                ImGui.SetClipboardText("^(.*?) - (.*?)");
                ImGuiUtil.AddNotification(NotificationType.Info, "Copied to clipboard");
            }

            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Comment, "CopyAIPromptExample", "Copy AI prompt example"))
            {
                ImGui.SetClipboardText("""
                Provide only the regular expression (no explanation) that captures the artist and the song name into separate groups, following this pattern:
                    Artist - Song Name
                    Examples:
                    Queen - Bohemian Rhapsody
                    Nirvana - Smells Like Teen Spirit
                    Adele - Rolling in the Deep
                    Michael Jackson - Billie Jean
                    Coldplay - Viva La Vida
                In some cases, the song name may be followed by extra information in parentheses (e.g., "(solo)", "(quartet)", "(octet) 2008"). This extra part should be ignored completely—it must not be included in the song name group.

                Examples to ignore the extra info:
                    The Beatles - Let It Be (solo)
                    Radiohead - Creep (quartet)
                    Beyoncé - Halo (octet) 2008
                """);
                ImGuiUtil.AddNotification(NotificationType.Info, "Copied to clipboard");
            }

            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Bookmark, "##RegexTestWebsite", "Open regex test website"))
            {
                Util.Extensions.OpenUrl("https://regex101.com/");
            }

            ImGui.Spacing();

            //-------------------

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.TextUnformatted("Sanitize song name");
            ImGui.Spacing();

            // --------- Find ----------
            ImGui.BeginGroup();
            ImGui.TextUnformatted("Find");
            ImGui.SetNextItemWidth(250f);
            if (ImGui.InputTextWithHint("##postSongNameFindRegex", "", ref MidiBard.config.postSongNameFindRegex, 1000))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            Enter expression to replace unwanted characters

            Example file name:
                Taylor_Swift - Shake_It_Off

            Find all underscore:
                _
            """);
            ImGui.EndGroup();

            ImGui.SameLine();

            // --------- Replace By ----------
            ImGui.BeginGroup();
            ImGui.TextUnformatted("Replace by");
            ImGui.SetNextItemWidth(250f);
            if (ImGui.InputTextWithHint("##postSongNameReplacement", "", ref MidiBard.config.postSongNameReplacement, 1000))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            Example:
                Replace all found characters by another one like blank space

            Result in:
                Taylor Swift - Shake It Off
            """);
            ImGui.EndGroup();

            ImGui.Spacing();

            ImGui.Unindent();
        }

        ImGui.PopStyleColor(3);
    }

    private void DrawEnsembleSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(setting_group_label_ensemble_settings);
        if (ImGui.Checkbox(setting_label_sync_clients, ref MidiBard.config.SyncClients))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(setting_tooltip_sync_clients);

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

        bool chatPlaylistSyncWasOn = MidiBard.config.useChatPlaylistSync;
        if (MidiBard.config.playOnMultipleDevices)
        {
            if (ImGui.Checkbox("Use party chat for playlist sync", ref MidiBard.config.useChatPlaylistSync))
            {
                if (chatPlaylistSyncWasOn || MidiBard.config.useChatPlaylistSync)
                {
                    PartyChatCommand.SendUseChatPlaylistSync(MidiBard.config.useChatPlaylistSync);
                }
            }
            ImGuiUtil.HelpMarker("When this option is active, only the party leader can remove and reorder songs from the playlist, these options are blocked for other members.");

            ImGuiUtil.Spacing(2);

            if (ImGui.Checkbox("Using File Sharing Services", ref MidiBard.config.usingFileSharingServices))
            {
                IPCHandles.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Using File Sharing Services like Google Drive to sync songs and performer settings.");
        }

        //-------------------

        var itemWidth = -ImGui.GetCursorPosX() + ImGui.GetWindowContentRegionMin().X;
        ImGui.Checkbox(ensemble_config_draw_ensemble_progress_indicator_on_visualizer, ref MidiBard.config.UseEnsembleIndicator);

        //-------------------

        string[] values = new string[] { "None", "Manual", "Default" };
        var currentCompensationMode = (int)MidiBard.config.CompensationMode;
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ensemble Compensation Mode: ");
        ImGui.SetNextItemWidth(itemWidth);
        if (ImGui.Combo("##Compensation Mode", ref currentCompensationMode, values, values.Length))
        {
            MidiBard.config.CompensationMode = (CompensationModes)currentCompensationMode;
            IPCHandles.SyncAllSettings();
        }
        ImGui.EndGroup();
        ImGuiUtil.ToolTip("""
            Ensemble instrument compensation mode selection:

          - None: No instrument delay compensation for instruments is performed during ensemble mode, which may result a lack of alignment between instruments during ensemble play.Choose this option only if your MIDI file already has instrument delay compensation.

          - Manual: Allows you to adjust the delay compensation value for each instrument, but notes of different pitches for the same instrument may not align perfectly.

          - Default: New default instrument delay compensation mode, with different compensation times for notes of different pitches, useful for instruments such as clarinet and bass drum.

          """);

        if (MidiBard.config.CompensationMode == CompensationModes.ByInstrument)
        {
            if (ImGui.Button("Edit Instrument Compensations"))
            {
                showCompensationEditWindow ^= true;
            }
        }

        //-------------------

        ImGuiGroupPanel.EndGroupPanel();

        // ImGuiUtil.Spacing(3);
        // DrawEnsembleMembersManager();
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
                foreach (var instrument in MidiBard.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    ImGui.Image(instrument.IconTextureWrap.GetWrapOrEmpty().ImGuiHandle, new Vector2(ImGui.GetFrameHeight()));
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(SanitizeIntrumentName(instrument.FFXIVDisplayName));
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    var compensationMs = MidiBard.config.ManualInstrumentCompensation[(int)instrument.Row.RowId];
                    if (ImGui.InputInt($"##{instrument.Row.RowId}", ref compensationMs, 1, 1))
                    {
                        compensationMs = compensationMs.Clamp(0, 500);
                        MidiBard.config.ManualInstrumentCompensation[(int)instrument.Row.RowId] = compensationMs;
                        IPCHandles.SyncAllSettings();
                    }
                }
                ImGui.EndTable();
            }

            if (ImGui.Button("Reset to default"))
            {
                MidiBard.config.ManualInstrumentCompensation = EnsembleManager.GetCompensationAver();
                IPCHandles.SyncAllSettings();
            }
        }
        ImGui.End();
    }

    private void DrawInstrumentNameReferenceWindow()
    {
        if (!showInstrumentNameReferenceWindow) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(250, 100) * ImGuiHelpers.GlobalScale, ImGuiHelpers.MainViewport.Size);
        if (ImGui.Begin("Track Name References For Auto-Switch Instruments", ref showInstrumentNameReferenceWindow))
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

    private void DrawEnsembleMembersManager()
    {
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Current.Header.Normal);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Current.Header.Hovered);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Current.Header.Active);
        if (ImGui.CollapsingHeader("Ensemble party members config", ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Indent();

            var partyMembers = api.PartyList.Select((partyMember) => partyMember.GetPartyMemberData()).ToList();
            ImGui.TextUnformatted("Display order and track assign");
            ImGui.Columns(2, "EnsemblePlayerConfigList", false);
            for (int i = 0; i < MidiBard.config.EnsembleMemberConfigs.Count; i++)
            {
                ImGui.PushID(i);
                ImGui.Text($"#{i + 1}");
                ImGui.SameLine();

                ImGui.SetNextItemWidth(-1);
                ImGui.TextUnformatted($"{MidiBard.config.EnsembleMemberConfigs[i].Name}");
                // if (ImGui.InputText("##Name", ref bar.Name, 32))
                //     QoLBar.Config.Save();

                // textsize = ImGui.GetItemRectSize();

                ImGui.NextColumn();
                if (ImGui.Button("↑"))
                {
                    MidiBard.config.ChangeEnsembleMemberConfigOrder(MidiBard.config.EnsembleMemberConfigs[i].Cid, -1);
                }

                ImGui.SameLine();
                if (ImGui.Button("↓"))
                {
                    MidiBard.config.ChangeEnsembleMemberConfigOrder(MidiBard.config.EnsembleMemberConfigs[i].Cid, 1);
                }

                ImGui.SameLine();
                if (ImGui.Button(" X "))
                {
                    MidiBard.config.RemoveEnsembleMemberConfig(MidiBard.config.EnsembleMemberConfigs[i].Cid);
                }

                ImGui.Separator();
                ImGui.NextColumn();
                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.TextUnformatted("Available party members");
            if (ImGui.BeginCombo("##partyMemberSelectList", "Select"))
            {
                for (int i = 0; i < partyMembers.Count; i++)
                {
                    var partyMember = partyMembers[i];
                    var isCidInConfigList = MidiBard.config.EnsembleMemberConfigs?.Any(p => p.Cid == partyMember.playerCid) ?? false;
                    if (!isCidInConfigList)
                    {
                        var playerInfo = $"{partyMember.playerName}@{partyMember.playerWorld}";
                        if (ImGui.Selectable($"{playerInfo}##{i}", false))
                        {
                            MidiBard.config.AddEnsembleMemberConfig(new EnsembleMemberConfig { Cid = partyMember.playerCid, Name = playerInfo, TrackAssignmentRegex = "" });
                            IPCHandles.SyncAllSettings();
                        }
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Unindent();
        }

        ImGui.PopStyleColor(3);
    }

    private static string SanitizeIntrumentName(string input)
    {
        return Regex.Replace(input, "[^a-zA-Z]", "");
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

}
