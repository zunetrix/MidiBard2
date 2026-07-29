using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Playlist;
using MidiBard.Util;
using MidiBard.Util.Lyrics;

namespace MidiBard;

public partial class PlaylistWindow
{
    private void DrawColumnsPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("PlaylistColumnsPopup");
        if (!popUp) return;

        ImGui.Text(Language.playlist_col_popup_title);
        ImGui.Separator();
        if (ImGui.Checkbox(Language.common_label_name, ref Plugin.Config.PlaylistWindowColumns.Name)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_artist, ref Plugin.Config.PlaylistWindowColumns.Artist)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_year, ref Plugin.Config.PlaylistWindowColumns.Year)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_duration, ref Plugin.Config.PlaylistWindowColumns.Duration)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_play_count, ref Plugin.Config.PlaylistWindowColumns.PlayCount)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.playlist_col_last_played, ref Plugin.Config.PlaylistWindowColumns.LastPlayed)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_played, ref Plugin.Config.PlaylistWindowColumns.Played)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_rating, ref Plugin.Config.PlaylistWindowColumns.Rating)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_tags, ref Plugin.Config.PlaylistWindowColumns.Tags)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_comments, ref Plugin.Config.PlaylistWindowColumns.Comments)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.common_label_file_path, ref Plugin.Config.PlaylistWindowColumns.FilePath)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.playlist_col_file_modified, ref Plugin.Config.PlaylistWindowColumns.FileModified)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox(Language.playlist_col_file_added, ref Plugin.Config.PlaylistWindowColumns.FileAddedAt)) Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawColSortButton(string label, SongSortColumn colId)
    {
        var icon = _sortCol == colId
            ? (_sortAsc ? FontAwesomeIcon.SortAmountUp : FontAwesomeIcon.SortAmountDown)
            : FontAwesomeIcon.Sort;

        if (ImGuiUtil.IconButton(icon, $"##sortPLCol_{colId}", string.Format(Language.playlist_tooltip_sort_by, label)))
        {
            if (_sortCol == colId)
                _sortAsc = !_sortAsc;
            else
            {
                _sortCol = colId;
                _sortAsc = true;
            }
            ApplySortPlaylistSongs();
        }
    }

    private void DrawPlayedFilterButton()
    {
        var (icon, color, tooltip) = _filterPlayed switch
        {
            1 => (FontAwesomeIcon.Check, (Vector4?)Plugin.Config.playedSongColor, Language.playlist_filter_played),
            2 => (FontAwesomeIcon.Times, (Vector4?)Style.Colors.Red, Language.playlist_filter_not_played),
            _ => (FontAwesomeIcon.Music, (Vector4?)null, Language.playlist_filter_all)
        };

        if (ImGuiUtil.IconButton(icon, "##filterPlayedBtn", tooltip, color))
        {
            _filterPlayed = (_filterPlayed + 1) % 3;
            SearchSongs();
        }
    }

    private void DrawSongList()
    {
        // Compute dynamic column count: # and Actions always visible
        var tableColumnCount = 2;
        if (Plugin.Config.PlaylistWindowColumns.Name) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Artist) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Year) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Duration) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.PlayCount) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.LastPlayed) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Played) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Rating) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Tags) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.Comments) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.FilePath) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.FileModified) tableColumnCount++;
        if (Plugin.Config.PlaylistWindowColumns.FileAddedAt) tableColumnCount++;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;

        using var table = ImRaii.Table("##SongTable", tableColumnCount, tableFlags, new Vector2(-1, 0));
        if (!table) return;
        // Setup columns
        var frameH = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var fixedNoResize = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
        ImGui.TableSetupColumn("#", fixedNoResize, ImGui.CalcTextSize("0000").X);
        ImGui.TableSetupColumn(Language.common_label_actions, fixedNoResize, frameH * 3 + spacing * 2);
        if (Plugin.Config.PlaylistWindowColumns.Name) ImGui.TableSetupColumn(Language.common_label_name, ImGuiTableColumnFlags.WidthFixed, 180f);
        if (Plugin.Config.PlaylistWindowColumns.Artist) ImGui.TableSetupColumn(Language.common_label_artist, ImGuiTableColumnFlags.WidthFixed, 140f);
        if (Plugin.Config.PlaylistWindowColumns.Year) ImGui.TableSetupColumn(Language.common_label_year, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.Duration) ImGui.TableSetupColumn(Language.common_label_duration, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.PlayCount) ImGui.TableSetupColumn(Language.common_label_play_count, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.LastPlayed) ImGui.TableSetupColumn(Language.playlist_col_last_played, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.Played) ImGui.TableSetupColumn(Language.common_label_played, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.Rating) ImGui.TableSetupColumn(Language.common_label_rating, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.Tags) ImGui.TableSetupColumn(Language.common_label_tags, ImGuiTableColumnFlags.WidthFixed, 140f);
        if (Plugin.Config.PlaylistWindowColumns.Comments) ImGui.TableSetupColumn(Language.common_label_comments, ImGuiTableColumnFlags.WidthFixed, 140f);
        if (Plugin.Config.PlaylistWindowColumns.FilePath) ImGui.TableSetupColumn(Language.common_label_file_path, ImGuiTableColumnFlags.WidthFixed, 250f);
        if (Plugin.Config.PlaylistWindowColumns.FileModified) ImGui.TableSetupColumn(Language.playlist_col_file_modified, ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.PlaylistWindowColumns.FileAddedAt) ImGui.TableSetupColumn(Language.playlist_col_file_added, ImGuiTableColumnFlags.WidthFixed);

        // Freeze 2 utility columns (#, actions) + 1 header row
        ImGui.TableSetupScrollFreeze(2, 1);

        // Combined label + filter row
        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableNextColumn();
        ImGui.Text("#");

        ImGui.TableNextColumn();
        ImGui.Text(Language.common_label_actions);

        if (Plugin.Config.PlaylistWindowColumns.Name)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.common_label_name, SongSortColumn.Name);
            ImGui.SameLine();
            ImGui.Text(Language.common_label_name);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PLfilterName", Language.common_input_hint_filter, ref _filterName, 100))
                SearchSongs();
        }
        if (Plugin.Config.PlaylistWindowColumns.Artist)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.common_label_artist, SongSortColumn.Artist);
            ImGui.SameLine();
            ImGui.Text(Language.common_label_artist);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PLfilterArtist", Language.common_input_hint_filter, ref _filterArtist, 100))
                SearchSongs();
        }
        if (Plugin.Config.PlaylistWindowColumns.Year)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.common_label_year, SongSortColumn.Year);
            ImGui.SameLine();
            ImGui.Text(Language.common_label_year);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PLfilterYear", Language.common_input_hint_filter, ref _filterYear, 10))
                SearchSongs();
        }
        if (Plugin.Config.PlaylistWindowColumns.Duration)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.common_label_duration, SongSortColumn.Duration);
            ImGui.SameLine();
            ImGui.Text(Language.common_label_duration);
        }
        if (Plugin.Config.PlaylistWindowColumns.PlayCount)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.common_label_play_count, SongSortColumn.PlayCount);
            ImGui.SameLine();
            ImGui.Text(Language.common_label_play_count);
        }
        if (Plugin.Config.PlaylistWindowColumns.LastPlayed)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.playlist_col_last_played, SongSortColumn.LastPlayed);
            ImGui.SameLine();
            ImGui.Text(Language.playlist_col_last_played);
        }
        if (Plugin.Config.PlaylistWindowColumns.Played)
        {
            ImGui.TableNextColumn();
            DrawPlayedFilterButton();
            ImGui.SameLine();
            ImGui.Text(Language.common_label_played);
        }
        if (Plugin.Config.PlaylistWindowColumns.Rating)
        {
            ImGui.TableNextColumn();
            DrawColSortButton(Language.common_label_rating, SongSortColumn.Rating);
            ImGui.SameLine();
            ImGui.Text(Language.common_label_rating);
        }
        if (Plugin.Config.PlaylistWindowColumns.Tags)
        {
            ImGui.TableNextColumn();
            ImGui.Text(Language.common_label_tags);
            ImGui.SetNextItemWidth(-1);
            if (_filterTagsCombo.Draw("##PLfilterTags", _availableTagNames, ref _filterTags, 10))
                SearchSongs();
            if (!string.IsNullOrEmpty(_filterTags))
            {
                ImGui.SameLine();
                if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##ClearPLTagFilter", Language.common_tooltip_clear_filter))
                {
                    _filterTags = string.Empty;
                    SearchSongs();
                }
            }
        }
        if (Plugin.Config.PlaylistWindowColumns.Comments)
        {
            ImGui.TableNextColumn();
            ImGui.Text(Language.common_label_comments);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PLfilterComments", Language.common_input_hint_filter, ref _filterComments, 200))
                SearchSongs();
        }
        if (Plugin.Config.PlaylistWindowColumns.FilePath)
        {
            ImGui.TableNextColumn();
            ImGui.Text(Language.common_label_file_path);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PLfilterFilePath", Language.common_input_hint_filter, ref _filterFilePath, 200))
                SearchSongs();
        }
        if (Plugin.Config.PlaylistWindowColumns.FileModified)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("FileModified", SongSortColumn.FileModified);
            ImGui.SameLine();
            ImGui.Text(Language.playlist_col_file_modified);
        }
        if (Plugin.Config.PlaylistWindowColumns.FileAddedAt)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("FileAdded", SongSortColumn.FileAddedAt);
            ImGui.SameLine();
            ImGui.Text(Language.playlist_col_file_added);
        }

        // Use clipper for performance with large lists
        var clipper = new ImGuiListClipper();
        clipper.Begin(_songSearchIndexes.Count);

        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= _songSearchIndexes.Count) break;

                var songIndex = _songSearchIndexes[i];
                if (songIndex >= PlaylistSongs.Count) continue;

                var ps = PlaylistSongs[songIndex];
                DrawSongEntry(i, ps, songIndex);
            }
        }

        clipper.End();
    }

    private void DrawSongEntry(int displayIndex, PlaylistSong ps, int songIndex)
    {
        var song = ps.Song;
        if (song == null) return;

        ImGui.PushID($"##PlaylistSongEntry_{song.Id}");
        using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Red, !song.IsValid))
        {
            // Table row
            ImGui.TableNextRow();

            // # column - always visible
            ImGui.TableNextColumn();
            ImGui.Text($"{displayIndex + 1:0000}");
            ImGui.OpenPopupOnItemClick("##PLSongContextMenu", ImGuiPopupFlags.MouseButtonRight);

            // Actions column - always visible
            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemoveSongBtn_{song.Id}", Language.common_tooltip_confirm))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = DeleteSongAsync(song.Id);
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{song.Id}", Language.common_action_edit))
            {
                _selectedSongIndex = songIndex;
                _selectedSong = song;
                Plugin.Ui.PlaylistSongEditWindow.EditPlaylistSong(_selectedPlaylist.Id, song.Id);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(AgentManager.AgentMetronome.EnsembleModeRunning || Plugin.CurrentBardPlayback.IsRunning))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, $"##LoadSongToPlaybackBtn_{song.Id}", Language.playlist_tooltip_load_playback))
                {
                    _selectedSongIndex = songIndex;
                    _selectedSong = song;
                    _ = PlaySongAsync();
                }
            }

            if (Plugin.Config.PlaylistWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                var isSelected = _selectedSongIndex == songIndex;
                if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", isSelected))
                {
                    _selectedSongIndex = songIndex;
                    _selectedSong = song;
                }
                ImGui.OpenPopupOnItemClick("##PLSongContextMenu", ImGuiPopupFlags.MouseButtonRight);
                ImGuiUtil.ToolTip(song.FilePath);

                // DnD payload carries song.Id so reorder works correctly even when a
                // column sort is active (display indices differ from DB order).
                if (ImGui.BeginDragDropSource())
                {
                    unsafe
                    {
                        int id = song.Id;
                        ImGui.SetDragDropPayload("DND_PL_SONG", new ReadOnlySpan<byte>(&id, sizeof(int)), ImGuiCond.None);
                    }
                    ImGui.Text($"({displayIndex + 1}) {song.Name}");
                    ImGui.EndDragDropSource();
                }

                using (ImRaii.PushColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget))
                {
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("DND_PL_SONG");
                        if (!payload.IsNull && payload.IsDelivery())
                        {
                            unsafe
                            {
                                int fromSongId = *(int*)payload.Data;
                                if (fromSongId != song.Id)
                                    _ = ReorderPlaylistSongByIdAsync(fromSongId, song.Id);
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                }
            }

            if (Plugin.Config.PlaylistWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Artist ?? "-");
            }

            if (Plugin.Config.PlaylistWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
            }

            if (Plugin.Config.PlaylistWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Duration.ToString(@"mm\:ss"));
            }

            if (Plugin.Config.PlaylistWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.PlayCount.ToString());
            }

            if (Plugin.Config.PlaylistWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.LastPlayedAt?.ToString("g") ?? "-");
            }

            if (Plugin.Config.PlaylistWindowColumns.Played)
            {
                ImGui.TableNextColumn();
                if (ps.IsPlayed)
                {
                    if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Check, $"ToggleIsPlayed_{song.Id}", Language.playlist_tooltip_toggle_status))
                    {
                        _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, false);
                    }
                }
                else
                {
                    if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, $"ToggleIsPlayed_{song.Id}", Language.playlist_tooltip_toggle_status))
                    {
                        _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, true);
                    }
                }
            }

            if (Plugin.Config.PlaylistWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");
            }

            if (Plugin.Config.PlaylistWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                var tagsText = song.Tags.Count > 0 ? string.Join(", ", song.Tags.Select(t => t.Name)) : "-";
                ImGui.Text(tagsText);
            }

            if (Plugin.Config.PlaylistWindowColumns.Comments)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Comments ?? string.Empty);
            }

            if (Plugin.Config.PlaylistWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FilePath);
            }

            if (Plugin.Config.PlaylistWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FileLastModifiedAt.ToString("g"));
            }

            if (Plugin.Config.PlaylistWindowColumns.FileAddedAt)
            {
                ImGui.TableNextColumn();
                ImGui.Text(ps.AddedAt.ToString("g"));
            }
        }
        DrawSongContextMenu(ps, song, songIndex);
        ImGui.PopID();
    }

    private void DrawSongContextMenu(PlaylistSong ps, Song song, int songIndex)
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using var popup = ImRaii.Popup("##PLSongContextMenu");
        if (!popup) return;

        var isCurrentPlaylist = _selectedPlaylist?.Id == Plugin.PlaylistManager.CurrentPlaylist?.Id;
        var isPlayed = ps.IsPlayed;

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal))
        {
            ImGui.Button($"{song.Name ?? song.FilePath}", new Vector2(ImGui.GetContentRegionAvail().X, 0));
        }

        ImGui.Separator();

        if (ImGui.MenuItem(Language.playlist_menu_toggle_played))
            _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, !isPlayed);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(!isCurrentPlaylist))
        {
            if (ImGui.MenuItem(Language.playlist_menu_send_song_to_chat))
                Plugin.PlaylistManager.SendSongToChat(songIndex);
        }

        if (ImGui.MenuItem(Language.playlist_menu_copy_song_name))
        {
            var songName = isCurrentPlaylist
                ? Plugin.PlaylistManager.GetPostSongName(songIndex)
                : song.Name ?? string.Empty;
            ImGui.SetClipboardText(songName);
            ImGuiUtil.AddNotification(NotificationType.Info, Language.main_notify_song_name_copied);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.MenuItem(Language.playlist_menu_edit_midi))
            Plugin.Ui.MidiEditorWindow.OpenFromFile(song.FilePath);

        if (ImGui.MenuItem(Language.playlist_menu_edit_lyric))
        {
            Plugin.Ui.LyricsEditorWindow.LoadLrcToEditor(new Lyrics(song.FilePath));
            Plugin.Ui.LyricsEditorWindow.IsOpen = true;
        }

        if (ImGui.MenuItem(Language.common_action_open_in_explorer))
            WindowsApi.OpenFileLocation(song.FilePath);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.MenuItem(Language.playlist_menu_edit_data))
            Plugin.Ui.PlaylistSongEditWindow.EditPlaylistSong(_selectedPlaylist!.Id, song.Id);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.MenuItem(Language.playlist_menu_remove_song))
        {
            if (ImGui.GetIO().KeyCtrl)
                _ = DeleteSongAsync(song.Id);
        }
        ImGuiUtil.ToolTip(Language.common_tooltip_confirm);
    }
}
