using System;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using MidiBard.Resources;
using MidiBard.Util;
using MidiBard.Util.Lyrics;
using MidiBard.Extensions.Dalamud;
using MidiBard.Util.ImGuiExt;
using MidiBard.Extensions.General;

namespace MidiBard;

public partial class SettingsWindow2
{
    // Shared culture-invalidated cache for all SettingsWindow label/tooltip arrays.
    // Rebuilt once per language change instead of allocating new arrays every frame.
    private static System.Globalization.CultureInfo? s_settingsCulture;
    private static string[]? s_toneModeToolTips;
    private static string[]? s_toneModeLabels;
    private static string[]? s_antiStackNoteLabels;

    private static void EnsureSettingsCacheValid()
    {
        if (s_settingsCulture == Language.Culture) return;
        s_settingsCulture = Language.Culture;
        s_toneModeToolTips =
        [
            Language.perf_tone_mode_off_tooltip,
            Language.perf_tone_mode_standard_tooltip,
            Language.perf_tone_mode_simple_tooltip,
            Language.perf_tone_mode_override_by_track_tooltip,
            Language.perf_tone_mode_program_electric_guitar_tooltip,
        ];
        s_toneModeLabels =
        [
            Language.perf_tone_mode_off,
            Language.perf_tone_mode_standard,
            Language.perf_tone_mode_simple,
            Language.perf_tone_mode_override_by_track,
            Language.perf_tone_mode_program_electric_guitar,
        ];
        s_antiStackNoteLabels =
        [
            Language.perf_anti_stack_off,
            Language.perf_anti_stack_keep_first,
            Language.perf_anti_stack_keep_shortest,
            Language.perf_anti_stack_keep_longest,
        ];
        s_themeLabels =
        [
            Language.theme_default,
            Language.theme_dark,
            Language.theme_modern_dark,
            Language.theme_light,
            Language.theme_ocean_fishing,
            Language.theme_deepblue,
            Language.theme_catnip,
            Language.theme_chocobo,
            Language.theme_dracula,
            Language.theme_neon,
            Language.theme_purple,
            Language.theme_wine,
            Language.theme_barbie_pink,
            Language.theme_cotton_candy,
            Language.theme_tropical,
            Language.theme_sunset,
            Language.theme_orange,
        ];
        s_compensationModeLabels =
        [
            Language.ensemble_comp_mode_none,
            Language.ensemble_comp_mode_manual,
            Language.ensemble_comp_mode_default,
        ];
    }

    private void DrawPerformanceSettings()
    {
        EnsureSettingsCacheValid();
        DrawInstrumentNameReferenceWindow();

        using (ImGuiGroupPanel.BeginGroupPanel(Language.setting_perf_group_label))
        {
            if (ImGui.Checkbox(Language.setting_perf_auto_switch_instrument_trackname, ref Plugin.Config.bmpTrackNames))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_perf_auto_switch_instrument_trackname_tooltip);

            ImGui.SameLine();
            // var btnNameReferencesize = ImGuiHelpers.GetButtonSize(btnNameReferenceText);
            // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X);
            if (ImGuiUtil.InfoIconButton(FontAwesomeIcon.InfoCircle, "btnInstrumentsNameReference", "Click to show instruments name reference"))
            {
                showInstrumentNameReferenceWindow ^= true;
            }

            //-------------------

            ImGui.Checkbox(Language.setting_perf_auto_switch_instrument_filename, ref Plugin.Config.autoSwitchInstrumentBySongName);
            ImGuiUtil.ToolTip(Language.setting_perf_auto_switch_instrument_filename_tooltip);

            //-------------------

            ImGui.Checkbox(Language.setting_perf_auto_transpose_filename, ref Plugin.Config.autoTransposeBySongName);
            ImGuiUtil.ToolTip(Language.setting_perf_auto_transpose_filename_tooltip);

            //-------------------

            if (ImGui.Checkbox(Language.setting_perf_auto_align_midi, ref Plugin.Config.AlignMidi))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_perf_auto_align_midi_tooltip);

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButtonToggle("##btnUiShowAutoAlignMidi", ref Plugin.Config.UiShowAutoAlignMidi,
            //     FontAwesomeIcon.Eye,
            //     FontAwesomeIcon.EyeSlash,
            //     Language.setting_interface_show_hide_elements)
            // )
            // {
            //     Plugin.IpcProvider.SyncAllSettings();
            // }

            if (Plugin.Config.AlignMidi)
            {
                ImGui.Spacing();
                ImGui.Indent(ImGui.GetStyle().IndentSpacing * 2);
                ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputDouble($"Align start offset", ref Plugin.Config.AlignMidiStartOffset, 0.1f, 0.1f, $" {Plugin.Config.AlignMidiStartOffset:f2} s", ImGuiInputTextFlags.AutoSelectAll))
                {
                    Plugin.Config.AlignMidiStartOffset = Math.Clamp(Plugin.Config.AlignMidiStartOffset, 0f, 10f);
                    Plugin.IpcProvider.SyncAllSettings();
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    Plugin.Config.AlignMidiStartOffset = 0;
                    Plugin.IpcProvider.SyncAllSettings();
                }
                ImGuiUtil.ToolTip("New song start offset, right click to reset");
                ImGui.Unindent(ImGui.GetStyle().IndentSpacing * 2);
            }

