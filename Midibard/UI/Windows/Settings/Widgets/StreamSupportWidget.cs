using System;
using System.IO;
using System.Threading.Tasks;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.RemoteControl;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public sealed class StreamSupportWidget : Widget
{
    public override string Title => Language.setting_stream_title;
    public override FontAwesomeIcon Icon => FontAwesomeIcon.Stream;

    public StreamSupportWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;

        //  Now Playing
        if (ImGui.Checkbox(Language.setting_pref_stream_wite_song_name_to_file, ref cfg.EnableNowPlayingFileOutput))
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.Text(Language.common_output_folder_label);
        var folder = Path.GetDirectoryName(cfg.NowPlayingFilePath) ?? "";
        using (ImRaii.Disabled())
            ImGui.InputText("##NowPlayingFolderPath", ref folder, 512, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Folder, "##BtnPickNowPlayingFolder", Language.common_action_change_folder))
            _ = PickFolderAsync();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenNowPlayingFolder", Language.common_action_open_folder))
            WindowsApi.OpenFolder(Path.GetDirectoryName(cfg.NowPlayingFilePath));

        ImGui.Text(Language.common_file_name_label);
        var fileName = Path.GetFileName(cfg.NowPlayingFilePath);
        ImGui.InputText("##NowPlayingFileName", ref fileName, 256);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var valid = string.IsNullOrWhiteSpace(fileName)
                ? "midibard-now-playing.txt"
                : !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                    ? fileName.TrimEnd('.') + ".txt"
                    : fileName;
            cfg.NowPlayingFilePath = Path.Combine(folder, valid);
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.File, "##BtnOpenNowPlayingFile", Language.common_action_open_file))
            WindowsApi.OpenFile(cfg.NowPlayingFilePath);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        //  Remote Control
        ImGui.Text(Language.setting_remote_title);
        ImGuiUtil.HelpMarker(Language.setting_remote_enable_tooltip);

        var remoteEnabled = cfg.RemoteControlEnabled;
        if (ImGui.Checkbox(Language.setting_remote_enable_on_client + "##RemoteControlEnabled", ref remoteEnabled))
        {
            cfg.RemoteControlEnabled = remoteEnabled;
            Context.Plugin.RefreshRemoteControlServer();
            // If server was just disabled, also stop the tunnel
            if (!remoteEnabled)
                Context.Plugin.RefreshTunnelService();
        }

        ImGui.Text(Language.setting_remote_port_label);
        ImGui.SetNextItemWidth(120);
        var remotePort = cfg.RemoteControlPort;
        if (ImGui.InputInt("##RemoteControlPort", ref remotePort))
        {
            // live clamping while typing
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            cfg.RemoteControlPort = Math.Clamp(remotePort, 1, 65535);
            Context.Plugin.IpcProvider.SyncAllSettings();
            Context.Plugin.RefreshRemoteControlServer();
            Context.Plugin.RefreshTunnelService();
        }

        ImGui.Spacing();

        //  Token row
        ImGui.Text(Language.setting_remote_token_label);
        var token = cfg.RemoteControlToken;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 170);
        using (ImRaii.Disabled())
            ImGui.InputText("##RemoteControlToken", ref token, 256, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(cfg.RemoteControlToken)))
        {
            if (ImGui.Button(Language.setting_remote_copy_token + "##RCTokenCopy"))
                ImGui.SetClipboardText(cfg.RemoteControlToken);
        }

        ImGui.SameLine();
        if (ImGui.Button(Language.setting_remote_regenerate_token + "##RCTokenRegen"))
        {
            Context.Plugin.RegenerateRemoteControlToken();
            Context.Plugin.IpcProvider.SyncAllSettings();
        }

        ImGui.Spacing();

        //  Services table (only when server is enabled)
        if (cfg.RemoteControlEnabled)
        {
            DrawServicesTable(cfg);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Tunnel
        ImGui.Text(Language.setting_tunnel_title);
        ImGuiUtil.HelpMarker(Language.setting_tunnel_enable_tooltip);

        using (ImRaii.Disabled(!cfg.RemoteControlEnabled))
        {
            var tunnelEnabled = cfg.TunnelEnabled;
            if (ImGui.Checkbox(Language.setting_tunnel_enable_on_client + "##TunnelEnabled", ref tunnelEnabled))
            {
                cfg.TunnelEnabled = tunnelEnabled;
                Context.Plugin.RefreshTunnelService();
            }
        }

        if (!cfg.RemoteControlEnabled)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(enable Remote Control first)");
        }

        // Command input + preset buttons
        ImGui.Text(Language.setting_tunnel_command_label);
        ImGuiUtil.HelpMarker(Language.setting_tunnel_command_tooltip);

        var tunnelCmd = cfg.TunnelCommand ?? string.Empty;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##TunnelCommand", ref tunnelCmd, 512))
            cfg.TunnelCommand = tunnelCmd;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.TunnelEnabled && cfg.RemoteControlEnabled)
                Context.Plugin.RefreshTunnelService();
        }

        // Preset buttons
        if (ImGui.Button(Language.setting_tunnel_preset_cloudflare + "##TunnelPresetCF"))
        {
            cfg.TunnelCommand = "cloudflared tunnel --url http://localhost:{port}";
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.TunnelEnabled && cfg.RemoteControlEnabled)
                Context.Plugin.RefreshTunnelService();
        }
        ImGui.SameLine();
        if (ImGui.Button(Language.setting_tunnel_preset_ngrok + "##TunnelPresetNgrok"))
        {
            cfg.TunnelCommand = "ngrok http {port}";
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.TunnelEnabled && cfg.RemoteControlEnabled)
                Context.Plugin.RefreshTunnelService();
        }
    }

    //  Services table
    private void DrawServicesTable(Configuration cfg)
    {
        var plugin = Context.Plugin;

        // Compute URLs
        var serverUrl = $"http://localhost:{cfg.RemoteControlPort}/";
        var accessUrl = string.IsNullOrWhiteSpace(cfg.RemoteControlToken)
            ? serverUrl
            : serverUrl + "#token=" + Uri.EscapeDataString(cfg.RemoteControlToken);
        var docsUrl = serverUrl + "docs/";
        var tunnelUrl = plugin.TunnelService?.PublicUrl;
        var tunnelAccessUrl = string.IsNullOrWhiteSpace(cfg.RemoteControlToken) || tunnelUrl == null
            ? tunnelUrl
            : tunnelUrl + "?token=" + Uri.EscapeDataString(cfg.RemoteControlToken);

        // Server status colour
        var serverStatus = plugin.RemoteControlStatus;
        var serverOk = plugin.RemoteControlServer?.IsListening == true;

        // Tunnel status
        var tunnelStatus = plugin.TunnelStatus;
        var tunnelRunning = plugin.TunnelService?.Status == TunnelStatus.Running;
        var tunnelStarting = plugin.TunnelService?.Status == TunnelStatus.Starting;
        var tunnelEnabled = cfg.TunnelEnabled;

        using var table = ImRaii.Table("##ServicesTable", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table) return;

        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 40);
        ImGui.TableSetupColumn("Service", ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("URL", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableHeadersRow();

        //  Server row
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); DrawStatusIcon(serverStatus, serverOk, false);
        ImGui.TableNextColumn(); ImGui.TextUnformatted(Language.setting_remote_server_url_label);
        ImGui.TableNextColumn(); ImGui.TextUnformatted(serverUrl);
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, "##SrvCopyUrl", Language.setting_remote_copy_url))
            ImGui.SetClipboardText(serverUrl);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "##SrvOpen", Language.setting_remote_open))
            WindowsApi.OpenUrl(accessUrl);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(cfg.RemoteControlToken)))
        {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Link, "##SrvCopyAccess", Language.setting_remote_copy_access_url))
                ImGui.SetClipboardText(accessUrl);
        }

        //  API Docs row
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); DrawStatusIcon(serverStatus, serverOk, false);
        ImGui.TableNextColumn(); ImGui.TextUnformatted(Language.setting_remote_api_docs_label);
        ImGui.TableNextColumn(); ImGui.TextUnformatted(docsUrl);
        ImGui.TableNextColumn();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, "##DocsCopyUrl", Language.setting_remote_copy_url))
            ImGui.SetClipboardText(docsUrl);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "##DocsOpen", Language.setting_remote_open))
            WindowsApi.OpenUrl(docsUrl);

        //  Tunnel row (only when tunnel enabled)
        if (tunnelEnabled)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); DrawStatusIcon(tunnelStatus, tunnelRunning, tunnelStarting);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(Language.setting_tunnel_public_url_label);
            ImGui.TableNextColumn();
            if (tunnelUrl != null)
                ImGui.TextUnformatted(tunnelUrl);
            else
                ImGui.TextDisabled("-");
            ImGui.TableNextColumn();
            using (ImRaii.Disabled(tunnelUrl == null))
            {
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, "##TunCopyUrl", Language.setting_tunnel_copy_url))
                    ImGui.SetClipboardText(tunnelUrl!);
                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "##TunOpen", Language.setting_tunnel_open_url))
                    WindowsApi.OpenUrl(tunnelAccessUrl ?? tunnelUrl!);
                ImGui.SameLine();
                using (ImRaii.Disabled(string.IsNullOrWhiteSpace(cfg.RemoteControlToken)))
                {
                    if (ImGuiUtil.IconButton(FontAwesomeIcon.Link, "##TunCopyAccess", Language.setting_tunnel_copy_access_url))
                        ImGui.SetClipboardText(tunnelAccessUrl!);
                }
            }
        }
    }

    private static void DrawStatusIcon(string tooltip, bool green, bool yellow)
    {
        var (icon, color) = green
            ? (FontAwesomeIcon.Check, Style.Colors.Green)
            : yellow
                ? (FontAwesomeIcon.Sync, Style.Colors.Yellow)
                : (FontAwesomeIcon.Times, Style.Colors.Red);

        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(icon.ToIconString());
            }
        }

        if (ImGui.IsItemHovered())
            ImGuiUtil.ToolTip(tooltip);
    }

    private async Task PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        var currentFolder = Path.GetDirectoryName(Context.Plugin.Config.NowPlayingFilePath) ?? "";

        Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select Now Playing Output Folder",
            (result, path) => tcs.TrySetResult(result && Directory.Exists(path) ? path : null),
            currentFolder);

        var selected = await tcs.Task;
        if (selected == null) return;

        var currentFileName = Path.GetFileName(Context.Plugin.Config.NowPlayingFilePath);
        Context.Plugin.Config.NowPlayingFilePath = Path.Combine(selected, currentFileName);
        Context.Plugin.IpcProvider.SyncAllSettings();
    }
}
