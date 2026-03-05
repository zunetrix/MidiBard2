using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;

using MidiBard.Resources;
using MidiBard.Util;
using MidiBard.Extensions.Time;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Util.Lyrics;
using MidiBard.Playlist;
using Dalamud.Interface.Utility;

namespace MidiBard;

public partial class MainWindow
{
    private int songTargetIndexInputValue = 1;

    private void DrawPlaylist()
    {
        if (Plugin.Config.UseStandalonePlaylistWindow)
        {
            ImGui.SetNextWindowSize(new(ImGui.GetWindowSize().Y), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(ImGui.GetWindowPos() - new Vector2(2, 0), ImGuiCond.FirstUseEver, new Vector2(1, 0));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Style.Components.WindowBg);
            ImGui.PushStyleColor(ImGuiCol.TitleBg, Style.Components.WindowBg);
            if (ImGui.Begin(
                    Language.window_title_standalone_playlist +
                    $" ({Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0})" +
                    (Plugin.PlaylistManager.CurrentPlaylist.Duration > TimeSpan.Zero ? $" Duration: {Plugin.PlaylistManager.CurrentPlaylist.Duration.GetDurationString()}" : "") +
                    $"###MidibardPlaylist",
                    ref Plugin.Config.UseStandalonePlaylistWindow, ImGuiWindowFlags.NoDocking))
            {
                DrawContent();
            }

            ImGui.PopStyleColor(2);
            ImGui.End();
        }
        else
        {
            if (!Plugin.Config.miniPlayer)
            {
                DrawContent();
                ImGui.Spacing();
            }
        }

        void DrawContent()
        {
            DrawPlaylistMenu();

            if (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count > 0)
            {
                if (ImGui.Button(Language.text_playlist_is_empty, new Vector2(-1, ImGui.GetFrameHeight())))
                {
                    RunImportFileTask();
                }
            }
            else
            {
                DrawPlaylistTable();
            }
        }
    }

