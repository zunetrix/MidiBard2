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

        ImGui.Text(Language.playlist_new_name);
        ImGui.InputTextWithHint("##NewPlaylistNameInput", Language.playlist_input_hint_name, ref _newPlaylistName, 100);

        if (ImGuiUtil.PrimaryButton(Language.common_action_create))
        {
            if (!string.IsNullOrWhiteSpace(_newPlaylistName))
            {
                _ = CreatePlaylistAsync(_newPlaylistName);
                _newPlaylistName = "";
            }
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGuiUtil.DangerButton(Language.common_action_cancel))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawClearPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("ClearPlaylistPopup");
        if (!popUp) return;

        ImGui.Text(Language.playlist_clear_title);
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, Language.playlist_clear_warning);
        ImGui.Text(string.Format(Language.playlist_clear_confirm, _selectedPlaylist?.Name));
        ImGui.Text(Language.playlist_clear_desc);
        ImGui.Text(string.Format(Language.playlist_clear_count, PlaylistSongs.Count));
        ImGui.Spacing();
        if (ImGuiUtil.DangerButton(Language.playlist_clear_btn))
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
        ImGuiUtil.ToolTip(Language.common_tooltip_confirm);

        ImGui.SameLine();
        if (ImGui.Button(Language.common_action_cancel))
            ImGui.CloseCurrentPopup();
    }

    private void DrawEditPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("##EditPlaylistPopup");
        if (!popup) return;

        ImGui.Text(Language.playlist_edit_title);

        ImGui.InputTextWithHint("##EditPlaylistNameInput", Language.playlist_input_hint_name, ref _editPlaylistName, 100);

        if (ImGuiUtil.SuccessButton($"{Language.common_action_save}##SavePlaylistRename"))
        {
            _ = RenameSelectedPlaylistAsync(_editPlaylistName);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton($"{Language.common_action_cancel}##CancelPlaylistRename"))
            ImGui.CloseCurrentPopup();
    }

    private async Task CreatePlaylistAsync(string name)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            _messageDisplay.ShowError(Language.playlist_err_empty_name);
            return;
        }

        // Fast-path validation to avoid hitting repository unique-index errors.
        if (_playlists.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            _messageDisplay.ShowError(Language.playlist_err_name_in_use);
            return;
        }

        var created = await Plugin.PlaylistManager.CreatePlaylistAsync(trimmedName);
        if (created == null)
        {
            // Re-check after failure to cover race conditions where another source created it first.
            await LoadPlaylistsAsync();
            var nowExists = _playlists.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
            _messageDisplay.ShowError(nowExists
                ? Language.playlist_err_name_in_use
                : Language.playlist_err_create_failed);
            return;
        }

        await LoadPlaylistsAsync();
        _messageDisplay.ShowSuccess(string.Format(Language.playlist_msg_created, trimmedName));
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
            _messageDisplay.ShowError(Language.playlist_err_empty_name);
            return;
        }

        if (_playlists.Any(p => p.Id != _selectedPlaylist.Id && p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            _messageDisplay.ShowError(Language.playlist_err_name_in_use);
            return;
        }

        if (string.Equals(_selectedPlaylist.Name, trimmedName, StringComparison.Ordinal))
            return;

        _selectedPlaylist.Name = trimmedName;
        _selectedPlaylist.UpdatedAt = DateTime.UtcNow;

        var updated = await Plugin.PlaylistManager.UpdatePlaylistAsync(_selectedPlaylist);
        if (!updated)
        {
            _messageDisplay.ShowError(Language.playlist_err_rename_failed);
            return;
        }

        await LoadPlaylistsAsync();
        _messageDisplay.ShowSuccess(string.Format(Language.playlist_msg_renamed, trimmedName));
    }
}
