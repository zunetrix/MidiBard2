using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

using MidiBard.Resources;
using MidiBard.Extensions.String;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MainWindow
{
    private void DrawPlaylistMenu()
    {
        if (IsImportRunning)
        {
            ImGuiUtil.DrawColoredBanner(Language.text_Import_in_progress, Style.Colors.Violet);
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4)))
        {
            using (ImRaii.Disabled(IsImportRunning))
            {
                using (ImRaii.Group())
                {
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
                }
            }

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

        }

        if (ImGui.BeginPopup("PlaylistPopupMenu"))
        {
            // ImGui.MenuItem(Plugin.PlaylistManager.CurrentPlaylist.Name.EllipsisPath(40), false);
            if (ImGui.MenuItem("Playlists"))
            {
                Plugin.Ui.PlaylistWindow.Toggle();
            }

            if (ImGui.MenuItem("Song Library"))
            {
                Plugin.Ui.SongsWindow.Toggle();
            }

            if (ImGui.MenuItem("Backup"))
            {
                Plugin.Ui.BackupWindow.Toggle();
            }

            if (ImGui.MenuItem("BML browser"))
            {
                Plugin.Ui.BardMusicLibraryWindow.Toggle();
            }
            ImGui.EndPopup();
        }

        if (Plugin.Config.enableSearching)
        {
            DrawPlaylistSearchBar();

            var isPlaylistFilteredWithoutMatches = searchedPlaylistIndexs.Count == 0
                && (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0) > 0
                && Plugin.Config.enableSearching
                && (!string.IsNullOrEmpty(PlaylistSearchString) || Plugin.Config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);

            if (isPlaylistFilteredWithoutMatches)
            {
                ImGuiUtil.DrawColoredBanner(Language.no_matching_songs_try_change_the_search_filters, Style.Colors.RedVivid);
            }
        }
    }
}
