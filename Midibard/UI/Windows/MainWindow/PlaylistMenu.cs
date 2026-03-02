using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;

using MidiBard.Resources;
using MidiBard.Util;
using MidiBard.Win32;
using MidiBard.Extensions.String;

namespace MidiBard;

public partial class MainWindow
{
    private void DrawPlaylistMenu()
    {
        if (IsImportRunning)
        {
            ImGuiUtil.DrawColoredBanner(Language.text_Import_in_progress, Style.Colors.Violet);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4));

        ImGui.BeginDisabled(IsImportRunning);
        ImGui.BeginGroup();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##btnPlaylistImportFile", Language.icon_button_tooltip_import_file, size: Style.Dimensions.PlayerButton))
        {
            RunImportFileTask();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##btnPlaylistImportFolder",
                Language.icon_button_tooltip_import_folder, size: Style.Dimensions.PlayerButton))
        {
            RunImportFolderTask();
        }

        ImGui.EndGroup();
        // ImGui.OpenPopupOnItemClick("OpenFileDialog_selection", ImGuiPopupFlags.MouseButtonRight);
        ImGui.EndDisabled();

        //-------------------

        ImGui.SameLine();
        Vector4? color = Plugin.Config.enableSearching ? Plugin.Config.themeColor : null;
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Search, "##btnPlaylistSerach", Language.icon_button_tooltip_search_playlist, color, size: Style.Dimensions.PlayerButton))
        {
            Plugin.Config.enableSearching ^= true;
        }

        //-------------------

        ImGui.SameLine();
        ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "##btnPlaylistClearPlaylist", Language.icon_button_tooltip_clearplaylist_tootltip, size: Style.Dimensions.PlayerButton);
        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Plugin.PlaylistManager.Clear();
            }
        }

        //-------------------

        ImGui.SameLine();
        var fontAwesomeIcon = Plugin.Config.UseStandalonePlaylistWindow
            ? FontAwesomeIcon.Compress
            : FontAwesomeIcon.Expand;
        if (ImGuiUtil.IconButton(fontAwesomeIcon, "##btnPlaylistStandalonePlaylist",
                Language.setting_label_standalone_playlist_window, size: Style.Dimensions.PlayerButton))
        {
            Plugin.Config.UseStandalonePlaylistWindow ^= true;
        }

        //-------------------

        ImGui.SameLine();
        ImGuiUtil.IconButton(FontAwesomeIcon.Eraser, "##btnPlaylistClearHighlightedSongs", size: Style.Dimensions.PlayerButton);
        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Plugin.PlaylistManager.ResetAllSongsPlayedStatusSync();
                // reset filter
                Plugin.Config.SearchFilterPlayedOption = FilterPlayedSongOptions.ShowAll;
            }
        }
        ImGuiUtil.ToolTip(Language.icon_button_tooltip_clear_highlighted_songs);

        //-------------------

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.EllipsisH,
            "##btnPlaylistMoreContextMenu",
            Language.icon_button_tooltip_playlist_menu,
            size: Style.Dimensions.PlayerButton)
        )
        {
            ImGui.OpenPopup("PlaylistPopupMenu");
        }

        ImGui.PopStyleVar();

        if (ImGui.BeginPopup("PlaylistPopupMenu"))
        {
            if (ImGui.MenuItem(Language.PlaylistTitle))
            {
                Plugin.Ui.PlaylistWindow.Toggle();
            }

            ImGui.MenuItem(Plugin.PlaylistManager.CurrentPlaylist.Name.EllipsisPath(40), false);

            var useWin32 = Plugin.Config.useLegacyFileDialog;
            // open playlist
            if (ImGui.MenuItem(Language.menu_label_open_playlist))
            {
                if (useWin32)
                {
                    FileDialogs.OpenPlaylistDialog((result, path) =>
                    {
                        if (result != true) return;
                        DalamudApi.PluginLog.Warning($"TODOWARNING: Open Playlist");
                        // Plugin.PlaylistManager.CurrentPlaylist.ReloadFromFile(path);
                    }, initialDirectory: Plugin.Config.lastOpenedFolderPath);
                }
                else
                {
                    Plugin.Ui.FileDialogService.FileDialogManager.OpenFileDialog("Open playlist", ".mpl", (result, path) =>
                    {
                        if (!result) return;
                        DalamudApi.PluginLog.Warning($"TODOWARNING: ReloadFromFile");
                        // Plugin.PlaylistManager.CurrentContainer.ReloadFromFile(path);
                    });
                }
            }

            // new playlist
            if (ImGui.MenuItem(Language.menu_label_new_playlist))
            {
                // if (Plugin.PlaylistManager.CurrentContainer.FilePathWhenLoading != null)
                // {
                //     Plugin.PlaylistManager.CurrentContainer.Save(Plugin.PlaylistManager.CurrentContainer.FilePathWhenLoading);
                // }

                // if (useWin32)
                // {
                //     FileDialogs.SavePlaylistDialog((result, path) =>
                //     {
                //         if (result != true) return;
                //         Plugin.PlaylistManager.CurrentContainer.ReloadFromFile(path);
                //     }, filename: Language.text_new_playlist, initialDirectory: Plugin.Config.lastOpenedFolderPath);
                // }
                // else
                // {
                //     Plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                //         ".mpl",
                //         Language.text_new_playlist, ".mpl", (result, path) =>
                //         {
                //             if (!result) return;
                //             Plugin.PlaylistManager.CurrentContainer.ReloadFromFile(path);
                //         });
                // }
            }

            // sync playlist
            // if (ImGui.MenuItem(Language.menu_label_sync_playlist))
            // {
            //     Plugin.IpcProvider.SyncAllSettings();
            //     if (Plugin.Config.playOnMultipleDevices && Plugin.Config.usingFileSharingServices)
            //     {
            //         Plugin.PartyChatCommand.SendReloadPlaylist();
            //     }
            //     else
            //     {
            //         Plugin.IpcProvider.LoadPlaylist();
            //     }
            // }

            // save playlist
            if (ImGui.MenuItem(Language.menu_label_save_playlist))
            {
                // Plugin.PlaylistManager.CurrentPlaylist.Save();
            }

            // save playlist as...
            if (ImGui.MenuItem(Language.menu_label_clone_current_playlist))
            {
                // if (useWin32)
                // {
                //     FileDialogs.SavePlaylistDialog((result, path) =>
                //     {
                //         if (result != true) return;
                //         Plugin.PlaylistManager.CurrentContainer.Save(path);
                //     },
                //     filename: Plugin.PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                //     initialDirectory: Plugin.Config.lastOpenedFolderPath);
                // }
                // else
                // {
                //     Plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                //         ".mpl",
                //         Plugin.PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                //         ".mpl", (b, s) =>
                //         {
                //             if (!b) return;
                //             Plugin.PlaylistManager.CurrentContainer.Save(s);
                //         });
                // }
            }

            // save playlist search result as...
            var isPlaylistFiltered = Plugin.Config.enableSearching && (!string.IsNullOrEmpty(PlaylistSearchString)
                || Plugin.Config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);
            if (ImGui.MenuItem(Language.menu_label_save_search_as_playlist, isPlaylistFiltered))
            {
                // var playlistSearchString = PlaylistSearchString;
                // if (useWin32)
                // {
                //     FileDialogs.SavePlaylistDialog((result, path) =>
                //     {
                //         if (result != true) return;
                //         SaveSearchedPlaylist(path);
                //     },
                //     filename: playlistSearchString,
                //     initialDirectory: Plugin.Config.lastOpenedFolderPath);
                // }
                // else
                // {
                //     Plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                //         "*.mpl", playlistSearchString,
                //         ".mpl",
                //         (result, path) =>
                //         {
                //             if (!result) return;
                //             SaveSearchedPlaylist(path);
                //         });
                // }

                // void SaveSearchedPlaylist(string filePath)
                // {
                //     try
                //     {
                //         RefreshPlaylistSearchResult();
                //         Plugin.PlaylistManager.CurrentContainer.ReloadFromFile(filePath);
                //         Plugin.PlaylistManager.CurrentContainer.SongPaths = searchedPlaylistIndexs
                //             .Select(i => Plugin.PlaylistManager.FilePathList[i]).ToList();
                //         Plugin.PlaylistManager.CurrentContainer.Save();
                //     }
                //     catch (Exception e)
                //     {
                //         DalamudApi.PluginLog.Warning(e, "error when saving current search result");
                //     }
                // }
            }

            if (ImGui.MenuItem("Save playlist as CSV"))
            {
                // if (useWin32)
                // {
                //     FileDialogs.SavePlaylistDialog((result, path) =>
                //     {
                //         if (result != true) return;
                //         Plugin.PlaylistManager.CurrentContainer.ExportToCsv(
                //             path,
                //             Plugin.Config.postSongNameCaptureRegex,
                //             Plugin.Config.postSongNameCaptureOutputFormat,
                //             Plugin.Config.postSongNameFindRegex,
                //             Plugin.Config.postSongNameReplacement);
                //     },
                //     filename: Plugin.PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                //     initialDirectory: Plugin.Config.lastOpenedFolderPath);
                // }
                // else
                // {
                //     Plugin.Ui.FileDialogService.FileDialogManager.SaveFileDialog(Language.window_title_choose_new_playlist_save_location,
                //         ".csv",
                //         Plugin.PlaylistManager.CurrentContainer.DisplayName + Language.text_file_copy,
                //         ".csv", (b, s) =>
                //         {
                //             if (!b) return;
                //             Plugin.PlaylistManager.CurrentContainer.ExportToCsv(s,
                //             Plugin.Config.postSongNameCaptureRegex,
                //             Plugin.Config.postSongNameCaptureOutputFormat,
                //             Plugin.Config.postSongNameFindRegex,
                //             Plugin.Config.postSongNameReplacement);
                //         });
                // }
            }

            //var totalDuration = Plugin.PlaylistManager.CurrentContainer.TotalDuration;
            //var durationString = totalDuration == TimeSpan.Zero
            //    ? "Not calculated"
            //    : $"{(int)totalDuration.TotalHours}h {(int)totalDuration.Minutes}m {(int)totalDuration.Seconds}s";
            //MenuItem($"Playlist total duration: {durationString}", false);
            if (ImGui.MenuItem("Recalculate playlist duration"))
            {
                DalamudApi.PluginLog.Information("Recalculate playlist duration");
                // Task.Run(Plugin.PlaylistManager.CalculateDurationAll);
            }

            if (ImGui.MenuItem("Remove duplicate songs by name"))
            {
                // DalamudApi.PluginLog.Information("Removing duplicate songs");
                // var distinctBy = Plugin.PlaylistManager.CurrentContainer.SongPaths.DistinctBy(i => i.FileName).ToList();
                // var currentContainerSongPaths = Plugin.PlaylistManager.CurrentContainer.SongPaths;
                // ImGuiUtil.AddNotification(NotificationType.Info, $"Removed {currentContainerSongPaths.Count - distinctBy.Count} entries");
                // Plugin.PlaylistManager.CurrentContainer.SongPaths = distinctBy;
            }

            ImGui.Separator();

            // recent used playlists
            ImGui.MenuItem(Language.menu_text_recent_playlist, false);
            var takeLast = Plugin.Config.RecentUsedPlaylists.TakeLast(10).Reverse();
            var id = 89465;
            try
            {
                foreach (var playlistPath in takeLast)
                {
                    ImGui.PushID(id++);
                    try
                    {
                        var ellipsisString = Path.ChangeExtension(playlistPath, null).EllipsisPath(40);
                        if (ImGui.BeginMenu(ellipsisString + "        "))
                        {
                            if (ImGui.MenuItem(Language.menu_item_load_playlist))
                            {
                                // Plugin.PlaylistManager.CurrentContainer.ReloadFromFile(playlistPath);

                                // TODO: implement RecentUsedPlaylists inside PlaylistManager
                                // Plugin.Config.RecentUsedPlaylists.Remove(playlistPath);
                            }

                            if (ImGui.MenuItem(Language.menu_item_open_in_file_explorer))
                            {
                                try
                                {
                                    if (!File.Exists(playlistPath))
                                    {
                                        Plugin.Config.RecentUsedPlaylists.Remove(playlistPath);
                                    }

                                    WindowsApi.OpenFileLocation(playlistPath);
                                }
                                catch (Exception e)
                                {
                                    DalamudApi.PluginLog.Warning(e, "error when opening process");
                                }
                            }

                            if (ImGui.MenuItem(Language.menu_item_open_in_text_editor))
                            {
                                try
                                {
                                    if (!File.Exists(playlistPath))
                                    {
                                        Plugin.Config.RecentUsedPlaylists.Remove(playlistPath);
                                    }

                                    WindowsApi.ExecuteCmd(playlistPath);
                                }
                                catch (Exception e)
                                {
                                    DalamudApi.PluginLog.Warning(e, $"error when opening process {playlistPath}");
                                }
                            }

                            if (ImGui.MenuItem(Language.menu_item_remove_from_recent_list))
                            {
                                Plugin.Config.RecentUsedPlaylists.Remove(playlistPath);
                            }

                            ImGui.EndMenu();
                        }
                    }
                    catch (Exception e)
                    {
                        DalamudApi.PluginLog.Warning(e, "error when drawing recent playlist");
                    }

                    ImGui.PopID();
                }
            }
            catch
            {
                // ignored
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Open BML browser"))
            {
                Plugin.Ui.BardMusicLibraryWindow.Toggle();
            }

            ImGui.EndPopup();
        }

        //-------------------

        if (Plugin.Config.enableSearching)
        {
            DrawPlaylistSearchBar();

            var isPlaylistFilteredWithoutMatches = searchedPlaylistIndexs.Count == 0
                && Plugin.PlaylistManager.FilePathList.Any()
                && Plugin.Config.enableSearching
                && (!string.IsNullOrEmpty(PlaylistSearchString) || Plugin.Config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);

            if (isPlaylistFilteredWithoutMatches)
            {
                ImGuiUtil.DrawColoredBanner(Language.no_matching_songs_try_change_the_search_filters, Style.Colors.Red);
            }
        }
    }
}
