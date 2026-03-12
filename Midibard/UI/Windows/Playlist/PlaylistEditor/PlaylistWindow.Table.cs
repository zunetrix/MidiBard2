using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public partial class PlaylistWindow
{
    private void DrawColumnsPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("PlaylistColumnsPopup");
        if (!popUp) return;

        ImGui.Text("Columns");
        ImGui.Separator();
        if (ImGui.Checkbox("Name", ref Plugin.Config.PlaylistWindowColumns.Name)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Artist", ref Plugin.Config.PlaylistWindowColumns.Artist)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Year", ref Plugin.Config.PlaylistWindowColumns.Year)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Duration", ref Plugin.Config.PlaylistWindowColumns.Duration)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Play Count", ref Plugin.Config.PlaylistWindowColumns.PlayCount)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Last Played", ref Plugin.Config.PlaylistWindowColumns.LastPlayed)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Played", ref Plugin.Config.PlaylistWindowColumns.Played)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Rating", ref Plugin.Config.PlaylistWindowColumns.Rating)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Tags", ref Plugin.Config.PlaylistWindowColumns.Tags)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("Comments", ref Plugin.Config.PlaylistWindowColumns.Comments)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("File Path", ref Plugin.Config.PlaylistWindowColumns.FilePath)) Plugin.IpcProvider.SyncAllSettings();
        if (ImGui.Checkbox("File Modified", ref Plugin.Config.PlaylistWindowColumns.FileModified)) Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawColSortButton(string label, SongSortColumn colId)
    {
        var icon = _sortCol == colId
            ? (_sortAsc ? FontAwesomeIcon.SortAmountUp : FontAwesomeIcon.SortAmountDown)
            : FontAwesomeIcon.Sort;

        if (ImGuiUtil.IconButton(icon, $"##sortPLCol_{colId}", $"Sort by {label}"))
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
            1 => (FontAwesomeIcon.Check, (Vector4?)Plugin.Config.playedSongColor, "Filter: Played"),
            2 => (FontAwesomeIcon.Times, (Vector4?)Style.Colors.Red, "Filter: Not played"),
            _ => (FontAwesomeIcon.Music, (Vector4?)null, "Filter: All")
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

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX;

        if (ImGui.BeginTable("##SongTable", tableColumnCount, tableFlags))
        {
            // Setup columns
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Name) ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.Artist) ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.Year) ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Duration) ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.PlayCount) ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.LastPlayed) ImGui.TableSetupColumn("Last Played", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Played) ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Rating) ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
            if (Plugin.Config.PlaylistWindowColumns.Tags) ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.Comments) ImGui.TableSetupColumn("Comments", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.FilePath) ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthStretch);
            if (Plugin.Config.PlaylistWindowColumns.FileModified) ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);

            // Freeze 1 header row so it stays visible while scrolling
            ImGui.TableSetupScrollFreeze(0, 1);

            // --- Combined label + filter row ---
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

            ImGui.TableNextColumn();
            ImGui.Text("#");

            ImGui.TableNextColumn();
            ImGui.Text("Actions");

            if (Plugin.Config.PlaylistWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Name", SongSortColumn.Name);
                ImGui.SameLine();
                ImGui.Text("Name");
                if (ImGui.InputTextWithHint("##PLfilterName", "Filter...", ref _filterName, 100))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Artist", SongSortColumn.Artist);
                ImGui.SameLine();
                ImGui.Text("Artist");
                if (ImGui.InputTextWithHint("##PLfilterArtist", "Filter...", ref _filterArtist, 100))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Year", SongSortColumn.Year);
                ImGui.SameLine();
                ImGui.Text("Year");
                if (ImGui.InputTextWithHint("##PLfilterYear", "Filter...", ref _filterYear, 10))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Duration", SongSortColumn.Duration);
                ImGui.SameLine();
                ImGui.Text("Duration");
            }
            if (Plugin.Config.PlaylistWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("PlayCount", SongSortColumn.PlayCount);
                ImGui.SameLine();
                ImGui.Text("Play Count");
            }
            if (Plugin.Config.PlaylistWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("LastPlayed", SongSortColumn.LastPlayed);
                ImGui.SameLine();
                ImGui.Text("Last Played");
            }
            if (Plugin.Config.PlaylistWindowColumns.Played)
            {
                ImGui.TableNextColumn();
                DrawPlayedFilterButton();
                ImGui.SameLine();
                ImGui.Text("Played");
            }
            if (Plugin.Config.PlaylistWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("Rating", SongSortColumn.Rating);
                ImGui.SameLine();
                ImGui.Text("Rating");
            }
            if (Plugin.Config.PlaylistWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Tags");
                if (ImGui.InputTextWithHint("##PLfilterTags", "Filter...", ref _filterTags, 100))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.Comments)
            {
                ImGui.TableNextColumn();
                ImGui.Text("Comments");
                if (ImGui.InputTextWithHint("##PLfilterComments", "Filter...", ref _filterComments, 200))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text("File Path");
                if (ImGui.InputTextWithHint("##PLfilterFilePath", "Filter...", ref _filterFilePath, 200))
                    SearchSongs();
            }
            if (Plugin.Config.PlaylistWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                DrawColSortButton("FileModified", SongSortColumn.FileModified);
                ImGui.SameLine();
                ImGui.Text("File Modified");
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
            ImGui.EndTable();
        }
    }

    private void DrawSongEntry(int displayIndex, PlaylistSong ps, int songIndex)
    {
        var song = ps.Song;
        if (song == null) return;

        ImGui.PushID($"##PlaylistSongEntry_{song.Id}");
        var textColor = song.IsValid ? Vector4.One : Style.Colors.Red;
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
        {
            // Table row
            ImGui.TableNextRow();

            // # column - always visible
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Text($"{displayIndex + 1:0000}");
            ImGui.PopStyleColor();

            // Actions column - always visible
            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##RemoveSongBtn_{song.Id}", Language.ConfirmInstructionTooltip))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = DeleteSongAsync(song.Id);
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{song.Id}", "Edit"))
            {
                _selectedSongIndex = songIndex;
                _selectedSong = song;
                Plugin.Ui.PlaylistSongEditWindow.EditPlaylistSong(_selectedPlaylist.Id, song.Id);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(Plugin.AgentMetronome.EnsembleModeRunning || Plugin.CurrentBardPlayback.IsRunning))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Play, $"##LoadSongToPlaybackBtn_{song.Id}", "Load to Playback"))
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
                ImGuiUtil.ToolTip(song.FilePath);

                if (ImGui.BeginDragDropSource())
                {
                    unsafe
                    {
                        int idx = songIndex;
                        ImGui.SetDragDropPayload("DND_PL_SONG", new ReadOnlySpan<byte>(&idx, sizeof(int)), ImGuiCond.None);
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
                                int fromIdx = *(int*)payload.Data;
                                if (fromIdx != songIndex)
                                    _ = ReorderPlaylistSongAsync(fromIdx, songIndex);
                            }
                        }
                        ImGui.EndDragDropTarget();
                    }
                }
            }

            if (Plugin.Config.PlaylistWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.Artist ?? "-");
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.Duration.ToString(@"mm\:ss"));
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.PlayCount.ToString());
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.Text(song.LastPlayedAt?.ToString("g") ?? "-");
                ImGui.PopStyleColor();
            }

            if (Plugin.Config.PlaylistWindowColumns.Played)
            {
                ImGui.TableNextColumn();
                if (ps.IsPlayed)
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonSuccessNormal)
                    .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonSuccessHovered)
                    .Push(ImGuiCol.ButtonActive, Style.Components.ButtonSuccessActive))
                    {
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.Check, $"ToggleIsPlayed_{song.Id}", "Click to toggle status"))
                        {
                            _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, false);
                        }
                    }
                }
                else
                {
                    using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
                   .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
                   .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
                    {
                        if (ImGuiUtil.IconButton(FontAwesomeIcon.Times, $"ToggleIsPlayed_{song.Id}", "Click to toggle status"))
                        {
                            _ = UpdatePlaylistSongPlayedStatusAsync(songIndex, true);
                        }
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
        }
        ImGui.PopID();
    }
}