            //-------------------

            if (ImGui.Checkbox(Language.setting_perf_auto_adapt_notes, ref Plugin.Config.AdaptNotesOOR))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_perf_auto_adapt_notes_tooltip);

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButtonToggle("##btnUiShowAdaptNotesOOR", ref Plugin.Config.UiShowAdaptNotesOOR,
            //     FontAwesomeIcon.Eye,
            //     FontAwesomeIcon.EyeSlash,
            //     Language.setting_interface_show_hide_elements)
            // )
            // {
            //     Plugin.IpcProvider.SyncAllSettings();
            // }

            //-------------------

            ImGui.Text(Language.setting_perf_anti_note_stack);
            if (ImGuiUtil.EnumCombo("##comboAntiStackNote", ref Plugin.Config.AntiStackType, labelsOverride: s_antiStackNoteLabels))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }

            //-------------------

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(Language.setting_perf_tone_mode);
            if (ImGuiUtil.EnumCombo("##comboGuitarToneMode", ref Plugin.Config.GuitarToneMode, labelsOverride: s_toneModeLabels, toolTips: s_toneModeToolTips))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_perf_tone_mode_tooltip);

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButtonToggle("##btnUiShowGuitarToneMode", ref Plugin.Config.UiShowGuitarToneMode,
            //     FontAwesomeIcon.Eye,
            //     FontAwesomeIcon.EyeSlash,
            //     Language.setting_interface_show_hide_elements)
            // )
            // {
            //     Plugin.IpcProvider.SyncAllSettings();
            // }

            //-------------------

            ImGui.Text(Language.setting_perf_play_speed);
            if (ImGui.InputFloat("##inputPlaySpeed", ref Plugin.Config.PlaySpeed, 0.1f, 0.5f, Plugin.CurrentBardPlayback?.GetBpmLabel(), ImGuiInputTextFlags.AutoSelectAll))
            {
                Plugin.Config.PlaySpeed = Plugin.Config.PlaySpeed.Clamp(0.1f, 10f);
                Plugin.CurrentBardPlayback.SetSpeed(Plugin.Config.PlaySpeed);
            }
            if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                Plugin.Config.PlaySpeed = 1;
                Plugin.CurrentBardPlayback.SetSpeed(Plugin.Config.PlaySpeed);
            }
            ImGuiUtil.ToolTip(Language.setting_perf_play_speed_tooltip);

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButtonToggle("##btnUiShowPlaySpeed", ref Plugin.Config.UiShowPlaySpeed,
            //     FontAwesomeIcon.Eye,
            //     FontAwesomeIcon.EyeSlash,
            //     Language.setting_interface_show_hide_elements)
            // )
            // {
            //     Plugin.IpcProvider.SyncAllSettings();
            // }

            //-------------------

            // SameLine(ImGuiUtil.GetWindowContentRegionWidth() / 2f);
            // SetNextItemWidth(itemWidth);
            ImGui.Text(Language.setting_perf_global_transpose);
            if (ImGui.InputInt("##inputGlobalTranspose", ref Plugin.Config.TransposeGlobal, 12))
            {
                // TODO: refactor plugin dependency
                Plugin.Config.SetTransposeGlobal(Plugin.Config.TransposeGlobal, Plugin);
                Plugin.IpcProvider.GlobalTranspose(Plugin.Config.TransposeGlobal);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                // TODO: refactor plugin dependency
                Plugin.Config.SetTransposeGlobal(0, Plugin);
                Plugin.IpcProvider.GlobalTranspose(Plugin.Config.TransposeGlobal);
            }
            ImGuiUtil.ToolTip(Language.setting_perf_transpose_tooltip);

            // ImGui.SameLine();
            // if (ImGuiUtil.IconButtonToggle("##btnUiShowTransposeGlobal", ref Plugin.Config.UiShowTransposeGlobal,
            //     FontAwesomeIcon.Eye,
            //     FontAwesomeIcon.EyeSlash,
            //     Language.setting_interface_show_hide_elements)
            // )
            // {
            //     Plugin.IpcProvider.SyncAllSettings();
            // }

            //-------------------

            // var itemWidth = ImGuiHelpers.GlobalScale * 100;
            // SetNextItemWidth(itemWidth);
            ImGui.Text(Language.setting_perf_delay_between_songs);
            if (ImGui.InputFloat("##inputSongDelay", ref Plugin.Config.SecondsBetweenTracks, 0.5f, 0.5f, $" {Plugin.Config.SecondsBetweenTracks:f2} s", ImGuiInputTextFlags.AutoSelectAll))
            {
                Plugin.Config.SecondsBetweenTracks = Math.Max(0, Plugin.Config.SecondsBetweenTracks);
                Plugin.IpcProvider.SyncAllSettings();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                Plugin.Config.SecondsBetweenTracks = 3;
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip(Language.setting_perf_song_delay_tooltip);

            //-------------------

            ImGui.Text(Language.setting_perf_default_instrument);
            DrawDefaultInstrumentComboBox();
            ImGuiUtil.HelpMarker("Default instrument if the track or file name doesn't contain a recognizable instrument name");
            ImGui.SameLine();
            if (ImGui.Checkbox("Force Default Instrument", ref Plugin.Config.ForceDefaultInstrument))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Force all tracks to use the default instrument, even if they have a recognizable one");
        }

        ImGui.Spacing();

        DrawPostSongOptions();

        ImGuiUtil.Spacing(3);

        DrawLyricsOptions();

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawInstrumentNameReferenceWindow()
    {
        if (!showInstrumentNameReferenceWindow) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(250, 100) * ImGuiHelpers.GlobalScale, ImGuiHelpers.MainViewport.Size);
        if (ImGui.Begin("Track Name References For Auto-Switch Instruments", ref showInstrumentNameReferenceWindow))
        {
            if (ImGui.BeginTable("###InstrumentReferenceTable", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("##InstrumentImage", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Track Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var instrument in InstrumentHelper.Instruments)
                {
                    if (instrument.Row.RowId == 0) continue;
                    ImGui.TableNextColumn();
                    DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(40, 40));
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

    private void DrawPostSongOptions()
    {
        if (ImGui.CollapsingHeader(Language.setting_chat_post_song_header, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(Language.setting_chat_auto_send_song_name, ref Plugin.Config.PostSong.Enabled))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Check this if you want to auto send song name to chat on play");

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.Text(Language.setting_chat_song_name_chat_target);
            if (OutputChannelCombo.Draw("##comboPostSongChatTarget", ref Plugin.Config.PostSong.ChatTarget))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }

            // Mode selector

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Mode");
            ImGui.SameLine();

            int modeInt = (int)Plugin.Config.PostSong.Mode;
            if (ImGui.RadioButton("DB Template##postSongMode0", ref modeInt, 0))
            {
                Plugin.Config.PostSong.Mode = PostSongMode.DatabaseTemplate;
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Build the chat message from a template using {Token} placeholders filled from the song's database fields.");

            ImGui.SameLine();

            if (ImGui.RadioButton("Filepath Regex##postSongMode1", ref modeInt, 1))
            {
                Plugin.Config.PostSong.Mode = PostSongMode.FilepathRegex;
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Build the chat message by applying a capture regex to the file name.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Mode-specific UI

            if (Plugin.Config.PostSong.Mode == PostSongMode.DatabaseTemplate)
            {
                DrawPostSongTemplateMode();
            }
            else
            {
                DrawPostSongRegexMode();
            }

            // Post-processing (both modes)

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Text(Language.setting_chat_sanitize_song_name);
            ImGui.Spacing();

            ImGui.Text(Language.common_label_find);
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputTextWithHint("##postSongFindRegex", "", ref Plugin.Config.PostSong.FindRegex, 1000))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            Enter expression to replace unwanted characters

            Example file name:
                Taylor_Swift - Shake_It_Off

            Find all underscore:
                _
            """);

            ImGui.Text(Language.common_label_replace_by);
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputTextWithHint("##postSongReplacement", "", ref Plugin.Config.PostSong.Replacement, 1000))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker("""
            Example:
                Replace all found characters by another one like blank space

            Result in:
                Taylor Swift - Shake It Off
            """);

            ImGui.Spacing();

            ImGui.Unindent();
        }
    }

    private void DrawPostSongTemplateMode()
    {
        ImGui.Text("Template");
        ImGui.SetNextItemWidth(520 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##postSongTemplate", "{SongName}", ref Plugin.Config.PostSong.Template, 1000))
        {
            Plugin.IpcProvider.SyncAllSettings();
        }
        ImGuiUtil.HelpMarker("""
        Build the chat message from a template using {Token} placeholders filled
        from the song's database fields.

        Available tokens:
            {SongName}   - song name
            {Artist}     - artist
            {Year}       - release year
            {Duration}   - duration (m:ss)
            {Comments}   - comments
            {Tag[0]}     - first tag
            {Tag[1]}     - second tag  (and so on)

        Examples:
            Template:  ♪ {SongName} - {Artist} ♪
            Display:   ♪ Bohemian Rhapsody - Queen ♪

            Template:  Now playing: {SongName}
            Display:   Now playing: Bohemian Rhapsody
        """);
    }

    private void DrawPostSongRegexMode()
    {
        ImGui.Text(Language.setting_chat_regex_output_format);
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text(Language.setting_chat_capture_regex);
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##PostSongCaptureRegex", "", ref Plugin.Config.PostSong.CaptureRegex, 1000))
        {
            Plugin.IpcProvider.SyncAllSettings();
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

        ImGui.BeginGroup();
        ImGui.Text(Language.setting_chat_output_format);
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##PostSongOutputFormat", "♪ Artist: $1 - Song: $2 ♪", ref Plugin.Config.PostSong.OutputFormat, 1000))
        {
            Plugin.IpcProvider.SyncAllSettings();
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

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Asterisk, "##CopyRegexExample", "Copy regex example for pattern: Arist - Song Name"))
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
            In some cases, the song name may be followed by extra information in parentheses (e.g., "(solo)", "(quartet)", "(octet) 2008"). This extra part should be ignored completely-it must not be included in the song name group.

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
            WindowsApi.OpenUrl("https://regex101.com/");
        }
    }

    private void DrawLyricsOptions()
    {
        if (ImGui.CollapsingHeader(Language.setting_chat_lyrics_header, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(Language.setting_chat_play_lyrics, ref Plugin.Config.playLyrics))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.HelpMarker(Language.setting_chat_lyrics_display_tooltip);

            // var btnNameReferencesize = ImGuiHelpers.GetButtonSize(Language.setting_chat_export_lrc_template);
            // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnNameReferencesize.X); // end of line
            ImGui.SameLine();
            if (ImGui.Button(Language.setting_chat_export_lrc_template))
            {
                Lyrics.ExportLrcTemplate(Plugin.Config.defaultPerformerFolder + $@"\LyricsTemplateExample.lrc");
                WindowsApi.OpenFolder(Plugin.Config.defaultPerformerFolder);
                ImGuiUtil.AddNotification(NotificationType.Success, $"Lrc template exported");
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.Text(Language.setting_chat_lyrics_chat_target);
            if (OutputChannelCombo.Draw("##comboLyricsChatTarget", ref Plugin.Config.LyricsChatTarget))
            {
                Plugin.IpcProvider.SyncAllSettings();
            }

            ImGui.Unindent();
        }
    }
    private static string SanitizeIntrumentName(string input)
    {
        return Regex.Replace(input, "[^a-zA-Z]", "");
    }

    private void DrawDefaultInstrumentComboBox()
    {
        if (ImGui.BeginCombo("##DefaultInstrumentCombo", InstrumentHelper.InstrumentStrings[Plugin.Config.DefaultInstrumentId], ImGuiComboFlags.HeightLarge))
        {
            ImGui.GetWindowDrawList().ChannelsSplit(2);
            for (uint i = 0; i < InstrumentHelper.Instruments.Length; i++)
            {
                var instrument = InstrumentHelper.Instruments[i];
                ImGui.GetWindowDrawList().ChannelsSetCurrent(1);
                DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(ImGui.GetTextLineHeightWithSpacing()));

                ImGui.SameLine();
                ImGui.GetWindowDrawList().ChannelsSetCurrent(0);
                ImGui.AlignTextToFramePadding();

                if (ImGui.Selectable($"{SanitizeIntrumentName(instrument.InstrumentString)}####InputDefaultInstrumentId_{i}", Plugin.Config.DefaultInstrumentId == i, ImGuiSelectableFlags.SpanAllColumns))
                {
                    Plugin.Config.DefaultInstrumentId = i;
                    Plugin.IpcProvider.SyncAllSettings();
                }
            }

            ImGui.GetWindowDrawList().ChannelsMerge();
            ImGui.EndCombo();
        }
    }
}
