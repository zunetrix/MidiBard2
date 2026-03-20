using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using MidiBard.Resources;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

namespace MidiBard;

public sealed class ChatLyricsSettingsWidget : Widget
{
    public override string Title => "Chat & Lyrics";
    public override FontAwesomeIcon Icon => FontAwesomeIcon.CommentDots;

    public ChatLyricsSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        DrawPostSongOptions();
        ImGuiUtil.Spacing(3);
        DrawLyricsOptions();
    }

    private void DrawPostSongOptions()
    {
        if (ImGui.CollapsingHeader(Language.post_song_to_chat, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(Language.auto_send_song_name_to_chat_on_play, ref Context.Plugin.Config.PostSong.Enabled))
                Context.Plugin.IpcProvider.SyncAllSettings();
            ImGuiUtil.ToolTip("Check this if you want to auto send song name to chat on play");

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.Text(Language.select_chat_to_send_song_name);
            if (OutputChannelCombo.Draw("##chatLyricsPostSongChatTarget", ref Context.Plugin.Config.PostSong.ChatTarget))
                Context.Plugin.IpcProvider.SyncAllSettings();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Mode");
            ImGui.SameLine();

            int modeInt = (int)Context.Plugin.Config.PostSong.Mode;
            if (ImGui.RadioButton("DB Template##chatLyricsPostSongMode0", ref modeInt, 0))
            {
                Context.Plugin.Config.PostSong.Mode = PostSongMode.DatabaseTemplate;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Build the chat message from a template using {Token} placeholders filled from the song's database fields.");

            ImGui.SameLine();

            if (ImGui.RadioButton("Filepath Regex##chatLyricsPostSongMode1", ref modeInt, 1))
            {
                Context.Plugin.Config.PostSong.Mode = PostSongMode.FilepathRegex;
                Context.Plugin.IpcProvider.SyncAllSettings();
            }
            ImGuiUtil.ToolTip("Build the chat message by applying a capture regex to the file name.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (Context.Plugin.Config.PostSong.Mode == PostSongMode.DatabaseTemplate)
                DrawPostSongTemplateMode();
            else
                DrawPostSongRegexMode();

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Text(Language.sanitize_song_name);
            ImGui.Spacing();

            ImGui.BeginGroup();
            ImGui.Text(Language.find);
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputTextWithHint("##chatLyricsPostSongFindRegex", "", ref Context.Plugin.Config.PostSong.FindRegex, 1000))
                Context.Plugin.IpcProvider.SyncAllSettings();
            ImGuiUtil.HelpMarker("""
            Enter expression to replace unwanted characters

            Example file name:
                Taylor_Swift - Shake_It_Off

            Find all underscore:
                _
            """);
            ImGui.EndGroup();

            ImGui.SameLine();

            ImGui.BeginGroup();
            ImGui.Text(Language.replace_by);
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputTextWithHint("##chatLyricsPostSongReplacement", "", ref Context.Plugin.Config.PostSong.Replacement, 1000))
                Context.Plugin.IpcProvider.SyncAllSettings();
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
    }

    private void DrawPostSongTemplateMode()
    {
        ImGui.Text("Template");
        ImGui.SetNextItemWidth(520 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##chatLyricsPostSongTemplate", "{SongName}", ref Context.Plugin.Config.PostSong.Template, 1000))
            Context.Plugin.IpcProvider.SyncAllSettings();
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
        ImGui.Text(Language.song_name_regex_and_output_format);
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text(Language.capture_regex);
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##chatLyricsPostSongCaptureRegex", "", ref Context.Plugin.Config.PostSong.CaptureRegex, 1000))
            Context.Plugin.IpcProvider.SyncAllSettings();
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
        ImGui.Text(Language.output_format);
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##chatLyricsPostSongOutputFormat", "♪ Artist: $1 - Song: $2 ♪", ref Context.Plugin.Config.PostSong.OutputFormat, 1000))
            Context.Plugin.IpcProvider.SyncAllSettings();
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

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Asterisk, "##chatLyricsCopyRegexExample", "Copy regex example for pattern: Arist - Song Name"))
        {
            ImGui.SetClipboardText("^(.*?) - (.*?)");
            ImGuiUtil.AddNotification(NotificationType.Info, "Copied to clipboard");
        }

        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Comment, "##chatLyricsCopyAIPromptExample", "Copy AI prompt example"))
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

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Bookmark, "##chatLyricsRegexTestWebsite", "Open regex test website"))
        {
            WindowsApi.OpenUrl("https://regex101.com/");
        }
    }

    private void DrawLyricsOptions()
    {
        if (ImGui.CollapsingHeader(Language.lyrics, ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(Language.setting_tooltip_play_lyrics, ref Context.Plugin.Config.playLyrics))
                Context.Plugin.IpcProvider.SyncAllSettings();
            ImGuiUtil.HelpMarker(Language.display_lyrics_tooltip);

            ImGui.SameLine();
            if (ImGui.Button(Language.button_export_lrc_template))
            {
                Lyrics.ExportLrcTemplate(Context.Plugin.Config.defaultPerformerFolder + $@"\LyricsTemplateExample.lrc");
                WindowsApi.OpenFolder(Context.Plugin.Config.defaultPerformerFolder);
                ImGuiUtil.AddNotification(NotificationType.Success, "Lrc template exported");
            }

            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.Text(Language.select_chat_to_send_lyrics);
            if (OutputChannelCombo.Draw("##chatLyricsLyricsChatTarget", ref Context.Plugin.Config.LyricsChatTarget))
                Context.Plugin.IpcProvider.SyncAllSettings();

            ImGui.Unindent();
        }
    }
}
