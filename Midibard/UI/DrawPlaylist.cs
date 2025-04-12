// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using ImGuiNET;

using MidiBard.Control.MidiControl;
using MidiBard.IPC;
using MidiBard.Managers.Ipc;
using MidiBard.UI.Win32;
using MidiBard.Util;

using MidiBard2.Resources;

using static Dalamud.api;
using static ImGuiNET.ImGui;
using static MidiBard.ImGuiUtil;

namespace MidiBard;

public partial class PluginUI
{
    private Regex PlaylistSearchRegex = null;
    private string PlaylistSearchString = "";
    private readonly List<int> searchedPlaylistIndexs = new();
    private bool RegexError;
    private string RegexErrorMessage = "";
    private int songTargetIndexInputValue = 1;

    private void DrawPlaylist()
    {
        if (MidiBard.config.UseStandalonePlaylistWindow)
        {

            SetNextWindowSize(new(GetWindowSize().Y), ImGuiCond.FirstUseEver);
            SetNextWindowPos(GetWindowPos() - new Vector2(2, 0), ImGuiCond.FirstUseEver, new Vector2(1, 0));
            PushStyleColor(ImGuiCol.TitleBgActive, Theme.Current.WindowBackground);
            PushStyleColor(ImGuiCol.TitleBg, Theme.Current.WindowBackground);
            if (Begin(
                    Language.window_title_standalone_playlist +
                    $" ({PlaylistManager.FilePathList.Count})" +
                    (PlaylistManager.CurrentContainer.TotalDuration > TimeSpan.Zero ? $" Duration: {GetDurationString(PlaylistManager.CurrentContainer.TotalDuration)}" : "") +
                    $"###MidibardPlaylist",
                    ref MidiBard.config.UseStandalonePlaylistWindow, ImGuiWindowFlags.NoDocking))
            {
                DrawContent();
            }

            PopStyleColor(2);
            End();
        }
        else
        {
            if (!MidiBard.config.miniPlayer)
            {
                DrawContent();
                Spacing();
            }
        }

        void DrawContent()
        {
            if (IsImportRunning)
            {
                ImGuiUtil.DrawColoredBanner(Theme.Colors.Violet, Language.text_Import_in_progress);
            }

            PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4));
            ImGuiUtil.PushIconButtonSize(ImGuiHelpers.ScaledVector2(45.5f, 25));

            ImGui.BeginDisabled(IsImportRunning);
            if (ImGui.BeginPopup("OpenFileDialog_selection"))
            {
                if (ImGui.MenuItem(Language.w32_file_dialog, null, MidiBard.config.useLegacyFileDialog))
                {
                    MidiBard.config.useLegacyFileDialog = true;
                }
                else if (ImGui.MenuItem(Language.imgui_file_dialog, null, !MidiBard.config.useLegacyFileDialog))
                {
                    MidiBard.config.useLegacyFileDialog = false;
                }

                ImGui.EndPopup();
            }

