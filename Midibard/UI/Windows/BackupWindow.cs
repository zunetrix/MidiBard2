using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public class BackupWindow : Window
{
    private Plugin Plugin { get; }

    private List<FileInfo> _backupFiles = new();
    private string _pendingRestorePath = string.Empty;
    private bool _openRestoreConfirmPopup;
    private readonly ImGuiMessageDisplay _messageDisplay = new();

    public BackupWindow(Plugin plugin) : base($"{Plugin.Name} Backup###BackupWindow")
    {
        Plugin = plugin;
        Size = ImGuiHelpers.ScaledVector2(500, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(380, 300),
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();
        LoadBackupList();
    }

    public override void Draw()
    {
        _messageDisplay.Draw();

        DrawFolderSection();
        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);

        DrawBackupControls();
        ImGuiHelpers.ScaledDummy(0, 4);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0, 4);

        DrawBackupList();

        // Must be outside any child window to avoid popup ID stack mismatch
        DrawRestoreConfirmPopup();
    }

    private void DrawFolderSection()
    {
        ImGui.Text("Backup Folder:");
        ImGuiHelpers.ScaledDummy(0, 2);

        float buttonWidth = Style.Dimensions.PlayerButton.X * 2 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(-buttonWidth - ImGui.GetStyle().ItemSpacing.X);
        var folder = Plugin.Config.DefaultBackupFolder;
        ImGui.InputText("##BackupFolderPath", ref folder, 512, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Folder, "##BtnPickBackupFolder", "Pick backup folder", size: Style.Dimensions.PlayerButton))
            _ = PickFolderAsync();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenBackupFolder", Language.open_folder, size: Style.Dimensions.PlayerButton))
            WindowsApi.OpenFolder(Plugin.Config.DefaultBackupFolder);
    }

    private void DrawBackupControls()
    {
        ImGui.Text("Max Backups:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 80);
        var maxCount = Plugin.Config.MaxBackupCount;
        if (ImGui.InputInt("##MaxBackupCount", ref maxCount))
        {
            if (maxCount < 1) maxCount = 1;
            Plugin.Config.MaxBackupCount = maxCount;
            Plugin.SaveConfig();
            Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.SameLine();
        if (ImGui.Button("Create Backup##CreateBackupBtn"))
            _ = CreateBackupAsync();
    }

    private void DrawBackupList()
    {
        ImGui.Text($"Backups ({_backupFiles.Count}):");
        ImGuiHelpers.ScaledDummy(0, 2);

        if (ImGui.BeginChild("##BackupListScrollable", new Vector2(0, ImGuiHelpers.GlobalScale * 200), true))
        {
            if (_backupFiles.Count == 0)
            {
                ImGui.TextDisabled("No backups found.");
            }
            else
            {
                foreach (var file in _backupFiles)
                {
                    ImGui.PushID(file.FullName);

                    if (ImGui.SmallButton("Restore"))
                    {
                        _pendingRestorePath = file.FullName;
                        _openRestoreConfirmPopup = true;
                    }

                    ImGui.SameLine();
                    ImGui.Text(file.Name);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({FormatFileSize(file.Length)})");

                    ImGui.PopID();
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawRestoreConfirmPopup()
    {
        if (_openRestoreConfirmPopup)
        {
            ImGui.OpenPopup("##RestoreConfirmPopup");
            _openRestoreConfirmPopup = false;
        }

        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(360, 0));
        if (!ImGui.BeginPopup("##RestoreConfirmPopup")) return;

        ImGui.Text("Restore Backup?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This will replace the current database.");
        ImGui.TextWrapped($"File: {Path.GetFileName(_pendingRestorePath)}");
        ImGui.Text("All connected clients will be briefly disconnected.");
        ImGui.Spacing();

        if (ImGui.Button("Restore##RestoreConfirmBtn"))
        {
            _ = RestoreBackupAsync(_pendingRestorePath);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##RestoreCancelBtn"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private async Task PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select Backup Folder",
            (result, path) => tcs.TrySetResult(result && Directory.Exists(path) ? path : null),
            Plugin.Config.DefaultBackupFolder);

        var selected = await tcs.Task;
        if (selected == null) return;

        Plugin.Config.DefaultBackupFolder = selected;
        Plugin.SaveConfig();
        Plugin.IpcProvider.SyncAllSettings();
        LoadBackupList();
    }

    private async Task CreateBackupAsync()
    {
        var dbPath = GetDatabasePath();
        if (!File.Exists(dbPath))
        {
            _messageDisplay.ShowError("Database file not found.");
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Plugin.Config.DefaultBackupFolder);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupPath = Path.Combine(Plugin.Config.DefaultBackupFolder, $"midibard-{timestamp}.db");
                File.Copy(dbPath, backupPath, overwrite: false);
                DalamudApi.PluginLog.Information($"[Backup] Created: {backupPath}");
                TrimBackups();
            });

            LoadBackupList();
            _messageDisplay.ShowSuccess("Backup created successfully.");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[Backup] Failed to create backup");
            _messageDisplay.ShowError("Backup failed. Check log for details.");
        }
    }

    private async Task RestoreBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            _messageDisplay.ShowError("Backup file not found.");
            return;
        }

        var dbPath = GetDatabasePath();

        // Step 1: disconnect all clients (including self)
        Plugin.IpcProvider.BroadcastDisconnectDatabase();

        // Step 2: wait for connections to close
        await Task.Delay(500);

        // Step 3: copy backup over current database
        try
        {
            File.Copy(backupPath, dbPath, overwrite: true);
            DalamudApi.PluginLog.Information($"[Backup] Restored: {backupPath}");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[Backup] Failed to restore backup");
            _messageDisplay.ShowError("Restore failed. Check log for details.");
            Plugin.IpcProvider.BroadcastReconnectDatabase();
            return;
        }

        // Step 4: reconnect all clients
        Plugin.IpcProvider.BroadcastReconnectDatabase();

        _messageDisplay.ShowSuccess($"Restored from {Path.GetFileName(backupPath)}.");
    }

    private void TrimBackups()
    {
        if (Plugin.Config.MaxBackupCount <= 0) return;
        var files = GetBackupFiles();
        while (files.Count > Plugin.Config.MaxBackupCount)
        {
            try
            {
                File.Delete(files[^1].FullName);
                DalamudApi.PluginLog.Information($"[Backup] Deleted old backup: {files[^1].Name}");
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Warning(ex, "[Backup] Failed to delete old backup");
            }
            files.RemoveAt(files.Count - 1);
        }
    }

    private void LoadBackupList() => _backupFiles = GetBackupFiles();

    private List<FileInfo> GetBackupFiles()
    {
        var dir = Plugin.Config.DefaultBackupFolder;
        if (!Directory.Exists(dir)) return new();
        return new DirectoryInfo(dir)
            .GetFiles("midibard-*.db")
            .OrderByDescending(f => f.Name)
            .ToList();
    }

    private string GetDatabasePath() =>
        Path.Combine(
            Plugin.Config.defaultPlaylistFolder ?? DalamudApi.PluginInterface.ConfigDirectory.FullName,
            "midibard.db");

    private static string FormatFileSize(long bytes) =>
        bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F1} KB"
            : $"{bytes / (1024.0 * 1024):F1} MB";
}
