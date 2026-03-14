using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public partial class SongsWindow
{
    private void DrawSongTable()
    {
        // Compute dynamic column count: # and Actions are always visible
        var tableColumnCount = 3;
        if (Plugin.Config.SongsWindowColumns.Name) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Artist) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Year) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Duration) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.PlayCount) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.LastPlayed) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Rating) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.FilePath) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Tags) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.Comments) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.FileModified) tableColumnCount++;
        if (Plugin.Config.SongsWindowColumns.IsValid) tableColumnCount++;

        var tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;

        using var table = ImRaii.Table("##SongsTable", tableColumnCount, tableFlags, new Vector2(-1, 0));
        if (!table) return;
        // Setup columns
        var frameH = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var fixedNoResize = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
        ImGui.TableSetupColumn("##ColCheckbox", fixedNoResize, frameH);
        ImGui.TableSetupColumn("##ColNumber", fixedNoResize, ImGui.CalcTextSize("0000").X);
        ImGui.TableSetupColumn("Actions", fixedNoResize, frameH * 2 + spacing);
        if (Plugin.Config.SongsWindowColumns.Name) ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 180f);
        if (Plugin.Config.SongsWindowColumns.Artist) ImGui.TableSetupColumn("Artist", ImGuiTableColumnFlags.WidthFixed, 140f);
        if (Plugin.Config.SongsWindowColumns.Year) ImGui.TableSetupColumn("Year", ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.SongsWindowColumns.Duration) ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.SongsWindowColumns.PlayCount) ImGui.TableSetupColumn("Play Count", ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.SongsWindowColumns.LastPlayed) ImGui.TableSetupColumn("Last Played", ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.SongsWindowColumns.Rating) ImGui.TableSetupColumn("Rating", ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.SongsWindowColumns.FilePath) ImGui.TableSetupColumn("File Path", ImGuiTableColumnFlags.WidthFixed, 250f);
        if (Plugin.Config.SongsWindowColumns.Tags) ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthFixed, 140f);
        if (Plugin.Config.SongsWindowColumns.Comments) ImGui.TableSetupColumn("Comments", ImGuiTableColumnFlags.WidthFixed, 140f);
        if (Plugin.Config.SongsWindowColumns.FileModified) ImGui.TableSetupColumn("File Modified", ImGuiTableColumnFlags.WidthFixed);
        if (Plugin.Config.SongsWindowColumns.IsValid) ImGui.TableSetupColumn("Valid", ImGuiTableColumnFlags.WidthFixed);

        // Freeze 3 utility columns (checkbox, #, actions) + 1 header row
        ImGui.TableSetupScrollFreeze(3, 1);

        // Combined label + filter row
        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));

        ImGui.TableNextColumn();
        ImGui.Text(""); // for checkbox show in 2nd line
        if (ImGui.Checkbox($"##GlobalMacroCheckbox", ref _isGlobalSongsCheckboxChecked))
        {
            if (_isGlobalSongsCheckboxChecked)
                SelectAllSongs();
            else
                ClearSongsSelection();
        }
        ImGuiUtil.ToolTip("Select / Unselect All");

        ImGui.TableNextColumn();
        ImGui.Text("#");

        ImGui.TableNextColumn();
        ImGui.Text("Actions");

        if (Plugin.Config.SongsWindowColumns.Name)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("Name", SongSortColumn.Name);
            ImGui.SameLine();
            ImGui.Text("Name");
            if (ImGui.InputTextWithHint("##filterName", "Filter...", ref _filterName, 100))
                Search();
        }
        if (Plugin.Config.SongsWindowColumns.Artist)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("Artist", SongSortColumn.Artist);
            ImGui.SameLine();
            ImGui.Text("Artist");
            if (ImGui.InputTextWithHint("##filterArtist", "Filter...", ref _filterArtist, 100))
                Search();
        }
        if (Plugin.Config.SongsWindowColumns.Year)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("Year", SongSortColumn.Year);
            ImGui.SameLine();
            ImGui.Text("Year");
            if (ImGui.InputTextWithHint("##filterYear", "Filter...", ref _filterYear, 10))
                Search();
        }
        if (Plugin.Config.SongsWindowColumns.Duration)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("Duration", SongSortColumn.Duration);
            ImGui.SameLine();
            ImGui.Text("Duration");
        }
        if (Plugin.Config.SongsWindowColumns.PlayCount)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("PlayCount", SongSortColumn.PlayCount);
            ImGui.SameLine();
            ImGui.Text("Play Count");
        }
        if (Plugin.Config.SongsWindowColumns.LastPlayed)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("LastPlayed", SongSortColumn.LastPlayed);
            ImGui.SameLine();
            ImGui.Text("Last Played");
        }
        if (Plugin.Config.SongsWindowColumns.Rating)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("Rating", SongSortColumn.Rating);
            ImGui.SameLine();
            ImGui.Text("Rating");
        }
        if (Plugin.Config.SongsWindowColumns.FilePath)
        {
            ImGui.TableNextColumn();
            ImGui.Text("File Path");
            if (ImGui.InputTextWithHint("##filterFilePath", "Filter...", ref _filterFilePath, 200))
                Search();
        }
        if (Plugin.Config.SongsWindowColumns.Tags)
        {
            ImGui.TableNextColumn();
            ImGui.Text("Tags");
            if (ImGuiUtil.DrawComboSearch("##filterTags", _availableTagNames, ref _filterTags, 10))
                Search();
            if (!string.IsNullOrEmpty(_filterTags))
            {
                ImGui.SameLine();
                if (ImGuiUtil.DangerIconButton(FontAwesomeIcon.Times, "##ClearTagFilter", "Clear filter"))
                {
                    _filterTags = string.Empty;
                    Search();
                }
            }
        }
        if (Plugin.Config.SongsWindowColumns.Comments)
        {
            ImGui.TableNextColumn();
            ImGui.Text("Comments");
            if (ImGui.InputTextWithHint("##filterComments", "Filter...", ref _filterComments, 200))
                Search();
        }
        if (Plugin.Config.SongsWindowColumns.FileModified)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("FileModified", SongSortColumn.FileModified);
            ImGui.SameLine();
            ImGui.Text("File Modified");
        }
        if (Plugin.Config.SongsWindowColumns.IsValid)
        {
            ImGui.TableNextColumn();
            DrawColSortButton("Valid", SongSortColumn.IsValid);
            ImGui.SameLine();
            ImGui.Text("Valid");
        }

        // Use clipper for performance with large lists
        var clipper = new ImGuiListClipper();
        clipper.Begin(_searchIndexes.Count);

        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i >= _searchIndexes.Count) break;

                var songIndex = _searchIndexes[i];
                if (songIndex >= _songs.Count) continue;

                var song = _songs[songIndex];
                DrawSongRow(i, song, songIndex);
            }
        }

        clipper.End();
    }

    private void DrawSongRow(int displayIndex, Song song, int songIndex)
    {
        ImGui.PushID($"##SongEntry_{song.Id}");

        using (ImRaii.PushColor(ImGuiCol.Text, Style.Colors.Red, !song.IsValid))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            bool isChecked = _selectedSongIds.Contains(song.Id);
            if (ImGui.Checkbox($"##{song.Id}", ref isChecked))
            {
                if (isChecked)
                    _selectedSongIds.Add(song.Id);
                else
                    _selectedSongIds.Remove(song.Id);
            }

            // # column - always visible
            ImGui.TableNextColumn();
            ImGui.Text($"{displayIndex + 1:0000}");

            // Actions column - always visible
            ImGui.TableNextColumn();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteSongBtn_{songIndex}", Language.ConfirmInstructionTooltip))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    _ = DeleteSongAsync(song.Id);
                }
            }

            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, $"##EditSongBtn_{songIndex}", "Edit"))
            {
                Plugin.Ui.SongEditWindow.EditSong(song.Id);
            }

            if (Plugin.Config.SongsWindowColumns.Name)
            {
                ImGui.TableNextColumn();
                // ImGui.Text($"({song.Id}) ");
                // ImGui.SameLine();
                if (ImGui.Selectable($"{song.Name}##Song_{song.Id}", false)) { }
                ImGuiUtil.ToolTip(song.FilePath);
            }

            if (Plugin.Config.SongsWindowColumns.Artist)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Artist ?? "-");
            }

            if (Plugin.Config.SongsWindowColumns.Year)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.ReleaseYear > 0 ? song.ReleaseYear.ToString() : "-");
            }

            if (Plugin.Config.SongsWindowColumns.Duration)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Duration.ToString(@"mm\:ss"));
            }

            if (Plugin.Config.SongsWindowColumns.PlayCount)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.PlayCount.ToString());
            }

            if (Plugin.Config.SongsWindowColumns.LastPlayed)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.LastPlayedAt.HasValue ? song.LastPlayedAt.Value.ToString("g") : "-");
            }

            if (Plugin.Config.SongsWindowColumns.Rating)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Rating > 0 ? new string('★', song.Rating) : "-");
            }

            if (Plugin.Config.SongsWindowColumns.FilePath)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FilePath);
            }

            if (Plugin.Config.SongsWindowColumns.Tags)
            {
                ImGui.TableNextColumn();
                var tagsText = song.Tags.Count > 0 ? string.Join(", ", song.Tags.Select(t => t.Name)) : "-";
                ImGui.Text(tagsText);
            }

            if (Plugin.Config.SongsWindowColumns.Comments)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.Comments ?? "-");
            }

            if (Plugin.Config.SongsWindowColumns.FileModified)
            {
                ImGui.TableNextColumn();
                ImGui.Text(song.FileLastModifiedAt.ToString("g"));
            }

            if (Plugin.Config.SongsWindowColumns.IsValid)
            {
                ImGui.TableNextColumn();
                var (icon, color) = song.IsValid
                    ? (FontAwesomeIcon.Check, Plugin.Config.playedSongColor)
                    : (FontAwesomeIcon.Times, Style.Colors.Red);
                using (ImRaii.PushColor(ImGuiCol.Text, color))
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    ImGui.Text(icon.ToIconString());
            }
        }
        ImGui.PopID();
    }
}
