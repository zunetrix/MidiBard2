using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Playlist;
using MidiBard.Resources;

namespace MidiBard;

public class BulkReplaceWindow : Window
{
    private Plugin Plugin { get; }

    private List<Song> _songs = new();
    private string _oldPrefix = string.Empty;
    private string _newPrefix = string.Empty;
    private int _previewCount = -1;
    private bool _isApplying = false;
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    public BulkReplaceWindow(Plugin plugin) : base($"{Plugin.Name} Bulk Replace Path###BulkReplaceWindow")
    {
        Plugin = plugin;
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    public void Open(List<Song> songs)
    {
        ResetState();
        _songs = songs ?? new List<Song>();
        IsOpen = true;
    }

    public override void OnClose()
    {
        ResetState();
        base.OnClose();
    }

    private void ResetState()
    {
        _oldPrefix = string.Empty;
        _newPrefix = string.Empty;
        _previewCount = -1;
        _isApplying = false;
        _songs = new List<Song>();
    }

    public override void Draw()
    {
        if (_isApplying)
            ImGuiUtil.DrawColoredBanner("Applying changes...", Style.Colors.Violet);
        else
            _messageDisplay.Draw();

        ImGui.Text("Old path prefix:");
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 310);
        if (ImGui.InputText("##BulkReplaceOldPrefix", ref _oldPrefix, 500))
            _previewCount = -1;

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnPickOldPrefix", Language.change_folder))
            PickFolder(isOldPrefix: true);

        DrawPathValidation(_oldPrefix, checkExists: false);

        ImGui.Text("New path prefix:");
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 310);
        if (ImGui.InputText("##BulkReplaceNewPrefix", ref _newPrefix, 500))
            _previewCount = -1;

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnPickNewPrefix", Language.change_folder))
            PickFolder(isOldPrefix: false);

        DrawPathValidation(_newPrefix, checkExists: true);

        ImGui.Spacing();

        if (ImGui.Button("Preview##BulkReplacePreview"))
        {
            if (!string.IsNullOrWhiteSpace(_oldPrefix))
            {
                _previewCount = _songs
                    .Count(s => s.FilePath != null && s.FilePath.StartsWith(_oldPrefix, System.StringComparison.OrdinalIgnoreCase));
            }
        }

        if (_previewCount >= 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Style.Colors.Violet, $"{_previewCount} song(s) will be updated.");
        }

        ImGui.Spacing();

        var applyDisabled = string.IsNullOrWhiteSpace(_oldPrefix)
            || string.IsNullOrWhiteSpace(_newPrefix)
            || !IsValidPathInput(_oldPrefix)
            || !IsValidPathInput(_newPrefix)
            || _isApplying;

        using (ImRaii.Disabled(applyDisabled))
        {
            if (ImGui.Button("Apply##BulkReplaceApply"))
            {
                if (ImGui.GetIO().KeyCtrl)
                    _ = ApplyAsync(_oldPrefix, _newPrefix);
            }
            ImGuiUtil.ToolTip("Hold CTRL + Click To Apply");
        }

        ImGui.SameLine();
        if (ImGui.Button("Close##BulkReplaceClose"))
            IsOpen = false;
    }

    private void PickFolder(bool isOldPrefix)
    {
        var current = isOldPrefix ? _oldPrefix : _newPrefix;
        var startPath = Directory.Exists(current) ? current : Plugin.Config.lastOpenedFolderPath;

        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select Folder",
            (result, path) =>
            {
                if (!result || !Directory.Exists(path)) return;
                if (isOldPrefix)
                    _oldPrefix = path;
                else
                    _newPrefix = path;
                _previewCount = -1;
            },
            startPath);
    }

    private async Task ApplyAsync(string oldPrefix, string newPrefix)
    {
        _isApplying = true;
        try
        {
            var count = await ServiceContainer.SongService.BulkReplaceFilePathPrefixAsync(oldPrefix, newPrefix);
            Plugin.Ui.RefreshOpenWindows();
            _previewCount = -1;
            _messageDisplay.ShowSuccess($"Updated {count} song(s).");
        }
        finally
        {
            _isApplying = false;
        }
    }

    private static void DrawPathValidation(string path, bool checkExists)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!IsValidPathInput(path))
        {
            ImGui.TextColored(Style.Colors.Red, "Invalid path.");
            return;
        }
        if (checkExists && !Directory.Exists(path))
            ImGui.TextColored(Style.Colors.Yellow, "Directory not found.");
    }

    private static bool IsValidPathInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { Path.GetFullPath(path); return true; }
        catch { return false; }
    }
}
