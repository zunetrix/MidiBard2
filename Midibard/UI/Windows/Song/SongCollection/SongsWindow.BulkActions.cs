using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Resources;
using MidiBard.Playlist;

namespace MidiBard;

public partial class SongsWindow
{
    private void DrawDeleteAllSongsPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("DeleteAllSongsPopup");
        if (!popUp) return;

        ImGui.Text("Delete all songs?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This action is irreversible.");
        ImGui.Text("All song metadata will be permanently lost.");
        ImGui.Text("Songs will also be removed from all playlists.");
        ImGui.Spacing();

        if (ImGuiUtil.DangerButton("Delete All##DeleteAllSongsConfirmBtn"))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                _ = DeleteAllSongsAsync();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGuiUtil.ToolTip(Language.ConfirmInstructionTooltip);

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

    }

    private void DrawAddSelectedSongsToPlaylistPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("AddSelectedSongsToPlaylistPopup");
        if (!popup) return;

        if (_closeAddToPlaylistPopup)
        {
            _closeAddToPlaylistPopup = false;
            ImGui.CloseCurrentPopup();
            return;
        }

        ImGui.Text("Add Selected Songs To Playlist");
        ImGui.Separator();
        ImGui.Text($"Selected songs: {_selectedSongIds.Count}");

        if (_isLoadingPlaylistTargets)
        {
            ImGui.TextDisabled("Loading playlists...");
            return;
        }

        if (_playlistTargets.Count == 0)
        {
            ImGui.TextDisabled("No playlists available.");
            if (ImGui.Button("Reload Playlists"))
                _ = LoadPlaylistTargetsAsync();

            ImGui.SameLine();
            if (ImGui.Button("Cancel##AddToPlaylistCancelEmpty"))
                ImGui.CloseCurrentPopup();
            return;
        }

        var labels = _playlistTargets
            .Select(p => string.IsNullOrWhiteSpace(p.Name) ? $"Playlist #{p.Id}" : p.Name)
            .ToArray();

        if (_selectedPlaylistTargetIndex >= labels.Length)
            _selectedPlaylistTargetIndex = 0;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##AddToPlaylistTargetCombo", ref _selectedPlaylistTargetIndex, labels, 10);

        if (ImGuiUtil.SuccessButton("Add Selected Songs##AddSelectedSongsToPlaylistConfirm"))
            _ = AddSelectedSongsToPlaylistAsync();

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##AddToPlaylistCancel"))
            ImGui.CloseCurrentPopup();
    }

    private async Task LoadPlaylistTargetsAsync()
    {
        _isLoadingPlaylistTargets = true;
        try
        {
            var playlists = await Plugin.PlaylistManager.GetAllPlaylistsAsync();
            _playlistTargets.Clear();
            _playlistTargets.AddRange(playlists.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase));

            if (_selectedPlaylistTargetIndex >= _playlistTargets.Count)
                _selectedPlaylistTargetIndex = 0;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongsWindow] Failed to load playlist targets");
            _messageDisplay.ShowError("Failed to load playlists.");
        }
        finally
        {
            _isLoadingPlaylistTargets = false;
        }
    }

    private async Task AddSelectedSongsToPlaylistAsync()
    {
        if (_selectedSongIds.Count == 0)
        {
            _messageDisplay.ShowError("No songs selected.");
            return;
        }

        if (_playlistTargets.Count == 0 || _selectedPlaylistTargetIndex < 0 || _selectedPlaylistTargetIndex >= _playlistTargets.Count)
        {
            _messageDisplay.ShowError("No target playlist selected.");
            return;
        }

        var target = _playlistTargets[_selectedPlaylistTargetIndex];
        var playlist = await ServiceContainer.PlaylistRepository.GetByIdAsync(target.Id);
        if (playlist == null)
        {
            _messageDisplay.ShowError("Target playlist was not found.");
            return;
        }

        var existingSongIds = playlist.Songs
            .Where(ps => ps.Song?.Id > 0)
            .Select(ps => ps.Song!.Id)
            .ToHashSet();

        var selectedIds = _selectedSongIds.ToList();
        var idsToAdd = selectedIds.Where(id => !existingSongIds.Contains(id)).ToList();

        if (idsToAdd.Count == 0)
        {
            _messageDisplay.ShowError("All selected songs are already in the target playlist.");
            return;
        }

        var ok = await ServiceContainer.PlaylistSongService.BulkAddSongsAsync(target.Id, idsToAdd);
        if (!ok)
        {
            _messageDisplay.ShowError("Failed to add selected songs to playlist.");
            return;
        }

        var skipped = selectedIds.Count - idsToAdd.Count;
        _messageDisplay.ShowSuccess($"Added {idsToAdd.Count} song(s) to '{target.Name}'.{(skipped > 0 ? $" Skipped {skipped} duplicate(s)." : string.Empty)}");

        // Keep other windows/clients in sync.
        Plugin.IpcProvider.LoadPlaylist(target.Id);
        if (Plugin.Ui.PlaylistWindow.IsOpen)
            await Plugin.Ui.PlaylistWindow.LoadPlaylistsAsync();

        _closeAddToPlaylistPopup = true;
    }

    private void DrawBulkTagPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("BulkTagPopup");
        if (!popup) return;

        if (_closeBulkTagPopup)
        {
            _closeBulkTagPopup = false;
            ImGui.CloseCurrentPopup();
            return;
        }

        ImGui.Text("Tag Selected Songs");
        ImGui.Separator();
        ImGui.Text($"Selected songs: {_selectedSongIds.Count}");

        if (_isLoadingTagTargets)
        {
            ImGui.TextDisabled("Loading tags...");
            return;
        }

        if (_tagTargets.Count == 0)
        {
            ImGui.TextDisabled("No tags available. Create tags in the Tags window first.");
            if (ImGui.Button("Reload Tags##BulkTagReload"))
                _ = LoadTagTargetsAsync();

            ImGui.SameLine();
            if (ImGui.Button("Cancel##BulkTagCancelEmpty"))
                ImGui.CloseCurrentPopup();
            return;
        }

        ImGui.Spacing();

        if (ImGui.RadioButton("Add tag##BulkTagAdd", _bulkTagAdd))
            _bulkTagAdd = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Remove tag##BulkTagRemove", !_bulkTagAdd))
            _bulkTagAdd = false;

        ImGui.Spacing();

        var tagNames = _tagTargets.Select(t => t.Name).ToList();
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        ImGuiUtil.DrawComboSearch("##BulkTagTargetCombo", tagNames, ref _selectedTagTargetName, ref _bulkTagComboSearch, 8);

        ImGui.Spacing();

        var actionLabel = _bulkTagAdd ? "Add Tag##BulkTagConfirm" : "Remove Tag##BulkTagConfirm";

        var bulkTagClicked = _bulkTagAdd
            ? ImGuiUtil.SuccessButton(actionLabel)
            : ImGuiUtil.DangerButton(actionLabel);
        if (bulkTagClicked)
            _ = BulkApplyTagAsync();

        ImGui.SameLine();
        if (ImGuiUtil.DangerButton("Cancel##BulkTagCancel"))
            ImGui.CloseCurrentPopup();
    }

    private async Task LoadTagTargetsAsync()
    {
        _isLoadingTagTargets = true;
        try
        {
            var tags = await ServiceContainer.TagService.GetAllAsync();
            _tagTargets.Clear();
            _tagTargets.AddRange(tags);
            if (!_tagTargets.Any(t => t.Name == _selectedTagTargetName))
                _selectedTagTargetName = _tagTargets.Count > 0 ? _tagTargets[0].Name : string.Empty;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[SongsWindow] Failed to load tags for bulk tag popup.");
        }
        finally
        {
            _isLoadingTagTargets = false;
        }
    }

    private async Task BulkApplyTagAsync()
    {
        if (_selectedSongIds.Count == 0)
        {
            _messageDisplay.ShowError("No songs selected.");
            return;
        }

        if (_tagTargets.Count == 0 || string.IsNullOrEmpty(_selectedTagTargetName))
        {
            _messageDisplay.ShowError("No tag selected.");
            return;
        }

        var tag = _tagTargets.FirstOrDefault(t => t.Name == _selectedTagTargetName);
        if (tag == null)
        {
            _messageDisplay.ShowError("Selected tag not found.");
            return;
        }
        var songs = await ServiceContainer.SongService.GetByIdsAsync(_selectedSongIds);

        var modified = new List<Song>();
        int affected = 0;

        foreach (var song in songs)
        {
            if (_bulkTagAdd)
            {
                if (song.Tags.All(t => t.Id != tag.Id))
                {
                    song.Tags.Add(new Tag { Id = tag.Id, Name = tag.Name });
                    modified.Add(song);
                    affected++;
                }
            }
            else
            {
                var existing = song.Tags.FirstOrDefault(t => t.Id == tag.Id);
                if (existing != null)
                {
                    song.Tags.Remove(existing);
                    modified.Add(song);
                    affected++;
                }
            }
        }

        if (modified.Count == 0)
        {
            var verb = _bulkTagAdd ? "already have" : "don't have";
            _messageDisplay.ShowError($"All selected songs {verb} the tag '{tag.Name}'.");
            return;
        }

        await ServiceContainer.SongService.BulkUpdateAsync(modified);

        var action = _bulkTagAdd ? "Added" : "Removed";
        var skipped = songs.Count - affected;
        _messageDisplay.ShowSuccess(
            $"{action} tag '{tag.Name}' on {affected} song(s).{(skipped > 0 ? $" Skipped {skipped} (already {(_bulkTagAdd ? "tagged" : "untagged")})." : string.Empty)}");

        await LoadSongsAsync();
        _closeBulkTagPopup = true;
    }

    private void DrawDeleteSelectedSongsPopup()
    {
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popup = ImRaii.Popup("DeleteSelectedSongsPopup");
        if (!popup) return;

        ImGui.Text($"Delete {_selectedSongIds.Count} selected song(s)?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This action is irreversible.");
        ImGui.Text("All song metadata will be permanently lost.");
        ImGui.Text("Songs will also be removed from all playlists.");
        ImGui.Spacing();

        if (ImGuiUtil.DangerButton("Delete##DeleteSelectedSongsConfirmBtn"))
        {
            _ = DeleteSelectedSongsAsync();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##DeleteSelectedSongsCancelBtn"))
            ImGui.CloseCurrentPopup();
    }
}
