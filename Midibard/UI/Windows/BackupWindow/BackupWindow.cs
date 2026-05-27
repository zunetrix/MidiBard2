using System;
using System.Collections.Generic;
using System.IO;
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
    }

    private void DrawFolderSection()
    {
        ImGui.Text("Backup Folder:");
        ImGuiHelpers.ScaledDummy(0, 2);

        float buttonWidth = Style.Dimensions.ButtonLarge.X * 2 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(-buttonWidth - ImGui.GetStyle().ItemSpacing.X);
        var folder = Plugin.Config.DefaultBackupFolder;
        using (ImRaii.Disabled())
        {
            ImGui.InputText("##BackupFolderPath", ref folder, 512, ImGuiInputTextFlags.ReadOnly);
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Folder, "##BtnPickBackupFolder", "Pick backup folder", size: Style.Dimensions.ButtonLarge))
            _ = PickFolderAsync();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenBackupFolder", Language.common_action_open_folder, size: Style.Dimensions.ButtonLarge))
            WindowsApi.OpenFolder(Plugin.Config.DefaultBackupFolder);

        ImGuiHelpers.ScaledDummy(0, 2);
        var backupOnInit = Plugin.Config.BackupOnInit;
        if (ImGui.Checkbox("Auto backup on startup##BackupOnInit", ref backupOnInit))
        {
            Plugin.Config.BackupOnInit = backupOnInit;
            Plugin.IpcProvider.SyncAllSettings();
        }
    }

    private void DrawBackupControls()
    {
        ImGui.Text("Max Backups:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 80);
        var maxCount = Plugin.Config.MaxBackupCount;
        if (ImGui.InputInt("##MaxBackupCount", ref maxCount, 1, 1, default, ImGuiInputTextFlags.AutoSelectAll))
        {
            if (maxCount < 1) maxCount = 1;
            Plugin.Config.MaxBackupCount = maxCount;
            Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.SameLine();
        if (ImGui.Button("Create Backup##CreateBackupBtn"))
            _ = CreateBackupAsync();
    }

    private void DrawBackupList()
    {
        ImGui.Text($"Backups ({_backupFiles.Count}):");
        ImGui.SameLine();
        if (ImGuiUtil.SuccessIconButton(FontAwesomeIcon.Sync, "##RefreshBackupList", "Refresh"))
        {
            LoadBackupList();
        }

        ImGuiHelpers.ScaledDummy(0, 2);

        using (ImRaii.Child("##BackupListScrollable", new Vector2(0, -1), true))
        {
            if (_backupFiles.Count == 0)
            {
                ImGui.TextDisabled("No backups found.");
            }
            else
            {
                foreach (var file in _backupFiles)
                {
                    if (ImGui.Button($"Restore##RestoreBackup_{file.FullName}"))
                    {
                        _pendingRestorePath = file.FullName;
                        ImGui.OpenPopup("##RestoreConfirmPopup");
                    }

                    ImGui.SameLine();
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, $"##DeleteBackup_{file.FullName}", Language.common_tooltip_confirm))
                    {
                        if (ImGui.GetIO().KeyCtrl)
                        {
                            DeleteBackup(file.FullName);
                        }
                    }

                    ImGui.SameLine();
                    ImGui.Text(file.Name);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({FormatFileSize(file.Length)})");
                }
            }

            // Keep popup draw in the same ID stack scope as OpenPopup calls above.
            DrawRestoreConfirmPopup();
        }
    }

    private void DrawRestoreConfirmPopup()
    {
        ImGui.SetNextWindowSize(ImGuiHelpers.ScaledVector2(360, 0));
        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var popUp = ImRaii.Popup("##RestoreConfirmPopup");
        if (!popUp) return;

        ImGui.Text("Restore Backup?");
        ImGui.Separator();
        ImGui.TextColored(Style.Colors.Red, "This will replace the current database.");
        ImGui.TextWrapped($"File: {Path.GetFileName(_pendingRestorePath)}");
        ImGui.Text("All connected clients will be briefly disconnected.");
        ImGui.Spacing();

        if (ImGui.Button("Restore##RestoreConfirmBtn"))
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                _ = RestoreBackupAsync(_pendingRestorePath);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGuiUtil.ToolTip(Language.common_tooltip_confirm);

        ImGui.SameLine();
        if (ImGui.Button("Cancel##RestoreCancelBtn"))
            ImGui.CloseCurrentPopup();
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
        Plugin.IpcProvider.SyncAllSettings();
        LoadBackupList();
    }

    private Task CreateBackupAsync()
    {
        return Task.Run(async () =>
        {
            var dbPath = BackupService.GetDatabasePath(Plugin.Config.defaultPlaylistFolder);
            if (!File.Exists(dbPath))
            {
                _messageDisplay.ShowError("Database file not found.");
                return;
            }

            _messageDisplay.Show("Creating backup...");

            // Step 1: disconnect all clients (including self)
            Plugin.IpcProvider.BroadcastDisconnectDatabase();

            // Step 2: wait for connections to close
            await Task.Delay(3000);

            // Step 3: copy database to backup
            try
            {
                BackupService.CreateBackup(dbPath, Plugin.Config.DefaultBackupFolder, Plugin.Config.MaxBackupCount);
                LoadBackupList();
                _messageDisplay.ShowSuccess("Backup created successfully.");
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, "[Backup] Failed to create backup");
                _messageDisplay.ShowError("Backup failed. Check log for details.");
            }
            finally
            {
                // Step 4: reconnect all clients
                Plugin.IpcProvider.BroadcastReconnectDatabase();
            }
        });
    }

    private async Task RestoreBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            _messageDisplay.ShowError("Backup file not found.");
            return;
        }

        _messageDisplay.Show("Restoring backup...");

        var dbPath = BackupService.GetDatabasePath(Plugin.Config.defaultPlaylistFolder);

        // Step 1: disconnect all clients (including self)
        Plugin.IpcProvider.BroadcastDisconnectDatabase();

        // Step 2: wait for connections to close
        await Task.Delay(3000);

        // Step 3: copy backup over current database
        try
        {
            BackupService.RestoreBackup(backupPath, dbPath);
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

        _messageDisplay.ShowSuccess($"Backup restored from {Path.GetFileName(backupPath)}.");
    }

    private void DeleteBackup(string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                _messageDisplay.ShowError("Backup file not found.");
                return;
            }

            File.Delete(backupPath);
            DalamudApi.PluginLog.Information($"[Backup] Deleted: {backupPath}");
            LoadBackupList();
            _messageDisplay.ShowSuccess($"Deleted {Path.GetFileName(backupPath)}.");
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[Backup] Failed to delete backup");
            _messageDisplay.ShowError("Delete failed. Check log for details.");
        }
    }

    private void LoadBackupList() => _backupFiles = BackupService.GetBackupFiles(Plugin.Config.DefaultBackupFolder);

    private static string FormatFileSize(long bytes) =>
        bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F1} KB"
            : $"{bytes / (1024.0 * 1024):F1} MB";
}
