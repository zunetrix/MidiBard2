using System;
using System.Linq;
using System.Numerics;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Ipc;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

using MidiBard2.Resources;

namespace MidiBard;

public partial class PluginUI
{
    private void DrawEnsembleSettings()
    {
        ImGuiGroupPanel.BeginGroupPanel(Language.setting_group_label_ensemble_settings);
        if (ImGui.Checkbox(Language.setting_label_sync_clients, ref MidiBard.config.SyncClients))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_sync_clients);

        ImGui.SameLine(ImGuiUtil.GetWindowContentRegionWidth() - ImGui.GetFrameHeightWithSpacing() - ImGuiUtil.GetIconButtonSize(FontAwesomeIcon.ExchangeAlt).X);
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExchangeAlt, "syncbtn", Language.icon_button_tooltip_sync_settings))
        {
            IPCHandles.SyncAllSettings();
            IPCHandles.SyncPlaylist();
            ImGuiUtil.AddNotification(NotificationType.Info, "Synced settings and playlist");
        }

        //-------------------

        if (ImGui.Checkbox(Language.setting_label_monitor_ensemble, ref MidiBard.config.MonitorOnEnsemble))
        {
            IPCHandles.SyncAllSettings();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_monitor_ensemble);

        //-------------------

        bool pmdWasOn = MidiBard.config.playOnMultipleDevices;
        if (ImGui.Checkbox("Play on Multiple Devices", ref MidiBard.config.playOnMultipleDevices))
        {
            if (pmdWasOn || MidiBard.config.playOnMultipleDevices)
            {
                PartyChatCommand.SendPlayOnMultipleDevices(MidiBard.config.playOnMultipleDevices);
            }
        }
        ImGuiUtil.ToolTip("Choose this if your bards are spread between different devices.");

        bool chatPlaylistSyncWasOn = MidiBard.config.useChatPlaylistSync;
        if (MidiBard.config.playOnMultipleDevices)
        {
            ImGui.Spacing();
            ImGui.Indent();
            ImGui.Indent();
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
            ImGui.Unindent();
            ImGui.Unindent();
            ImGui.Spacing();
        }

        //-------------------

        var itemWidth = -ImGui.GetCursorPosX() + ImGui.GetWindowContentRegionMin().X;
        ImGui.Checkbox(Language.ensemble_config_draw_ensemble_progress_indicator_on_visualizer, ref MidiBard.config.UseEnsembleIndicator);

        //-------------------

        string[] values = new string[] { "None", "Manual", "Default" };
        var currentCompensationMode = (int)MidiBard.config.CompensationMode;
        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ensemble Compensation Mode");
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

        ImGuiUtil.Spacing(3);

        DrawEnsembleMembersSettings();
    }

    private void DrawLyricsOptions()
    {
        if (ImGui.CollapsingHeader("Lyrics", ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Spacing();
            ImGui.Indent();

            if (ImGui.Checkbox(Language.setting_tooltip_play_lyrics, ref MidiBard.config.playLyrics))
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
    }

    private void DrawPostSongOptions()
    {
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

    private void DrawEnsembleMembersSettings()
    {
        if (ImGui.CollapsingHeader("Ensemble party members config", ImGuiTreeNodeFlags.NoAutoOpenOnLog))
        {
            ImGui.Indent();

            var partyMembers = api.PartyList.Select((partyMember) => partyMember.GetPartyMemberData()).ToList();
            ImGui.TextUnformatted("Display order");

            if (ImGui.BeginTable("##EnsembleMemberTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed);

                for (int i = 0; i < MidiBard.config.EnsembleMemberConfigs.Count; i++)
                {
                    ImGui.PushID(i);
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted($"{i + 1:00}");

                    ImGui.TableNextColumn();
                    ImGui.Selectable($"{MidiBard.config.EnsembleMemberConfigs[i].Name}");

                    if (ImGui.BeginDragDropSource())
                    {
                        unsafe
                        {
                            ImGui.SetDragDropPayload("DND_ENSEMBLE_MEMBER", new IntPtr(&i), sizeof(int));
                            ImGui.Button($"({i + 1}) {MidiBard.config.EnsembleMemberConfigs[i].Name}");
                        }

                        // PluginLog.Warning($"Drag start [{i}]: {MidiBard.config.EnsembleMemberConfigs[i].Name}");
                        ImGui.EndDragDropSource();
                    }

                    ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget);
                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_ENSEMBLE_MEMBER");

                        bool isDropping = false;
                        unsafe
                        {
                            isDropping = dragDropPayload.NativePtr != null;
                        }

                        if (isDropping)
                        {
                            unsafe
                            {
                                int originalIndex = *(int*)dragDropPayload.Data;

                                int offset = i - originalIndex;
                                if (offset != 0 && originalIndex + offset >= 0)
                                {
                                    int targetIndex = originalIndex + offset;
                                    // PluginLog.Warning($"Drag end [{i}]: [{originalIndex}, {targetIndex}] {offset}");
                                    MidiBard.config.MoveEnsembleMemberConfigToIndex(originalIndex, targetIndex);
                                }
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.PopStyleColor();

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"↑##MoveUpEnsembleMemberConfig_{i}"))
                        MidiBard.config.ChangeEnsembleMemberConfigOrder(MidiBard.config.EnsembleMemberConfigs[i].Cid, -1);

                    ImGui.SameLine();
                    if (ImGui.Button($"↓##MoveDownEnsembleMemberConfig_{i}"))
                        MidiBard.config.ChangeEnsembleMemberConfigOrder(MidiBard.config.EnsembleMemberConfigs[i].Cid, 1);

                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $" X ##RemoveEnsembleMemberConfig_{i}", "Delete"))
                        MidiBard.config.RemoveEnsembleMemberConfig(MidiBard.config.EnsembleMemberConfigs[i].Cid);

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            bool allPartyMembersInConfig = partyMembers.All(partyMember =>
                MidiBard.config.EnsembleMemberConfigs?.Any(config => config.Cid == partyMember.Cid) ?? false);

            ImGui.BeginDisabled(allPartyMembersInConfig);
            ImGui.TextUnformatted("Available party members");
            if (ImGui.BeginCombo("##partyMemberSelectList", "Select"))
            {
                foreach (var partyMember in partyMembers)
                {
                    var isCidInConfigList = MidiBard.config.EnsembleMemberConfigs?.Any(p => p.Cid == partyMember.Cid) ?? false;
                    if (!isCidInConfigList)
                    {
                        var playerInfo = $"{partyMember.Name}@{partyMember.World}";
                        if (ImGui.Selectable($"{playerInfo}##{partyMember.Cid}", false))
                        {
                            MidiBard.config.AddEnsembleMemberConfig(new EnsembleMemberConfig { Cid = partyMember.Cid, Name = playerInfo, TrackAssignmentRegex = "" });
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
}