            ImGui.BeginGroup();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "buttonimport",
                    Language.icon_button_tooltip_import_file))
            {
                RunImportFileTask();
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "buttonimportFolder",
                    Language.icon_button_tooltip_import_folder))
            {
                RunImportFolderTask();
            }

            ImGui.EndGroup();

            ImGui.OpenPopupOnItemClick("OpenFileDialog_selection", ImGuiPopupFlags.MouseButtonRight);
            ImGui.EndDisabled();


            SameLine();
            var color = MidiBard.config.enableSearching ? MidiBard.config.themeColor : Theme.Colors.White;
            if (IconButton(FontAwesomeIcon.Search, "searchbutton", Language.icon_button_tooltip_search_playlist, color))
            {
                MidiBard.config.enableSearching ^= true;
            }

            SameLine();

            IconButton(FontAwesomeIcon.TrashAlt, "clearplaylist", Language.icon_button_tooltip_clearplaylist_tootltip);
            if (IsItemHovered())
            {
                if (IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    PlaylistManager.Clear();
                }
            }

            SameLine();
            var fontAwesomeIcon = MidiBard.config.UseStandalonePlaylistWindow
                ? FontAwesomeIcon.Compress
                : FontAwesomeIcon.Expand;
            if (ImGuiUtil.IconButton(fontAwesomeIcon, "ButtonStandalonePlaylist",
                    Language.setting_label_standalone_playlist_window))
            {
                MidiBard.config.UseStandalonePlaylistWindow ^= true;
            }

            ImGui.SameLine();
            if (IconButton(FontAwesomeIcon.Eraser, "btnClearHighlightedSongs"))
            {
                PlaylistManager.ResetAllSongsPlayedStatusSync();
                // reset filter
                MidiBard.config.SearchFilterPlayedOption = Configuration.FilterPlayedSongOptions.ShowAll;
            }
            ToolTip(Language.icon_button_tooltip_clear_highlighted_songs);

            SameLine();
            if (IconButton(FontAwesomeIcon.EllipsisH, "more", Language.icon_button_tooltip_playlist_menu))
            {
                ImGui.OpenPopup("PlaylistMenu");
            }

            PopStyleVar();
            PopIconButtonSize();

            if (BeginPopup("PlaylistMenu"))
            {
                var shortenPath = Path.ChangeExtension(PlaylistManager.CurrentContainer.FilePathWhenLoading, null).EllipsisString(40);
                MenuItem(shortenPath, false);

                var useWin32 = MidiBard.config.useLegacyFileDialog;
                //open playlist
                if (MenuItem(Language.menu_label_open_playlist))
                {
                    if (useWin32)
                    {
                        FileDialogs.OpenPlaylistDialog((result, path) =>
                        {
                            if (result != true) return;
                            PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(path);
                        });
                    }
                    else
                    {
                        fileDialogManager.OpenFileDialog("Open playlist", ".mpl", (b, s) =>
                        {
                            if (!b) return;
                            PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(s);
                        });
                    }
                }

                //new playlist
                if (MenuItem(Language.menu_label_new_playlist))
                {
                    if (PlaylistManager.CurrentContainer.FilePathWhenLoading != null)
                    {
                        PlaylistManager.CurrentContainer.Save(PlaylistManager.CurrentContainer.FilePathWhenLoading);
                    }

                    if (useWin32)
                    {
                        FileDialogs.SavePlaylistDialog((result, path) =>
                        {
                            if (result != true) return;
                            PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(path, true);
                        }, Language.text_new_playlist);
                    }
                    else
                    {
                        fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                            ".mpl",
                            Language.text_new_playlist, ".mpl", (b, s) =>
                            {
                                if (b)
                                {
                                    PlaylistManager.CurrentContainer = PlaylistContainer.FromFile(s, true);
                                }
                            });
                    }
                }

                //sync playlist
                if (MenuItem(Language.menu_label_sync_playlist))
                {
                    IPCHandles.SyncAllSettings();
                    if (MidiBard.config.playOnMultipleDevices && MidiBard.config.usingFileSharingServices)
                    {
                        PartyChatCommand.SendReloadPlaylist();
                    }
                    else
                    {
                        IPCHandles.SyncPlaylist();
                    }
                }

                //save playlist
                if (MenuItem(Language.menu_label_save_playlist))
                {
                    PlaylistManager.CurrentContainer.Save();
                }

                //save playlist as...
                if (MenuItem(Language.menu_label_clone_current_playlist))
                {
                    if (useWin32)
                    {
                        FileDialogs.SavePlaylistDialog((result, path) =>
                        {
                            if (result != true) return;
                            PlaylistManager.CurrentContainer.Save(path);
                        }, PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy);
                    }
                    else
                    {
                        fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                            ".mpl",
                            PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                            ".mpl", (b, s) =>
                            {
                                if (!b) return;
                                PlaylistManager.CurrentContainer.Save(s);
                            });
                    }
                }

                //save playlist search result as...
                var isPlaylistFiltered = MidiBard.config.enableSearching && (!string.IsNullOrEmpty(PlaylistSearchString)
                    || MidiBard.config.SearchFilterPlayedOption != Configuration.FilterPlayedSongOptions.ShowAll);
                if (MenuItem(Language.menu_label_save_search_as_playlist, isPlaylistFiltered))
                {
                    var playlistSearchString = PlaylistSearchString;
                    if (useWin32)
                    {
                        FileDialogs.SavePlaylistDialog((result, path) =>
                        {
                            if (result != true) return;
                            SaveSearchedPlaylist(path);
                        }, playlistSearchString);
                    }
                    else
                    {
                        fileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                            "*.mpl", playlistSearchString,
                            ".mpl",
                            (b, s) =>
                            {
                                if (!b) return;
                                SaveSearchedPlaylist(s);
                            });
                    }

                    void SaveSearchedPlaylist(string filePath1)
                    {
                        try
                        {
                            RefreshSearchResult();
                            var playlistContainer = PlaylistContainer.FromFile(filePath1, true);
                            playlistContainer.SongPaths = MidiBard.Ui.searchedPlaylistIndexs
                                .Select(i => PlaylistManager.FilePathList[i]).ToList();
                            playlistContainer.Save();
                        }
                        catch (Exception e)
                        {
                            PluginLog.Warning(e, "error when saving current search result");
                        }
                    }
                }

                //var totalDuration = PlaylistManager.CurrentContainer.TotalDuration;
                //var durationString = totalDuration == TimeSpan.Zero
                //    ? "Not calculated"
                //    : $"{(int)totalDuration.TotalHours}h {(int)totalDuration.Minutes}m {(int)totalDuration.Seconds}s";
                //MenuItem($"Playlist total duration: {durationString}", false);
                if (MenuItem("Recalculate playlist duration"))
                {
                    PluginLog.Information("Recalculate playlist duration");
                    Task.Run(PlaylistManager.CalculateDurationAll);
                }

                if (MenuItem("Remove duplicate songs by name"))
                {
                    PluginLog.Information("Removing duplicate songs");
                    var distinctBy = PlaylistManager.CurrentContainer.SongPaths.DistinctBy(i => i.FileName).ToList();
                    var currentContainerSongPaths = PlaylistManager.CurrentContainer.SongPaths;
                    AddNotification(NotificationType.Info, $"Removed {currentContainerSongPaths.Count - distinctBy.Count} entries");
                    PlaylistManager.CurrentContainer.SongPaths = distinctBy;
                }

                Separator();

                //recent used playlists
                MenuItem(Language.menu_text_recent_playlist, false);

                var takeLast = MidiBard.config.RecentUsedPlaylists.TakeLast(10).Reverse();
                var id = 89465;
                try
                {
                    foreach (var playlistPath in takeLast)
                    {
                        PushID(id++);
                        try
                        {
                            var ellipsisString = Path.ChangeExtension(playlistPath, null).EllipsisString(40);
                            if (BeginMenu(ellipsisString + "        "))
                            {
                                if (MenuItem(Language.menu_item_load_playlist))
                                {
                                    var playlistContainer = PlaylistContainer.FromFile(playlistPath);
                                    if (playlistContainer != null)
                                    {
                                        PlaylistManager.CurrentContainer = playlistContainer;
                                    }
                                    else
                                    {
                                        AddNotification(NotificationType.Error, $"{playlistPath} NOT exist!");
                                        MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                    }
                                }

                                if (MenuItem(Language.menu_item_open_in_file_explorer))
                                {
                                    try
                                    {
                                        if (!File.Exists(playlistPath))
                                        {
                                            MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                        }

                                        Extensions.OpenFileLocation(playlistPath);
                                    }
                                    catch (Exception e)
                                    {
                                        PluginLog.Warning(e, "error when opening process");
                                    }
                                }

                                if (MenuItem(Language.menu_item_open_in_text_editor))
                                {
                                    try
                                    {
                                        if (!File.Exists(playlistPath))
                                        {
                                            MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                        }

                                        Extensions.ExecuteCmd(playlistPath);
                                    }
                                    catch (Exception e)
                                    {
                                        PluginLog.Warning(e, $"error when opening process {playlistPath}");
                                    }
                                }

                                if (MenuItem(Language.menu_item_remove_from_recent_list))
                                {
                                    MidiBard.config.RecentUsedPlaylists.Remove(playlistPath);
                                }

                                EndMenu();
                            }
                        }
                        catch (Exception e)
                        {
                            PluginLog.Warning(e, "error when drawing recent playlist");
                        }

                        PopID();
                    }
                }
                catch (Exception e)
                {
                    //
                }

                EndPopup();
            }

            if (MidiBard.config.enableSearching)
            {
                TextBoxSearch();
                var isPlaylistFilteredWithoutMatches = searchedPlaylistIndexs.Count == 0
                    && PlaylistManager.FilePathList.Any()
                    && MidiBard.config.enableSearching
                    && (!string.IsNullOrEmpty(PlaylistSearchString) || MidiBard.config.SearchFilterPlayedOption != Configuration.FilterPlayedSongOptions.ShowAll);

                if (isPlaylistFilteredWithoutMatches)
                {
                    DrawColoredBanner(Theme.Colors.Red, Language.text_no_matching_songs_filter);
                }
            }

            if (!PlaylistManager.FilePathList.Any())
            {
                if (Button(Language.text_playlist_is_empty, new Vector2(-1, GetFrameHeight())))
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

    private static void moveSongToPosition(int songIndex, int targetIndex)
    {
        PlaylistManager.MoveSongToIndexSync(songIndex, targetIndex);
        // TODO: scroll to index after move
    }
    private void DrawPlaylistSelector()
    {
        //ImGui.SetNextWindowPos(GetWindowPos() + new Vector2(GetWindowWidth(), 0), ImGuiCond.Always);
        //SetNextWindowSize(new Vector2(ImGuiHelpers.GlobalScale * 150, GetWindowHeight()));
        //if (ImGui.Begin("playlists",
        //        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoMove |
        //        ImGuiWindowFlags.NoFocusOnAppearing))
        //{
        //    try
        //    {
        //        bool sync = false;
        //        var container = PlaylistContainerManager.Container;
        //        var playlistEntries = container.Entries;
        //        if (BeginListBox("##playlistListbox", new Vector2(-1, ImGuiUtil.GetWindowContentRegionHeight() - 2 * GetFrameHeightWithSpacing())))
        //        {
        //            for (int i = 0; i < playlistEntries.Count; i++)
        //            {
        //                var playlist = playlistEntries[i];
        //                if (Selectable($"{playlist.Name} ({playlist.PathList.Count})##{i}",
        //                        PlaylistContainerManager.CurrentPlaylistIndex == i))
        //                {
        //                    PlaylistContainerManager.CurrentPlaylistIndex = i;
        //                }
        //            }

        //            EndListBox();
        //        }
        //        SetNextItemWidth(-1);
        //        if (InputText($"##currentPlaylistName", ref container.CurrentPlaylist.Name, 128, ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue))
        //        {
        //            sync = true;
        //        }

        //        if (IconButton(FontAwesomeIcon.File, "new", Language.icon_button_tooltip_new_playlist))
        //        {
        //            playlistEntries.Add(new PlaylistEntry() { Name = Language.icon_button_tooltip_new_playlist });
        //            sync = true;
        //        }

        //        SameLine();
        //        if (IconButton(FontAwesomeIcon.Copy, "clone", Language.icon_button_tooltip_clone_current_playlist))
        //        {
        //            playlistEntries.Insert(container.CurrentListIndex, container.CurrentPlaylist.Clone());
        //            sync = true;
        //        }
        //        SameLine();
        //        if (IconButton(FontAwesomeIcon.Download, "saveas", Language.icon_button_tooltip_save_search_as_playlist))
        //        {
        //            try
        //            {
        //                var c = new PlaylistEntry();
        //                c.Name = PlaylistSearchString;
        //                RefreshSearchResult();
        //                c.PathList = MidiBard.Ui.searchedPlaylistIndexs.Select(i => PlaylistManager.FilePathList[i]).ToList();
        //                playlistEntries.Add(c);
        //                sync = true;
        //            }
        //            catch (Exception e)
        //            {
        //                PluginLog.Warning(e, "error when try saving current search result as new playlist");
        //            }
        //        }
        //        SameLine();
        //        if (IconButton(FontAwesomeIcon.Save, "save", Language.icon_button_tooltip_save_and_sync_playlist))
        //        {
        //            container.Save();
        //            sync = true;
        //        }

        //        SameLine(GetWindowWidth() - ImGui.GetFrameHeightWithSpacing());
        //        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "deleteCurrentPlist", Language.icon_button_tooltip_delete_current_playlist))
        //        {
        //        }
        //        if (IsItemHovered() && IsMouseDoubleClicked(ImGuiMouseButton.Left))
        //        {
        //            playlistEntries.Remove(container.CurrentPlaylist);
        //            sync = true;
        //        }

        //        if (sync)
        //        {
        //            IPCHandles.SyncPlaylist();
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        PluginLog.Error(e, "error when draw playlist popup");
        //    }

        //    End();
        //}
    }

    private void DrawPlaylistTable()
    {
        PushStyleColor(ImGuiCol.Button, Theme.Colors.Black);
        PushStyleColor(ImGuiCol.ButtonHovered, Theme.Colors.Black);
        PushStyleColor(ImGuiCol.ButtonActive, Theme.Colors.Black);
        PushStyleColor(ImGuiCol.Header, MidiBard.config.themeColorTransparent);

        bool beginChild;
        if (MidiBard.config.UseStandalonePlaylistWindow)
        {
            beginChild = BeginChild("playlistchild");
        }
        else
        {
            beginChild = BeginChild("playlistchild",
                new Vector2(x: -1,
                    y: GetTextLineHeightWithSpacing() * Math.Min(val1: 15, val2: PlaylistManager.FilePathList.Count)));
        }

        if (beginChild)
        {
            if (BeginTable(str_id: "##PlaylistTable", column: 3,
                    flags: ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                           ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV, GetWindowSize()))
            {
                TableSetupColumn("\ue035", ImGuiTableColumnFlags.WidthFixed);
                TableSetupColumn("##deleteColumn", ImGuiTableColumnFlags.WidthFixed);
                TableSetupColumn("filenameColumn", ImGuiTableColumnFlags.WidthStretch);

                ImGuiListClipperPtr clipper;
                unsafe
                {
                    clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                }

                var isPlaylistFiltered = MidiBard.config.enableSearching && (!string.IsNullOrEmpty(PlaylistSearchString)
                || MidiBard.config.SearchFilterPlayedOption != Configuration.FilterPlayedSongOptions.ShowAll);

                if (isPlaylistFiltered)
                {
                    clipper.Begin(searchedPlaylistIndexs.Count);
                    while (clipper.Step())
                    {
                        for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            // prevent invalid removed item index
                            if (i >= searchedPlaylistIndexs.Count) break;
                            DrawPlayListEntry(searchedPlaylistIndexs[i]);
                        }
                    }

                    if (playlistScrollToCurrentSong)
                    {
                        playlistScrollToCurrentSong = false;
                        var findIndex = searchedPlaylistIndexs.FindIndex(i1 => i1 == PlaylistManager.CurrentSongIndex);
                        if (findIndex > -1)
                        {
                            var scrollY = findIndex * clipper.ItemsHeight;
                            ImGui.SetScrollY(scrollY);
                        }
                    }
                    clipper.End();
                }
                else
                {
                    clipper.Begin(PlaylistManager.FilePathList.Count);
                    while (clipper.Step())
                    {
                        for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        {
                            // prevent invalid removed item index
                            if (i >= PlaylistManager.FilePathList.Count) break;
                            DrawPlayListEntry(i);
                            //ImGui.SameLine(800); ImGui.TextUnformatted($"[{i}] {clipper.DisplayStart} {clipper.DisplayEnd} {clipper.ItemsCount}");
                        }
                    }
                    if (playlistScrollToCurrentSong)
                    {
                        playlistScrollToCurrentSong = false;

                        var scrollY = PlaylistManager.CurrentSongIndex * clipper.ItemsHeight;
                        ImGui.SetScrollY(scrollY);
                    }
                    clipper.End();
                }

                EndTable();
            }
        }

        EndChild();
        PopStyleColor(4);
    }

    private void DrawPlayListEntry(int i)
    {
        ImGui.PushID(i);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);

        bool lockMultipleDevicesOptions = MidiBard.config.playOnMultipleDevices && !api.PartyList.IsPartyLeader();

        DrawPlaylistItemSelectable(i);

        ImGui.TableNextColumn();
        ImGui.BeginDisabled(lockMultipleDevicesOptions);
        DrawPlaylistDeleteButton();
        ImGui.EndDisabled();

        ImGui.TableNextColumn();
        DrawPlaylistTrackName();

        ImGui.PopID();

        void DrawPlaylistItemSelectable(int i)
        {
            if (ImGui.Selectable($"{i + 1:000}##plistitem", PlaylistManager.CurrentSongIndex == i,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.AllowItemOverlap))
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    if (!MidiBard.AgentMetronome.EnsembleModeRunning)
                    {
                        if (MidiBard.config.playOnMultipleDevices && api.PartyList.Length > 1)
                        {
                            PartyChatCommand.SendSwitchTo(i + 1);
                        }
                        else
                        {
                            MidiPlayerControl.StopLrc();
                            PlaylistManager.LoadPlayback(i);
                        }
                    }
                }
            }

            // if (IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            // if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            // {
            //     ImGui.OpenPopup("##playlistRightClickMenu");
            // }
            ImGui.OpenPopupOnItemClick($"##playlistRightClickMenu", ImGuiPopupFlags.MouseButtonRight);

            ImGui.PushStyleColor(ImGuiCol.Border, Theme.Current.TooltipBorderColor);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1);
            if (ImGui.BeginPopup($"##playlistRightClickMenu"))
            {

                var song = PlaylistManager.FilePathList[i];
                var isFilePlayed = song.IsFilePlayed;

                // menu title
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Current.Button.Normal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Current.Button.Normal);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Current.Button.Normal);
                float fullWidth = ImGui.GetContentRegionAvail().X;
                ImGui.Button($"({i + 1}) {PlaylistManager.FilePathList[i].FileName}", new Vector2(fullWidth, 0));
                ImGui.PopStyleColor(3);

                // close btn
                // ImGui.SameLine();
                // ImGui.Dummy(Vector2.Zero);
                // ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
                // ImGui.SetItemAllowOverlap();
                // ImGui.PushStyleColor(ImGuiCol.Button, Theme.Colors.Red);
                // ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Colors.Red);
                // ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Colors.Red);
                // if (ImGui.Button(" X "))
                // {
                //     ImGui.CloseCurrentPopup();
                // }
                // ImGui.PopStyleColor(3);
                // ImGuiUtil.ToolTip(Language.menu_label_close);

                ImGui.Separator();

                //-------------------

                var color = isFilePlayed ? Theme.Current.TextPrimary : MidiBard.config.playedSongColor;
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                if (ImGui.Selectable(Language.menu_label_toggle_song_played_status))
                {
                    PlaylistManager.ChangeSongPlayedStatusSync(i, !isFilePlayed);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleColor();

                // if (ImGui.MenuItem(Language.menu_label_toggle_song_played_status))
                // {
                //     PlaylistManager.ChangeSongPlayedStatusSync(i, !isFilePlayed);
                // }

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem(Language.menu_label_send_song_name_to_chat))
                {
                    PlaylistManager.SendSongToChat(i);
                }

                if (ImGui.MenuItem(Language.menu_label_copy_song_name))
                {
                    var songName = PlaylistManager.GetPostSongName(i);
                    ImGui.SetClipboardText($"{songName}");
                    ImGuiUtil.AddNotification(NotificationType.Info, Language.text_song_name_copied_to_clipboard);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                //-------------------

                if (ImGui.MenuItem("Recalculate song duration"))
                {
                    PlaylistManager.CalculateSongDuration(i);
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                //-------------------

                ImGui.BeginDisabled(lockMultipleDevicesOptions);
                if (ImGui.MenuItem(Language.menu_label_move_song_to_top))
                {
                    PlaylistManager.MoveSongToIndexSync(i, 0);
                }

                if (ImGui.MenuItem(Language.menu_label_move_song_to_bottom))
                {
                    PlaylistManager.MoveSongToIndexSync(i, PlaylistManager.FilePathList.Count - 1);
                }

                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Current.Button.Normal);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Current.Button.Hovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Current.Button.Active);
                ImGui.TextUnformatted(Language.menu_label_move_song_to_position);
                ImGui.Spacing();
                ImGui.SetNextItemWidth(150);
                if (ImGui.InputInt("##btnMoveSongToIndex", ref songTargetIndexInputValue, 1, 10, ImGuiInputTextFlags.AutoSelectAll))
                {
                    if (songTargetIndexInputValue <= 0)
                        songTargetIndexInputValue = 1;
                }

                var btnChangeText = "Move";
                // var btnChangeSize = ImGuiHelpers.GetButtonSize(btnChangeText);
                // ImGui.SameLine(ImGui.GetWindowWidth() - 2 * ImGui.GetCursorPosX() - btnChangeSize.X);
                ImGui.SameLine();
                if (ImGui.Button(btnChangeText))
                {
                    moveSongToPosition(i, songTargetIndexInputValue - 1);
                }
                ImGui.PopStyleColor(3);
                ImGui.EndDisabled();

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem("Edit lyric"))
                {
                    if (PlaylistManager.FilePathList.TryGetValue(i, out var entry))
                    {
                        LrcEditor.Instance.LoadLrcToEditor(LrcEditor.GetLrcFromSongEntry(entry));
                        LrcEditor.Instance.Show();
                    }
                }

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem(Language.menu_item_open_in_file_explorer))
                {
                    if (PlaylistManager.FilePathList.TryGetValue(i, out var entry))
                    {
                        Extensions.OpenFileLocation(entry.FilePath);
                    }
                }

                //-------------------

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.BeginDisabled(lockMultipleDevicesOptions);
                if (ImGui.MenuItem(Language.menu_label_remove_song_from_playlist))
                {
                    PlaylistManager.RemoveSync(i);
                }
                ImGui.EndDisabled();

                //-------------------

                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();

            // Drag & Drop
            ImGui.BeginDisabled(lockMultipleDevicesOptions);
            if (ImGui.BeginDragDropSource())
            {
                unsafe
                {
                    ImGui.SetDragDropPayload("DND_PLAYLIST_ITEM", new IntPtr(&i), sizeof(int));
                    ImGui.PushStyleColor(ImGuiCol.Button, Theme.Current.Button.Active);
                    ImGui.Button($"({i + 1}) {PlaylistManager.FilePathList[i].FileName}");
                    ImGui.PopStyleColor();
                }
                // PluginLog.Debug($"Drag start [{i}]: {PlaylistManager.FilePathList[i].FileName}");
                ImGui.EndDragDropSource();
            }

            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_PLAYLIST_ITEM");

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
                            PlaylistManager.MoveSongToIndexSync(originalIndex, targetIndex);
                        }
                    }
                }
                ImGui.EndDragDropTarget();
            }
            ImGui.EndDisabled();
        }

        void DrawPlaylistDeleteButton()
        {
            PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            if (IconButton(FontAwesomeIcon.TrashAlt, $"##deletePlaylistSong##{i}"))
            {
                PlaylistManager.RemoveSync(i);
            }
            PopStyleVar();

            // PushFont(UiBuilder.IconFont);
            //
            // if (Button($"{FontAwesomeIcon.TrashAlt.ToIconString()}##{i}",
            //         new Vector2(GetTextLineHeight(), GetTextLineHeight())))
            // {
            //     PlaylistManager.RemoveSync(i);
            // }
            // PopStyleVar();
            // PopFont();
        }

        void DrawPlaylistTrackName()
        {
            if (i >= 0 && i < PlaylistManager.FilePathList.Count)
            {
                var entry = PlaylistManager.FilePathList[i];
                var displayName = entry.FileName;
                var textColor = entry.IsFilePlayed ? MidiBard.config.playedSongColor : ImGuiColors.DalamudWhite;
                TextColored(textColor, displayName);
                var songTooltipText = entry.SongLength != default
                        ? $"{(int)entry.SongLength.TotalMinutes}:{entry.SongLength.Seconds:00} {displayName}"
                        : displayName;
                ImGuiUtil.ToolTip(songTooltipText + "\n\nDrag to change order");
            }
        }
    }
    private void TextBoxSearch()
    {
        var color = MidiBard.config.SearchUseRegex ? MidiBard.config.themeColor : Theme.Current.TextPrimary;
        if (IconButton(FontAwesomeIcon.StarOfLife, "buttonUseRegex", "Use regex", color))
        {
            MidiBard.config.SearchUseRegex ^= true;
            RefreshSearchResult();
        }

        SameLine();
        // SetNextItemWidth(-1);
        var regexError = MidiBard.config.SearchUseRegex && RegexError;

        if (regexError)
        {
            PushStyleColor(ImGuiCol.FrameBg, Vector4.Lerp(Theme.Current.FrameBackground, Theme.Colors.Red, 0.5f));
        }

        if (InputTextWithHint("##searchplaylist", MidiBard.config.SearchUseRegex ? "Enter regex to search" : Language.hint_search_textbox, ref PlaylistSearchString, 255, ImGuiInputTextFlags.AutoSelectAll))
        {
            RefreshSearchResult();
        }

        var (filterPlayedSongsIcon, filterPlayedSongsIconColor, filterPlayedSongsTooltip) = MidiBard.config.SearchFilterPlayedOption switch
        {
            Configuration.FilterPlayedSongOptions.ShowAll => (FontAwesomeIcon.Music, Theme.Current.TextPrimary, "Show all songs"),
            Configuration.FilterPlayedSongOptions.ShowPlayed => (FontAwesomeIcon.Tasks, MidiBard.config.playedSongColor, "Filter played songs"),
            Configuration.FilterPlayedSongOptions.ShowUnPlayed => (FontAwesomeIcon.ListUl, Theme.Current.TextPrimary, "Filter unplayed songs"),
            _ => (FontAwesomeIcon.Music, ImGuiColors.DalamudWhite, "Show all songs")
        };

        SameLine();
        if (IconButton(filterPlayedSongsIcon, "btnFilterPlayedSongs", filterPlayedSongsTooltip, filterPlayedSongsIconColor))
        {
            MidiBard.config.ToggleSearchFilterPlayedOption();
            RefreshSearchResult();
        }

        if (regexError)
        {
            PopStyleColor();
            if (IsItemFocused())
            {
                SetNextWindowPos(GetItemRectMin() + new Vector2(0, GetFrameHeightWithSpacing()));
                if (Begin("tooltipRegexError", ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize))
                {
                    TextUnformatted(RegexErrorMessage);
                }
                End();
            }
        }
    }
    internal void RefreshSearchResult()
    {
        searchedPlaylistIndexs.Clear();

        try
        {
            PlaylistSearchRegex = new Regex(PlaylistSearchString, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            RegexError = false;
        }
        catch (Exception e)
        {
            RegexErrorMessage = e.Message;
            RegexError = true;
        }

        searchedPlaylistIndexs.AddRange(
            PlaylistManager.FilePathList
            .Select((item, index) => new { Index = index, item.FileName, item.IsFilePlayed })
            .Where((item) =>
            {
                var showPlayedSongsFilterResult = MidiBard.config.SearchFilterPlayedOption switch
                {
                    Configuration.FilterPlayedSongOptions.ShowAll => item.IsFilePlayed == true || item.IsFilePlayed == false,
                    Configuration.FilterPlayedSongOptions.ShowPlayed => item.IsFilePlayed == true,
                    Configuration.FilterPlayedSongOptions.ShowUnPlayed => item.IsFilePlayed == false,
                    _ => item.IsFilePlayed == true || item.IsFilePlayed == false
                };

                var isRegexSearch = MidiBard.config.SearchUseRegex && !RegexError && PlaylistSearchRegex != null;
                var textSearchResult = isRegexSearch ? PlaylistSearchRegex.IsMatch(item.FileName) : item.FileName.ContainsIgnoreCase(PlaylistSearchString);

                return showPlayedSongsFilterResult && textSearchResult;
            })
            .Select(item => item.Index)
            .ToList()
        );
    }
}
