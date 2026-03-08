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
using System.Linq;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MainWindow
{
    private int songTargetIndexInputValue = 1;

    private void DrawCurrentPlaylist()
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
    }

    private void DrawContent()
    {
        DrawPlaylistMenu();

        if (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Any() ?? false)
        {
            DrawPlaylistTable();
        }
        else
        {
            if (ImGui.Button(Language.text_playlist_is_empty, new Vector2(-1, ImGui.GetFrameHeight())))
            {
                RunImportFileTask();
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
            var maxSongsToDisplay = Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0;
            beginChild = ImGui.BeginChild("playlistchild",
                new Vector2(x: -1, y: ImGui.GetTextLineHeightWithSpacing() * Math.Min(minSongsToDisplay, maxSongsToDisplay)));
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

                var isFiltered = Plugin.Config.enableSearching &&
                  (!string.IsNullOrEmpty(PlaylistSearchString) ||
                  Plugin.Config.SearchFilterPlayedOption != FilterPlayedSongOptions.ShowAll);

                var currentPlaylistId = Plugin.PlaylistManager.CurrentPlaylist?.Id ?? -1;
                if (isFiltered && currentPlaylistId != _lastRefreshedPlaylistId)
                    RefreshPlaylistSearchResult();

                var itemCount = isFiltered ? searchedPlaylistIndexs.Count : Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 0;

                bool lockMultipleDevicesOptions = Plugin.Config.playOnMultipleDevices
                                            && Plugin.Config.useChatPlaylistSync
                                            && DalamudApi.PartyList.IsInParty()
                                            && !DalamudApi.PartyList.IsPartyLeader();

                var clipper = new ImGuiListClipper();
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

                if (_playlistScrollToCurrentSong)
                {
                    _playlistScrollToCurrentSong = false;
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
        DrawPlaylistItemSelectable(i, lockMultipleDevicesOptions);

        ImGui.TableNextColumn();
        ImGui.BeginDisabled(lockMultipleDevicesOptions);
        DrawPlaylistDeleteButton(i);
        ImGui.EndDisabled();

        ImGui.TableNextColumn();
        DrawPlaylistTrackDuration(entry);

        ImGui.TableNextColumn();
        DrawPlaylistTrackName(entry);

        ImGui.PopID();
    }

    private void DrawPlaylistItemSelectable(int songIndex, bool lockMultipleDevicesOptions)
    {
        if (ImGui.Selectable($"{songIndex + 1:000}##playlistItem", Plugin.PlaylistManager.CurrentSongIndex == songIndex,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.AllowItemOverlap))
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (!Plugin.AgentMetronome.EnsembleModeRunning)
                {
                    if (Plugin.Config.playOnMultipleDevices && DalamudApi.PartyList.Length > 1)
                    {
                        Plugin.ChatWatcher.SendSwitchTo(songIndex);
                    }
                    else
                    {
                        Plugin.MidiPlayerControl.StopLrc();
                        Plugin.PlaylistManager.LoadPlayback(songIndex);
                    }
                }
            }
        }

        DrawPlaylistContextMenu(songIndex, lockMultipleDevicesOptions);

        // Drag & Drop
        ImGui.BeginDisabled(lockMultipleDevicesOptions);
        if (ImGui.BeginDragDropSource())
        {
            unsafe
            {
                ImGui.SetDragDropPayload("DND_PLAYLIST_ITEM", new ReadOnlySpan<byte>(&songIndex, sizeof(int)), ImGuiCond.None);
                ImGui.PushStyleColor(ImGuiCol.Button, Style.Components.ButtonInfoActive);
                ImGui.Button($"({songIndex + 1}) {Plugin.PlaylistManager.CurrentPlaylist?.Songs?[songIndex].GetFileName()}");
                ImGui.PopStyleColor();
            }
            ImGui.EndDragDropSource();
        }

        ImGui.PushStyleColor(ImGuiCol.DragDropTarget, Style.Components.DragDropTarget);
        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr dragDropPayload = ImGui.AcceptDragDropPayload("DND_PLAYLIST_ITEM");

            bool isDropping = !dragDropPayload.IsNull;

            if (isDropping && dragDropPayload.IsDelivery())
            {
                unsafe
                {
                    int originalIndex = *(int*)dragDropPayload.Data;

                    int offset = songIndex - originalIndex;
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

    private void DrawPlaylistContextMenu(int songIndex, bool lockMultipleDevicesOptions)
    {
        ImGui.OpenPopupOnItemClick($"##playlistRightClickMenu", ImGuiPopupFlags.MouseButtonRight);
        // 7.13: converted manual PushStyleColor/PushStyleVar to RAII (safe against early returns)
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f);
        using (var popUp = ImRaii.Popup($"##playlistRightClickMenu"))
        {
            if (!popUp) return;

            var song = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[songIndex];
            var isFilePlayed = song.IsPlayed;

            using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonInfoNormal)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonInfoNormal))
            {
                float fullWidth = ImGui.GetContentRegionAvail().X;
                ImGui.Button($"({songIndex + 1}) {Plugin.PlaylistManager.CurrentPlaylist?.Songs?[songIndex].GetFileName()}", new Vector2(fullWidth, 0));
            }

            ImGui.Separator();

            if (ImGui.MenuItem(Language.menu_label_toggle_song_played_status))
            {
                Plugin.PlaylistManager.ChangeSongPlayedStatusSync(songIndex, !isFilePlayed);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.MenuItem(Language.menu_label_send_song_name_to_chat))
            {
                Plugin.PlaylistManager.SendSongToChat(songIndex);
            }

            if (ImGui.MenuItem(Language.menu_label_copy_song_name))
            {
                var songName = Plugin.PlaylistManager.GetPostSongName(songIndex);
                ImGui.SetClipboardText($"{songName}");
                ImGuiUtil.AddNotification(NotificationType.Info, Language.text_song_name_copied_to_clipboard);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.BeginDisabled(lockMultipleDevicesOptions);
            if (ImGui.MenuItem(Language.menu_label_move_song_to_top))
            {
                Plugin.PlaylistManager.MoveSongToIndexSync(songIndex, 0);
            }

            if (ImGui.MenuItem(Language.menu_label_move_song_to_bottom))
            {
                var lastIndex = (Plugin.PlaylistManager.CurrentPlaylist?.Songs?.Count ?? 1) - 1;
                Plugin.PlaylistManager.MoveSongToIndexSync(songIndex, lastIndex);
            }

            ImGui.Spacing();

            ImGui.Text(Language.menu_label_move_song_to_position);
            ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("##btnMoveSongToIndex", ref songTargetIndexInputValue, 1, 10, default, ImGuiInputTextFlags.AutoSelectAll))
            {
                if (songTargetIndexInputValue <= 0)
                    songTargetIndexInputValue = 1;
            }

            ImGui.SameLine();
            if (ImGui.Button("Move"))
            {
                Plugin.PlaylistManager.MoveSongToIndexSync(songIndex, songTargetIndexInputValue - 1);
            }
            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.MenuItem("Edit lyric"))
            {
                var entry = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[songIndex];
                Plugin.Ui.LyricsEditorWindow.LoadLrcToEditor(new Lyrics(entry.GetFilePath()));
                Plugin.Ui.LyricsEditorWindow.IsOpen = true;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.MenuItem(Language.menu_item_open_in_file_explorer))
            {
                var entry = Plugin.PlaylistManager.CurrentPlaylist?.Songs?[songIndex];
                WindowsApi.OpenFileLocation(entry.GetFilePath());
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.BeginDisabled(lockMultipleDevicesOptions);
            if (ImGui.MenuItem(Language.menu_label_remove_song_from_playlist))
            {
                Plugin.PlaylistManager.RemoveSync(songIndex);
            }
            ImGui.EndDisabled();

        }
    }

    private void DrawPlaylistDeleteButton(int songIndex)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Colors.Transparent)
        .Push(ImGuiCol.ButtonHovered, Style.Colors.Transparent)
        .Push(ImGuiCol.ButtonActive, Style.Colors.Transparent))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##btnDeletePlaylistSong##{songIndex}"))
            {
                Plugin.PlaylistManager.RemoveSync(songIndex);
            }
            ImGui.PopStyleVar();
        }
    }

    private void DrawPlaylistTrackDuration(PlaylistSong entry)
    {
        // 7.10: removed pointless $"{...}" interpolation wrapper
        ImGui.Text(entry.GetSongLengthFormated());
    }

    private void DrawPlaylistTrackName(PlaylistSong entry)
    {
        var displayName = entry.GetFileName();
        if (entry.IsPlayed)
            ImGui.PushStyleColor(ImGuiCol.Text, Plugin.Config.playedSongColor);

        ImGui.Text(displayName);

        if (entry.IsPlayed)
            ImGui.PopStyleColor();

        var songTooltipText = $"{displayName}\n\n{entry.GetFileDirectory()}";
        ImGuiUtil.ToolTip(songTooltipText + "\n\n(Drag to reorder - right click for more options)");
    }
}