    private void DrawPlaylistTable()
    {
        bool beginChild;
        if (Plugin.Config.UseStandalonePlaylistWindow)
        {
            beginChild = ImGui.BeginChild("playlistchild");
        }
        else
        {
            var minSongsToDisplay = 15;
            var maxSogsToDisplay = Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0; // +2 for table header row
            beginChild = ImGui.BeginChild("playlistchild",
                new Vector2(x: -1, y: ImGui.GetTextLineHeightWithSpacing() * Math.Min(minSongsToDisplay, maxSogsToDisplay)));
        }

        if (beginChild)
        {
            var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV;

            if (ImGui.BeginTable("##PlaylistTable", 4, tableFlags, ImGui.GetWindowSize()))
            {
                ImGui.TableSetupColumn("##songNumberColumn", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##deleteColumn", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort);
                ImGui.TableSetupColumn("##durationColumn", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("##fileNameColumn", ImGuiTableColumnFlags.WidthStretch);
                // ImGui.TableHeadersRow();
                // ↑ ↓

                // table header sort
                // ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                // ImGui.TableNextColumn();
                // ImGui.TableNextColumn();

                // ImGui.TableNextColumn();
                // var songDurationSortIcon = songDurationSortDirectionDesc ? FontAwesomeIcon.SortAmountUpAlt : FontAwesomeIcon.SortAmountDownAlt;
                // if (ImGuiUtil.IconButton(songDurationSortIcon, "##playlistTableDurationSort"))
                // {
                //     Plugin.PlaylistManager.SortBy((song) => song.SongLength, descending: !songDurationSortDirectionDesc);
                //     songDurationSortDirectionDesc = !songDurationSortDirectionDesc;
                //     RefreshPlaylistSearchResult();
                // }
                // ImGuiUtil.ToolTip("Sort");

                // ImGui.TableNextColumn();
                // var songNameSortIcon = songNameSortDirectionDesc ? FontAwesomeIcon.SortAmountUpAlt : FontAwesomeIcon.SortAmountDownAlt;
                // if (ImGuiUtil.IconButton(songNameSortIcon, "##playlistTableFileNameSort"))
                // {
                //     Plugin.PlaylistManager.SortBy((song) => song.FileName, descending: !songNameSortDirectionDesc);
                //     songNameSortDirectionDesc = !songNameSortDirectionDesc;
                //     RefreshPlaylistSearchResult();
                // }
                // ImGuiUtil.ToolTip("Sort");

                var isFiltered = Plugin.Config.enableSearching &&
                  (!string.IsNullOrEmpty(PlaylistSearchString) ||
                  Plugin.Config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);

                var itemCount = isFiltered ? searchedPlaylistIndexs.Count : Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0;

                bool lockMultipleDevicesOptions = Plugin.Config.playOnMultipleDevices
                                            && Plugin.Config.useChatPlaylistSync
                                            && DalamudApi.PartyList.IsInParty()
                                            && !DalamudApi.PartyList.IsPartyLeader();

                ImGuiListClipperPtr clipper;
                unsafe
                {
                    clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper());
                }

                clipper.Begin(itemCount);

                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        if (i >= itemCount) break;

                        int realIndex = isFiltered ? searchedPlaylistIndexs[i] : i;

                        if (realIndex >= (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0)) continue;

                        DrawPlayListEntry(realIndex, lockMultipleDevicesOptions);
                    }
                }

                if (playlistScrollToCurrentSong)
                {
                    playlistScrollToCurrentSong = false;
                    int targetIndex = isFiltered
                        ? searchedPlaylistIndexs.FindIndex(i1 => i1 == Plugin.PlaylistManager.CurrentSongIndex)
                        : Plugin.PlaylistManager.CurrentSongIndex;

                    if (targetIndex >= 0 && targetIndex < itemCount)
                    {
                        var scrollY = targetIndex * clipper.ItemsHeight;
                        ImGui.SetScrollY(scrollY);
                    }
                }

                clipper.End();
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawPlayListEntry(int i, bool lockMultipleDevicesOptions)
    {
        var isInvalidSongIndex = i < 0 || i >= (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0);
        if (isInvalidSongIndex) return;

        var entry = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i];

        ImGui.PushID(i);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawPlaylistItemSelectable(i);

        ImGui.TableNextColumn();
        ImGui.BeginDisabled(lockMultipleDevicesOptions);
        DrawPlaylistDeleteButton();
        ImGui.EndDisabled();

        ImGui.TableNextColumn();
        DrawPlaylistTrackDuration();

        ImGui.TableNextColumn();
        DrawPlaylistTrackName();

        ImGui.PopID();

        void DrawPlaylistItemSelectable(int i)
        {
            if (ImGui.Selectable($"{i + 1:000}##playlistItem", Plugin.PlaylistManager.CurrentSongIndex == i,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.AllowItemOverlap))
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    if (!Plugin.AgentMetronome.EnsembleModeRunning)
                    {
                        if (Plugin.Config.playOnMultipleDevices && DalamudApi.PartyList.Length > 1)
                        {
                            Plugin.ChatWatcher.SendSwitchTo(i);
                        }
                        else
                        {
                            Plugin.MidiPlayerControl.StopLrc();
                            Plugin.PlaylistManager.LoadPlayback(i);
                        }
                    }
                }
            }

            DrawPlaylistContextMenu(i);

            // Drag & Drop
            ImGui.BeginDisabled(lockMultipleDevicesOptions);
            if (ImGui.BeginDragDropSource())
            {
                unsafe
                {
                    ImGui.SetDragDropPayload("DND_PLAYLIST_ITEM", new ReadOnlySpan<byte>(&i, sizeof(int)), ImGuiCond.None);
                    ImGui.PushStyleColor(ImGuiCol.Button, Style.Components.ButtonInfoActive);
                    ImGui.Button($"({i + 1}) {Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i].GetFileName()}");
                    ImGui.PopStyleColor();
                }
                // DalamudApi.PluginLog.Debug($"Drag start [{i}]: {Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i].GetFileName()}");
                ImGui.EndDragDropSource();
            }

            ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget);
            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_PLAYLIST_ITEM");

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
                            Plugin.PlaylistManager.MoveSongToIndexSync(originalIndex, targetIndex);
                        }
                    }
                }
                ImGui.EndDragDropTarget();
            }
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
        }

        void DrawPlaylistContextMenu(int i)
        {
            // if (IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            // if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            // {
            //     ImGui.OpenPopup("##playlistRightClickMenu");
            // }
            ImGui.OpenPopupOnItemClick($"##playlistRightClickMenu", ImGuiPopupFlags.MouseButtonRight);
            ImGui.PushStyleColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1);
            if (ImGui.BeginPopup($"##playlistRightClickMenu"))
            {
                var song = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i];
                var isFilePlayed = song.IsPlayed;

                // menu title
                ImGui.PushStyleColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal);
                float fullWidth = ImGui.GetContentRegionAvail().X;
                ImGui.Button($"({i + 1}) {Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i].GetFileName()}", new Vector2(fullWidth, 0));
                ImGui.PopStyleColor(3);

                // close btn
                // ImGui.SameLine();
                // ImGui.Dummy(Vector2.Zero);
                // ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
                // ImGui.SetItemAllowOverlap();
                // ImGui.PushStyleColor(ImGuiCol.Button, Style.Colors.Red);
                // ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Style.Colors.Red);
                // ImGui.PushStyleColor(ImGuiCol.ButtonActive, Style.Colors.Red);
                // if (ImGui.Button(" X "))
                // {
                //     ImGui.CloseCurrentPopup();
                // }
                // ImGui.PopStyleColor(3);
                // ImGuiUtil.ToolTip(Language.menu_label_close);

                ImGui.Separator();

                //-------------------

                // if (ImGui.Selectable(Language.menu_label_toggle_song_played_status))
                // {
                //     Plugin.PlaylistManager.ChangeSongPlayedStatusSync(i, !isFilePlayed);
                //     ImGui.CloseCurrentPopup();
                // }

                if (ImGui.MenuItem(Language.menu_label_toggle_song_played_status))
                {
                    Plugin.PlaylistManager.ChangeSongPlayedStatusSync(i, !isFilePlayed);
                }

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem(Language.menu_label_send_song_name_to_chat))
                {
                    Plugin.PlaylistManager.SendSongToChat(i);
                }

                if (ImGui.MenuItem(Language.menu_label_copy_song_name))
                {
                    var songName = Plugin.PlaylistManager.GetPostSongName(i);
                    ImGui.SetClipboardText($"{songName}");
                    ImGuiUtil.AddNotification(NotificationType.Info, Language.text_song_name_copied_to_clipboard);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                //-------------------

                if (ImGui.MenuItem("Recalculate song duration"))
                {
                    Plugin.PlaylistManager.CalculateSongDuration(i);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                //-------------------

                ImGui.BeginDisabled(lockMultipleDevicesOptions);
                if (ImGui.MenuItem(Language.menu_label_move_song_to_top))
                {
                    Plugin.PlaylistManager.MoveSongToIndexSync(i, 0);
                }

                if (ImGui.MenuItem(Language.menu_label_move_song_to_bottom))
                {
                    Plugin.PlaylistManager.MoveSongToIndexSync(i, Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0 - 1);
                }

                ImGui.Spacing();

                ImGui.Text(Language.menu_label_move_song_to_position);
                ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt("##btnMoveSongToIndex", ref songTargetIndexInputValue, 1, 10, default, ImGuiInputTextFlags.AutoSelectAll))
                {
                    if (songTargetIndexInputValue <= 0)
                        songTargetIndexInputValue = 1;
                }

                // var btnChangeSize = ImGuiHelpers.GetButtonSize(btnChangeText);
                // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnChangeSize.X);
                ImGui.SameLine();
                if (ImGui.Button("Move"))
                {
                    Plugin.PlaylistManager.MoveSongToIndexSync(i, songTargetIndexInputValue - 1);
                    // TODO: scroll to index after move
                }
                ImGui.EndDisabled();

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem("Edit lyric"))
                {
                    if (i >= 0 && i < (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0))
                    {
                        var entry = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i];
                        // TODO: add LyricsEditorWindow
                        Plugin.Ui.LyricsEditorWindow.LoadLrcToEditor(new Lyrics(entry.GetFilePath()));
                        Plugin.Ui.LyricsEditorWindow.IsOpen = true;
                    }
                }

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem(Language.menu_item_open_in_file_explorer))
                {
                    if (i >= 0 && i < (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0))
                    {
                        var entry = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i];
                        WindowsApi.OpenFileLocation(entry.GetFilePath());
                    }
                }

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.BeginDisabled(lockMultipleDevicesOptions);
                if (ImGui.MenuItem(Language.menu_label_remove_song_from_playlist))
                {
                    Plugin.PlaylistManager.RemoveSync(i);
                }
                ImGui.EndDisabled();

                //-------------------

                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        void DrawPlaylistDeleteButton()
        {
            // fix button background for light color themes
            ImGui.PushStyleColor(ImGuiCol.Button, Style.Colors.Transparent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Style.Colors.Transparent);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Style.Colors.Transparent);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##btnDeletePlaylistSong##{i}"))
            {
                Plugin.PlaylistManager.RemoveSync(i);
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);

            // PushFont(UiBuilder.IconFont);
            // PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5f, 5f));
            // if (Button($"{FontAwesomeIcon.TrashAlt.ToIconString()}##{i}",
            //         new Vector2(GetTextLineHeight(), GetTextLineHeight())))
            // {
            //     Plugin.PlaylistManager.RemoveSync(i);
            // }
            // PopStyleVar();
            // PopFont();
        }

        void DrawPlaylistTrackDuration()
        {
            ImGui.Text($"{entry.GetSongLengthFormated()}");
        }

        void DrawPlaylistTrackName()
        {
            var displayName = entry.GetFileName();
            // ImGui.TextColored(textColor, displayName);
            if (entry.IsPlayed)
                ImGui.PushStyleColor(ImGuiCol.Text, Plugin.Config.playedSongColor);

            ImGui.Text(displayName);

            if (entry.IsPlayed)
                ImGui.PopStyleColor();

            var songTooltipText = $"{displayName}\n\n{entry.GetFileDirectory()}";
            ImGuiUtil.ToolTip(songTooltipText + "\n\n(Drag to reorder - right click for more options)");
        }
    }

    // private void DrawPlaylistSelector()
    // {
    //     ImGui.SetNextWindowPos(ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth(), 0), ImGuiCond.Always);
    //     ImGui.SetNextWindowSize(new Vector2(ImGuiHelpers.GlobalScale * 150, GetWindowHeight()));
    //     if (ImGui.Begin("playlists",
    //            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove |
    //            ImGuiWindowFlags.NoFocusOnAppearing))
    //     {
    //         try
    //         {
    //             bool sync = false;
    //             var container = PlaylistContainerManager.Container;
    //             var playlistEntries = container.Entries;
    //             if (ImGui.BeginListBox("##playlistListbox", new Vector2(-1, ImGuiUtil.GetWindowContentRegionHeight() - 2 * GetFrameHeightWithSpacing())))
    //             {
    //                 for (int i = 0; i < playlistEntries.Count; i++)
    //                 {
    //                     var playlist = playlistEntries[i];
    //                     if (ImGui.Selectable($"{playlist.Name} ({playlist.PathList.Count})##{i}",
    //                             PlaylistContainerManager.CurrentPlaylistIndex == i))
    //                     {
    //                         PlaylistContainerManager.CurrentPlaylistIndex = i;
    //                     }
    //                 }

    //                 ImGui.EndListBox();
    //             }
    //             ImGui.SetNextItemWidth(-1);
    //             if (ImGui.InputText($"##currentPlaylistName", ref container.CurrentPlaylist.Name, 128, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
    //             {
    //                 sync = true;
    //             }

    //             if (ImGuiUtil.IconButton(FontAwesomeIcon.File, "new", Language.icon_button_tooltip_new_playlist))
    //             {
    //                 playlistEntries.Add(new PlaylistEntry() { Name = Language.icon_button_tooltip_new_playlist });
    //                 sync = true;
    //             }

    //             ImGui.SameLine();
    //             if (ImGui.IconButton(FontAwesomeIcon.Copy, "clone", Language.icon_button_tooltip_clone_current_playlist))
    //             {
    //                 playlistEntries.Insert(container.CurrentListIndex, container.CurrentPlaylist.Clone());
    //                 sync = true;
    //             }
    //             ImGui.SameLine();
    //             if (ImGui.IconButton(FontAwesomeIcon.Download, "saveas", Language.icon_button_tooltip_save_search_as_playlist))
    //             {
    //                 try
    //                 {
    //                     var c = new PlaylistEntry();
    //                     c.Name = PlaylistSearchString;
    //                     RefreshPlaylistSearchResult();
    //                     c.PathList = MidiBard.Ui.searchedPlaylistIndexs.Select(i => Plugin.PlaylistManager.CurrentPlaylist?.Songs?[i]).ToList();
    //                     playlistEntries.Add(c);
    //                     sync = true;
    //                 }
    //                 catch (Exception e)
    //                 {
    //                     DalamudApi.PluginLog.Warning(e, "error when try saving current search result as new playlist");
    //                 }
    //             }
    //             ImGui.SameLine();
    //             if (ImGuiUtil.IconButton(FontAwesomeIcon.Save, "save", Language.icon_button_tooltip_save_and_sync_playlist))
    //             {
    //                 container.Save();
    //                 sync = true;
    //             }

    //             ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.GetFrameHeightWithSpacing());
    //             if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "deleteCurrentPlist", Language.icon_button_tooltip_delete_current_playlist))
    //             {
    //             }
    //             if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
    //             {
    //                 playlistEntries.Remove(container.CurrentPlaylist);
    //                 sync = true;
    //             }

    //             if (sync)
    //             {
    //                 IPCHandles.SyncPlaylist();
    //             }
    //         }
    //         catch (Exception e)
    //         {
    //             DalamudApi.PluginLog.Error(e, "error when draw playlist popup");
    //         }

    //         ImGui.End();
    //     }
    // }
}
