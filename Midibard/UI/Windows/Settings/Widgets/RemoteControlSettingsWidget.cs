using System;
using System.Collections.Generic;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.RemoteControl;
using MidiBard.RemoteSync;
using MidiBard.RemoteSync.Client;
using MidiBard.RemoteSync.Server;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

/// <summary>
/// Settings for everything served by the local listener: the REST/web remote
/// control, the HTTP tunnel, and the Conductor/Follower remote sync.
/// </summary>
public sealed class RemoteControlSettingsWidget : Widget
{
    public override string Title => Language.setting_remote_title;
    public override FontAwesomeIcon Icon => FontAwesomeIcon.NetworkWired;

    // Cached snapshot of active remote-control sessions
    private List<RemoteControlServer.ActiveSession> _cachedSessions = [];
    private RemoteControlServer? _subscribedServer;

    // Remote sync (Conductor/Follower) session cache
    private List<SyncServer.SessionSnapshot> _cachedSyncSessions = [];
    private SyncServer? _subscribedSyncServer;

    public RemoteControlSettingsWidget(WidgetContext ctx) : base(ctx) { }

    public override void Draw()
    {
        var cfg = Context.Plugin.Config;

        //  Remote Control
        ImGui.Text(Language.setting_remote_title);
        ImGuiUtil.HelpMarker(Language.setting_remote_enable_tooltip);

        var remoteEnabled = cfg.RemoteControlEnabled;
        if (ImGui.Checkbox(Language.setting_remote_enable_on_client + "##RemoteControlEnabled", ref remoteEnabled))
        {
            cfg.RemoteControlEnabled = remoteEnabled;
            Context.Plugin.RefreshRemoteControlServer();
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

        // Token row
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

        // Services table
        SyncServerSubscription();
        if (cfg.RemoteControlEnabled)
        {
            DrawServicesTable(cfg);
            ImGui.Spacing();
            DrawConnectedUsersTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawTunnelSection(cfg);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawRemoteSyncSection(cfg);
    }

    // Tunnel
    private void DrawTunnelSection(Configuration cfg)
    {
        ImGui.Text(Language.setting_tunnel_title);
        ImGuiUtil.HelpMarker(Language.setting_tunnel_enable_tooltip);

        var listenerInUse = cfg.RemoteControlEnabled
            || Context.Plugin.RemoteSyncManager.IsConductorActive;
        using (ImRaii.Disabled(!listenerInUse))
        {
            var tunnelEnabled = cfg.TunnelEnabled;
            if (ImGui.Checkbox(Language.setting_tunnel_enable_on_client + "##TunnelEnabled", ref tunnelEnabled))
            {
                cfg.TunnelEnabled = tunnelEnabled;
                Context.Plugin.RefreshTunnelService();
            }
        }

        if (!listenerInUse)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(enable Remote Control or Remote Sync Conductor first)");
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
            if (cfg.TunnelEnabled)
                Context.Plugin.RefreshTunnelService();
        }

        // Preset buttons
        if (ImGui.Button(Language.setting_tunnel_preset_cloudflare + "##TunnelPresetCF"))
        {
            cfg.TunnelCommand = "cloudflared tunnel --url http://localhost:{port}";
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.TunnelEnabled)
                Context.Plugin.RefreshTunnelService();
        }
        ImGui.SameLine();
        if (ImGui.Button(Language.setting_tunnel_preset_ngrok + "##TunnelPresetNgrok"))
        {
            cfg.TunnelCommand = "ngrok http {port}";
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.TunnelEnabled)
                Context.Plugin.RefreshTunnelService();
        }
    }

    // Keep the SessionsChanged subscription in sync with the current server instance.
    private void SyncServerSubscription()
    {
        var current = Context.Plugin.RemoteControlServer;
        if (current == _subscribedServer) return;

        if (_subscribedServer != null)
            _subscribedServer.SessionsChanged -= OnSessionsChanged;

        _subscribedServer = current;

        if (_subscribedServer != null)
        {
            _subscribedServer.SessionsChanged += OnSessionsChanged;
            // Populate cache immediately with the current snapshot.
            _cachedSessions = _subscribedServer.SnapshotActiveSessions();
        }
        else
        {
            _cachedSessions = [];
        }
    }

    private void OnSessionsChanged()
    {
        // Called on a thread-pool thread - snapshot is thread-safe (ConcurrentDictionary read).
        _cachedSessions = _subscribedServer?.SnapshotActiveSessions() ?? [];
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

        // API Docs row
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

        // Tunnel row (only when tunnel enabled)
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

    private void DrawConnectedUsersTable()
    {
        var sessions = _cachedSessions;

        ImGui.Text("Connected Clients");

        using var table = ImRaii.Table("##ConnectedClientsTable", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("IP Address", ImGuiTableColumnFlags.WidthFixed, 200);
        ImGui.TableSetupColumn("User Agent");
        ImGui.TableSetupColumn("Last Active", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        if (sessions.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled("-");
            ImGui.TableNextColumn();
            ImGui.TextDisabled("No active sessions");
            ImGui.TableNextColumn();
            ImGui.TextDisabled("-");
        }
        else
        {
            foreach (var session in sessions)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(session.IpAddress);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(session.UserAgent);

                ImGui.TableNextColumn();
                var idleTime = DateTime.Now - session.LastActive;
                string idleStr = idleTime.TotalSeconds < 60
                    ? $"{(int)idleTime.TotalSeconds}s ago"
                    : $"{(int)idleTime.TotalMinutes}m ago";
                ImGui.TextUnformatted(idleStr);
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

    // Remote Sync (Conductor/Follower)
    // Strings are localized in the final polish phase.
    private void DrawRemoteSyncSection(Configuration cfg)
    {
        var manager = Context.Plugin.RemoteSyncManager;

        ImGui.Text("Remote Sync");
        ImGuiUtil.HelpMarker("""
        Synchronizes playback across multiple game clients in solo mode.
        One Conductor hosts the sync endpoint on the remote - control port; Followers connect to it,
        locally or through the tunnel above.Only one client per machine needs to connect
        """);

        var serverEnabled = cfg.RemoteSyncServerEnabled;
        if (ImGui.Checkbox("Host sync server (Conductor)##RemoteSyncServerEnabled", ref serverEnabled))
        {
            cfg.RemoteSyncServerEnabled = serverEnabled;
            if (serverEnabled)
                cfg.RemoteSyncConnectEnabled = false;
            manager.Refresh();
        }

        var connectEnabled = cfg.RemoteSyncConnectEnabled;
        if (ImGui.Checkbox("Connect to a Conductor (Follower)##RemoteSyncConnectEnabled", ref connectEnabled))
        {
            cfg.RemoteSyncConnectEnabled = connectEnabled;
            if (connectEnabled)
                cfg.RemoteSyncServerEnabled = false;
            manager.Refresh();
        }

        ImGui.Spacing();

        if (cfg.RemoteSyncServerEnabled)
            DrawSyncConductorSection(cfg, manager);
        else
            DrawSyncFollowerSection(cfg, manager);

        ImGui.Spacing();
        DrawSyncSettings(cfg);
    }

    // Shared sync settings (file sync folder, correction thresholds, relay)
    private void DrawSyncSettings(Configuration cfg)
    {
        if (!ImGui.CollapsingHeader("Sync settings##RemoteSyncSettings"))
            return;

        ImGui.Text("Sync folder:");
        ImGuiUtil.HelpMarker(
            "The Conductor pushes MIDI files from this folder; Followers save received " +
            "files here and load it as a temp playlist (never touching saved playlists). " +
            "Clients on the same machine share one folder, so files transfer once per machine.");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 70);
        var folder = cfg.RemoteSyncFolder;
        if (ImGui.InputText("##RemoteSyncFolder", ref folder, 512))
            cfg.RemoteSyncFolder = folder;
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Folder, "##BtnPickSyncFolder", "Change folder"))
            _ = PickSyncFolderAsync();
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.FolderOpen, "##BtnOpenSyncFolder", "Open folder"))
            WindowsApi.OpenFolder(cfg.RemoteSyncFolder);

        ImGui.SetNextItemWidth(120);
        var maxFileMb = cfg.RemoteSyncMaxFileMb;
        if (ImGui.InputInt("Max file size (MB)##RemoteSyncMaxFileMb", ref maxFileMb))
            cfg.RemoteSyncMaxFileMb = Math.Clamp(maxFileMb, 1, 64);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.Spacing();

        ImGui.SetNextItemWidth(120);
        var softMs = cfg.RemoteSyncSoftThresholdMs;
        if (ImGui.InputInt("Ignore drift below (ms)##RemoteSyncSoft", ref softMs))
            cfg.RemoteSyncSoftThresholdMs = Math.Clamp(softMs, 5, 1000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker("Dead zone: drift smaller than this is left alone (avoids constant corrections).");

        ImGui.SetNextItemWidth(120);
        var hardMs = cfg.RemoteSyncHardThresholdMs;
        if (ImGui.InputInt("Hard-seek above (ms)##RemoteSyncHard", ref hardMs))
            cfg.RemoteSyncHardThresholdMs = Math.Clamp(hardMs, 100, 5000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker("Drift at or above this jumps straight to the Conductor's position. In between, playback speed is nudged smoothly.");

        ImGui.SetNextItemWidth(120);
        var nudgePct = (float)(cfg.RemoteSyncMaxSpeedNudge * 100);
        if (ImGui.SliderFloat("Max speed nudge (%)##RemoteSyncNudge", ref nudgePct, 1f, 10f, "%.0f%%"))
            cfg.RemoteSyncMaxSpeedNudge = Math.Clamp(nudgePct / 100.0, 0.01, 0.10);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.SetNextItemWidth(120);
        var intervalMs = cfg.RemoteSyncBroadcastIntervalMs;
        if (ImGui.InputInt("Progress broadcast interval (ms)##RemoteSyncInterval", ref intervalMs))
            cfg.RemoteSyncBroadcastIntervalMs = Math.Clamp(intervalMs, 50, 5000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();

        ImGui.SetNextItemWidth(120);
        var playLeadMs = cfg.RemoteSyncPlayLeadMs;
        if (ImGui.InputInt("Play countdown (ms)##RemoteSyncPlayLead", ref playLeadMs))
            cfg.RemoteSyncPlayLeadMs = Math.Clamp(playLeadMs, 300, 10_000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker(
            "When the Conductor presses play with followers connected, every client " +
            "starts together after this scheduled countdown.");

        ImGui.SetNextItemWidth(200);
        string[] compensationLabels =
        [
            Language.ensemble_comp_mode_none,
            Language.ensemble_comp_mode_manual,
            Language.ensemble_comp_mode_default,
        ];
        if (ImGuiUtil.EnumCombo("Instrument compensation##RemoteSyncCompensation",
                ref cfg.RemoteSyncCompensationMode, labelsOverride: compensationLabels))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker(
            "Set by the Conductor and applied by every follower: notes are delayed by each " +
            "instrument's sample attack time (same tables as Ensemble) so onsets line up audibly. " +
            "Adds a fixed delay below 100 ms to all clients. Followers ignore their own value.");

        if (ImGui.Checkbox("Relay commands to same-machine clients over local IPC##RemoteSyncRelay", ref cfg.RemoteSyncRelayToLocalIpc))
            Context.Plugin.IpcProvider.SyncAllSettings();
        ImGuiUtil.HelpMarker(
            "Gateway mode: only this client connects to the Conductor and re-broadcasts " +
            "everything to the other clients on this machine via local IPC.");
    }

    private async System.Threading.Tasks.Task PickSyncFolderAsync()
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
        Context.Plugin.Ui.FileDialogService.FileDialogManager.OpenFolderDialog(
            "Select Remote Sync Folder",
            (result, path) => tcs.TrySetResult(result && System.IO.Directory.Exists(path) ? path : null),
            Context.Plugin.Config.RemoteSyncFolder);

        var selected = await tcs.Task;
        if (selected == null) return;

        Context.Plugin.Config.RemoteSyncFolder = selected;
        Context.Plugin.IpcProvider.SyncAllSettings();
    }

    private void DrawSyncConductorSection(Configuration cfg, RemoteSyncManager manager)
    {
        var listening = Context.Plugin.RemoteControlServer?.IsListening == true && manager.IsConductorActive;
        if (manager.IsConductorActive && !listening)
        {
            ImGui.TextColored(Style.Colors.Red,
                Context.Plugin.RemoteControlError ?? "Listener failed to start.");
        }

        var localAddress = $"ws://127.0.0.1:{cfg.RemoteControlPort}/ws/sync";
        ImGui.Text("Follower address:");
        ImGui.SameLine();
        ImGui.TextUnformatted(localAddress);
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, "##SyncCopyLocalAddr", "Copy local address"))
            ImGui.SetClipboardText(localAddress);

        var tunnelUrl = Context.Plugin.TunnelService?.PublicUrl;
        if (tunnelUrl != null)
        {
            var tunnelAddress = tunnelUrl
                .Replace("https://", "wss://")
                .Replace("http://", "ws://")
                .TrimEnd('/') + "/ws/sync";
            ImGui.Text("Tunnel address:");
            ImGui.SameLine();
            ImGui.TextUnformatted(tunnelAddress);
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Copy, "##SyncCopyTunnelAddr", "Copy tunnel address"))
                ImGui.SetClipboardText(tunnelAddress);
        }

        ImGui.TextDisabled("Followers authenticate with the remote-control token above.");

        ImGui.Spacing();

        using (ImRaii.Disabled(manager.Server is not { SessionCount: > 0 }))
        {
            if (ImGui.Button("Start all (3s)##SyncStartAll"))
                manager.StartAll();
            ImGui.SameLine();
            if (ImGui.Button("Reload sync folder on followers##SyncReloadFolder"))
                manager.Broadcaster?.NotifySyncFolderReload();
        }
        ImGuiUtil.HelpMarker(
            "Start all: every client (the Conductor included) seeks to the Conductor's " +
            "position and starts at the same scheduled instant (clock-offset compensated). " +
            "Reload: followers rebuild their temp playlist from the sync folder.");

        ImGui.Spacing();
        SyncSyncServerSubscription(manager.Server);
        DrawSyncFollowersTable();
    }

    private void DrawSyncFollowerSection(Configuration cfg, RemoteSyncManager manager)
    {
        ImGui.Text("Conductor address:");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var address = cfg.RemoteSyncServerAddress;
        if (ImGui.InputText("##SyncServerAddress", ref address, 512))
            cfg.RemoteSyncServerAddress = address;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.RemoteSyncConnectEnabled)
                manager.Refresh();
        }

        ImGui.Text("Token:");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 110);
        var token = cfg.RemoteSyncServerToken;
        if (ImGui.InputText("##SyncServerToken", ref token, 256, ImGuiInputTextFlags.Password))
        {
            cfg.RemoteSyncServerToken = token;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
            if (cfg.RemoteSyncConnectEnabled)
                manager.Refresh();
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!cfg.RemoteSyncConnectEnabled))
        {
            if (ImGui.Button("Reconnect##SyncReconnect"))
                manager.Refresh();
        }

        ImGui.Spacing();

        var client = manager.Client;
        var (statusColor, statusText) = client?.State switch
        {
            SyncClientState.Connected => (Style.Colors.Green, client.StatusMessage),
            SyncClientState.Connecting => (Style.Colors.Yellow, client.StatusMessage),
            _ => (Style.Colors.Red, client?.StatusMessage ?? "Disabled"),
        };
        ImGui.Text("Status:");
        ImGui.SameLine();
        ImGui.TextColored(statusColor, statusText);

        if (client is { State: SyncClientState.Connected, LastRttMs: >= 0 })
        {
            ImGui.Text($"RTT: {client.LastRttMs} ms");
            ImGui.SameLine();
            ImGui.Text($"Clock offset: {client.LastOffsetMs:+#;-#;0} ms");
            if (manager.Executor.LastDriftMs is { } drift)
            {
                ImGui.SameLine();
                ImGui.Text($"Drift: {drift:+#;-#;0} ms");
            }
        }

        if (DalamudApi.PartyList.IsInParty())
        {
            ImGui.TextColored(Style.Colors.Yellow,
                "You are in a party. Remote sync is solo-mode only and will not control playback while in a party.");
        }
    }

    private void SyncSyncServerSubscription(SyncServer? current)
    {
        if (current == _subscribedSyncServer)
            return;

        if (_subscribedSyncServer != null)
            _subscribedSyncServer.SessionsChanged -= OnSyncSessionsChanged;

        _subscribedSyncServer = current;

        if (_subscribedSyncServer != null)
        {
            _subscribedSyncServer.SessionsChanged += OnSyncSessionsChanged;
            _cachedSyncSessions = _subscribedSyncServer.SnapshotSessions();
        }
        else
        {
            _cachedSyncSessions = [];
        }
    }

    private void OnSyncSessionsChanged()
    {
        _cachedSyncSessions = _subscribedSyncServer?.SnapshotSessions() ?? [];
    }

    private void DrawSyncFollowersTable()
    {
        ImGui.Text("Connected Followers");

        var conductorPhase = Context.Plugin.RemoteSyncManager.GetLocalGridPhase();
        ImGui.SameLine();
        ImGui.TextDisabled(conductorPhase is { } ownPhase
            ? $"| Conductor solo grid: {ownPhase} ms"
            : "| Conductor solo grid: - (not playing)");
        ImGuiUtil.HelpMarker(
            "Solo performance packets quantize notes into 50 ms slots. Each client's grid has its own\n" +
            "phase, chosen by the game whenever a send session opens (first note after ~2.5 s of silence).\n" +
            "Grid = follower phase on the Conductor's clock; delta = difference from the Conductor (-25..25).\n" +
            "Clients with different phases round the same notes to different slots, which listeners hear as\n" +
            "small timing differences even when Drift is 0.");

        using var table = ImRaii.Table("##SyncFollowersTable", 6,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table) return;

        ImGui.TableSetupColumn("Character");
        ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Drift", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Grid", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Connected", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        var sessions = _cachedSyncSessions;
        if (sessions.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled("No followers connected");
            for (var i = 0; i < 5; i++)
            {
                ImGui.TableNextColumn();
                ImGui.TextDisabled("-");
            }

            return;
        }

        foreach (var session in sessions)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (session.HelloReceived)
                ImGui.TextUnformatted(session.CharacterName);
            else
                ImGui.TextDisabled("(identifying...)");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(session.IpAddress);

            ImGui.TableNextColumn();
            if (session.State != null)
                ImGui.TextUnformatted(session.State);
            else
                ImGui.TextDisabled("-");

            ImGui.TableNextColumn();
            if (session.DriftMs is { } drift)
            {
                var color = Math.Abs(drift) >= Context.Plugin.Config.RemoteSyncHardThresholdMs
                    ? Style.Colors.Red
                    : Math.Abs(drift) >= Context.Plugin.Config.RemoteSyncSoftThresholdMs
                        ? Style.Colors.Yellow
                        : Style.Colors.Green;
                ImGui.TextColored(color, $"{drift:+#;-#;0} ms");
            }
            else
            {
                ImGui.TextDisabled("-");
            }

            ImGui.TableNextColumn();
            if (session.SoloGridPhaseMs is { } phase)
            {
                if (conductorPhase is { } reference)
                {
                    var delta = SoloGridTracker.SlotPhaseDelta(phase, reference);
                    var color = Math.Abs(delta) <= 5 ? Style.Colors.Green
                        : Math.Abs(delta) <= 15 ? Style.Colors.Yellow
                        : Style.Colors.Red;
                    ImGui.TextColored(color, $"{phase} ms ({delta:+#;-#;0})");
                }
                else
                {
                    ImGui.TextUnformatted($"{phase} ms");
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Send session #{session.SoloGridSessionId}");
            }
            else
            {
                ImGui.TextDisabled("-");
            }

            ImGui.TableNextColumn();
            var connectedFor = DateTime.Now - session.ConnectedAt;
            ImGui.TextUnformatted(connectedFor.TotalMinutes < 1
                ? $"{(int)connectedFor.TotalSeconds}s"
                : $"{(int)connectedFor.TotalMinutes}m");
        }
    }
}
