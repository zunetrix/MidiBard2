using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

using MidiBard.Extensions.String;
using MidiBard.Resources;

namespace MidiBard;

public partial class MainWindow
{
    private Regex PlaylistSearchRegex = null;
    private string PlaylistSearchString = "";
    private readonly List<int> searchedPlaylistIndexs = new();
    private bool RegexError;
    private string RegexErrorMessage = "";
    private bool songDurationSortDirectionDesc = true;
    private bool songNameSortDirectionDesc = true;

    private void DrawPlaylistSearchBar()
    {
        var regexError = Plugin.Config.SearchUseRegex && RegexError;

        if (regexError)
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Lerp(Style.Components.FrameBg, Style.Colors.Red, 0.5f));

        // ImGui.SetNextItemWidth(-1);
        float iconButtonWidth = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.X * 2;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        int totalButtons = 3;
        float totalButtonsWidth = iconButtonWidth * totalButtons + spacing * totalButtons;
        float inputWidth = ImGui.GetContentRegionAvail().X - totalButtonsWidth;
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputTextWithHint("##searchplaylist", Plugin.Config.SearchUseRegex ? "Enter regex to search" : Language.hint_search_textbox, ref PlaylistSearchString, 255, ImGuiInputTextFlags.AutoSelectAll))
        {
            RefreshPlaylistSearchResult();
        }

        if (regexError)
        {
            if (ImGui.IsItemFocused())
            {
                ImGui.SetNextWindowPos(ImGui.GetItemRectMin() + new Vector2(0, ImGui.GetFrameHeightWithSpacing()));
                if (ImGui.Begin("##tooltipRegexError", ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.TextUnformatted(RegexErrorMessage);
                }
                ImGui.End();
            }
            ImGui.PopStyleColor();
        }

        // DrawClearButton();

        DrawUseRegexButton();

        DrawFilterPlayedSongsButton();

        DrawSortPlaylistButton();
    }

    private void DrawUseRegexButton()
    {
        Vector4? color = Plugin.Config.SearchUseRegex ? Plugin.Config.themeColor : null;
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.StarOfLife, "buttonUseRegex", "Use regex", color))
        {
            Plugin.Config.SearchUseRegex = !Plugin.Config.SearchUseRegex;
            RefreshPlaylistSearchResult();
        }
    }

    // private void DrawClearButton()
    // {
    //     ImGui.SameLine();
    //     if (ImGuiUtil.IconButton(FontAwesomeIcon.Times))
    //     {
    //         PlaylistSearchString = string.Empty;
    //         RefreshPlaylistSearchResult();
    //     }
    //     ImGuiUtil.ToolTip("Clear");
    // }

    private void DrawFilterPlayedSongsButton()
    {
        (var filterPlayedSongsIcon, Vector4? filterPlayedSongsIconColor, string filterPlayedSongsTooltip) = Plugin.Config.SearchFilterPlayedOption switch
        {
            FilterPlayedSongOptions.ShowAll => (FontAwesomeIcon.Music, null, "Show all songs"),
            FilterPlayedSongOptions.ShowPlayed => (FontAwesomeIcon.Tasks, Plugin.Config.playedSongColor, "Filter played songs"),
            FilterPlayedSongOptions.ShowUnPlayed => (FontAwesomeIcon.ListUl, null, "Filter unplayed songs"),
            _ => (FontAwesomeIcon.Music, (Vector4?)null, "Show all songs")
        };

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(filterPlayedSongsIcon, "##btnFilterPlayedSongs", filterPlayedSongsTooltip, filterPlayedSongsIconColor))
        {
            Plugin.Config.ToggleSearchFilterPlayedOption();
            RefreshPlaylistSearchResult();
        }
    }

    private void DrawSortPlaylistButton()
    {
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.SortAmountDown, "##btnSortPlaylist", "Sort"))
        {
            ImGui.OpenPopup("SortPlaylistContextMenu");
        }

        if (ImGui.BeginPopup("SortPlaylistContextMenu"))
        {
            if (ImGui.MenuItem("Sort by name"))
            {
                Plugin.PlaylistManager.SortBy((song) => song.FileName, descending: !songNameSortDirectionDesc);
                songNameSortDirectionDesc = !songNameSortDirectionDesc;
                RefreshPlaylistSearchResult();
            }

            if (ImGui.MenuItem("Sort by duration"))
            {
                Plugin.PlaylistManager.SortBy((song) => song.SongLength, descending: !songDurationSortDirectionDesc);
                songDurationSortDirectionDesc = !songDurationSortDirectionDesc;
                RefreshPlaylistSearchResult();
            }

            ImGui.EndPopup();
        }
    }

    private void RefreshPlaylistSearchResult()
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
            Plugin.PlaylistManager.FilePathList
            .Select((item, index) => new { Index = index, item.FileName, item.IsFilePlayed })
            .Where((item) =>
            {
                var showPlayedSongsFilterResult = Plugin.Config.SearchFilterPlayedOption switch
                {
                    FilterPlayedSongOptions.ShowAll => item.IsFilePlayed == true || item.IsFilePlayed == false,
                    FilterPlayedSongOptions.ShowPlayed => item.IsFilePlayed == true,
                    FilterPlayedSongOptions.ShowUnPlayed => item.IsFilePlayed == false,
                    _ => item.IsFilePlayed == true || item.IsFilePlayed == false
                };

                var isRegexSearch = Plugin.Config.SearchUseRegex && !RegexError && PlaylistSearchRegex != null;
                var textSearchResult = isRegexSearch ? PlaylistSearchRegex.IsMatch(item.FileName) : item.FileName.ContainsIgnoreCase(PlaylistSearchString);

                return showPlayedSongsFilterResult && textSearchResult;
            })
            .Select(item => item.Index)
            .ToList()
        );
    }
}
