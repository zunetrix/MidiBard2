using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;

namespace MidiBard;

public partial class PlaylistWindow
{
    private void DrawSplitter(ref float leftWidth, float minWidth, float maxWidth)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(0, 0)))
        {
            ImGui.InvisibleButton("##PlaylistSplitter", ImGuiHelpers.ScaledVector2(5, -1));
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var io = ImGui.GetIO();
                leftWidth += io.MouseDelta.X;
                leftWidth = MathF.Max(minWidth, MathF.Min(leftWidth, maxWidth));
            }
        }
    }

    private void DrawLeftPanel()
    {
        // fixed header
        using (ImRaii.Group())
        {
            using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
                .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueHovered)
                .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueActive))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Plus, "##NewPlaylistBtn", "New Playlist"))
                {
                    ImGui.OpenPopup("##NewPlaylistPopup");
                }
            }

            // Search playlists
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##PlaylistSearchInput", Language.SearchInputLabel, ref _playlistSearch, 150))
            {
                SearchPlaylists();
            }
            DrawNewPlaylistPopup();
        }
        ImGui.Separator();

        ImGui.Text("Playlists:");
        ImGuiHelpers.ScaledDummy(0, 5);

        // Draw playlist list using indexes
        ImGui.BeginChild("##PlaylistScrolableArea");
        for (var i = 0; i < _playlistSearchIndexes.Count; i++)
        {
            var idx = _playlistSearchIndexes[i];
            var playlist = _playlists[idx];
            if (ImGui.Selectable($"{playlist.Name}##Song_{playlist.Id}", _selectedPlaylist?.Id == playlist.Id))
            {
                _selectedPlaylist = playlist;
                _ = LoadPlaylistSongsAsync(playlist.Id);
            }
        }
        ImGui.EndChild();
    }

    private void DrawRightPanel()
    {
        // Fixed header at top
        using (ImRaii.Group())
        {
            DrawRightPanelHeader();
        }

        // Scrollable content area with songs table
        ImGui.BeginChild("##PlaylistSongsScrollableContent", ImGuiHelpers.ScaledVector2(-1, 0), false);
        DrawRightPanelContent();
        ImGui.EndChild();

        // Popups must be outside BeginChild to work properly
        DrawClearPlaylistPopup();
        DrawEditPlaylistPopup();
    }

    private void DrawRightPanelHeader()
    {
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Bars, "##ShowLeftPanelBtn", "Show/Hide Left Panel"))
        {
            _showPlaylistEditorLeftPanel = !_showPlaylistEditorLeftPanel;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, "##DeletePlaylistBtn", Language.ConfirmInstructionTooltip))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                DeleteSelectedPlaylistAsync();
            }
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Edit, "##EditPlaylistBtn", "Edit Playlist Name"))
        {
            if (_selectedPlaylist != null)
            {
                _editPlaylistName = _selectedPlaylist.Name ?? string.Empty;
                ImGui.OpenPopup("##EditPlaylistPopup");
            }
        }

        ImGui.SameLine();
        // Playlist header with delete button
        ImGui.Text($"Playlist: {_selectedPlaylist?.Name}");
        ImGui.Separator();

        // Import buttons + column visibility button
        DrawMenuButtons();
        ImGui.Separator();

        // Search for songs
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##PlaylistSearchSongInput", Language.SearchInputLabel, ref _songSearch, 250))
        {
            SearchSongs();
        }
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 5);
    }

    private void DrawRightPanelContent()
    {
        DrawSongList();
    }
}
