using System;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;

namespace MidiBard;

public partial class PlaylistWindow
{
    private void DrawNewPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("##NewPlaylistPopup");
        if (!popUp) return;

        ImGui.Text("New Playlist");
        ImGui.InputTextWithHint("##NewPlaylistNameInput", "Playlist Name", ref _newPlaylistName, 100);

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonBlueNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonBlueHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonBlueActive))
        {
            if (ImGui.Button("Create"))
            {
                if (!string.IsNullOrWhiteSpace(_newPlaylistName))
                {
                    _ = CreatePlaylistAsync(_newPlaylistName);
                    _newPlaylistName = "";
                }
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private void DrawClearPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("ClearPlaylistPopup");
        if (!popUp) return;

        ImGui.Text("Remove all songs?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This action is irreversible.");
        ImGui.Text($"Are you sure you want to remove all songs from playlist: {_selectedPlaylist?.Name}?");
        ImGui.Text($"The songs will remain in the song collection, they'll simply be detached from the current playlist.");
        ImGui.Text($"This will remove {PlaylistSongs.Count} songs from the playlist.");
        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Button, Style.Components.ButtonDangerNormal)
            .Push(ImGuiCol.ButtonHovered, Style.Components.ButtonDangerHovered)
            .Push(ImGuiCol.ButtonActive, Style.Components.ButtonDangerActive))
        {
            if (ImGui.Button("Clear All Songs"))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    if (_selectedPlaylist != null)
                    {
                        _ = ClearPlaylistAsync(_selectedPlaylist.Id);
                    }
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
    }

    private void DrawEditPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("##EditPlaylistPopup");
        if (!popup) return;

        ImGui.Text("Edit Playlist");

        ImGui.InputTextWithHint("##EditPlaylistNameInput", "Playlist Name", ref _editPlaylistName, 100);

        if (ImGui.Button("Save##SavePlaylistRename"))
        {
            _ = RenameSelectedPlaylistAsync(_editPlaylistName);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##CancelPlaylistRename"))
            ImGui.CloseCurrentPopup();
    }

    private async Task CreatePlaylistAsync(string name)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _messageDisplay.ShowError("Playlist name cannot be empty.");
            return;
        }

        // Fast-path validation to avoid hitting repository unique-index errors.
        if (_playlists.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            _messageDisplay.ShowError("Playlist name is already in use.");
            return;
        }

        var created = await Plugin.PlaylistManager.CreatePlaylistAsync(trimmedName);
        if (created == null)
        {
            // Re-check after failure to cover race conditions where another source created it first.
            await LoadPlaylistsAsync();
            var nowExists = _playlists.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
            _messageDisplay.ShowError(nowExists
                ? "Playlist name is already in use."
                : "Failed to create playlist. Check log for details.");
            return;
        }

        await LoadPlaylistsAsync();
        _messageDisplay.ShowSuccess($"Playlist created: {trimmedName}");
    }

    private async Task DeleteSelectedPlaylistAsync()
    {
        if (_selectedPlaylist == null) return;

        await Plugin.PlaylistManager.DeletePlaylistAsync(_selectedPlaylist.Id);
        _selectedPlaylist = null;
        _songSearchIndexes.Clear();
        await LoadPlaylistsAsync();
    }

    private async Task RenameSelectedPlaylistAsync(string newName)
    {
        if (_selectedPlaylist == null) return;

        var trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _messageDisplay.ShowError("Playlist name cannot be empty.");
            return;
        }

        if (_playlists.Any(p => p.Id != _selectedPlaylist.Id && p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            _messageDisplay.ShowError("Playlist name is already in use.");
            return;
        }

        if (string.Equals(_selectedPlaylist.Name, trimmedName, StringComparison.Ordinal))
            return;

        _selectedPlaylist.Name = trimmedName;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            _messageDisplay.ShowError("Failed to rename playlist. Check log for details.");
            return;
        }

        await LoadPlaylistsAsync();
        _messageDisplay.ShowSuccess($"Playlist renamed to: {trimmedName}");
    }
}
