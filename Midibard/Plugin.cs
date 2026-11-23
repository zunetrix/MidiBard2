using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using BardMusicPlayer.XIVMIDI;

using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using MidiBard.Control;
using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Control.MidiControl.Preview;
using MidiBard.Ipc;
using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Managers;
using MidiBard.Playlist;
using MidiBard.Playlist.Helpers;
using MidiBard.RemoteControl;
using MidiBard.RemoteSync;
using MidiBard.Structs;
using MidiBard.Util;
using MidiBard.Util.Lyrics;
using MidiBard.Resources;

namespace MidiBard;

public class Plugin : IDalamudPlugin
{
    public static string Name => "MidiBard 2";
    internal static readonly Version Version = typeof(Plugin).Assembly.GetName().Version;

    internal Configuration Config { get; }
    internal PluginUi Ui { get; }
    internal PluginCommandManager PluginCommandManager { get; }
    internal BardPlayDevice BardPlayDevice { get; }
    internal EnsembleManager EnsembleManager { get; }
    internal SoloPerformanceMonitor SoloPerformanceMonitor { get; }
    internal InputDeviceManager InputDeviceManager { get; }
    internal PerformanceEvents PerformanceEvents { get; }
    internal BardPlayback CurrentBardPlayback { get; set; }
    internal InstrumentSwitcher InstrumentSwitcher { get; }
    internal ChatWatcher ChatWatcher { get; }
    internal FilePlayback FilePlayback { get; }
    internal MidiPlayerControl MidiPlayerControl { get; }
    internal PlaybackUserActions PlaybackUserActions { get; }
    internal LyricsPlayer LyricsPlayer { get; }
    internal MidiFileConfigManager MidiFileConfigManager { get; }
    internal RemotePlaybackLifecycle RemotePlaybackLifecycle { get; }
    internal RemoteControlStatusMonitor RemoteControlStatusMonitor { get; }
    internal RemoteControlServer? RemoteControlServer { get; private set; }
    internal string? RemoteControlError { get; private set; }
    internal TunnelService? TunnelService { get; private set; }
    internal string? TunnelError { get; private set; }
    internal RemoteSyncManager RemoteSyncManager { get; }
    internal PerformanceSampleProbe PerformanceSampleProbe { get; }
    internal static PartyWatcher PartyWatcher;
    internal IpcProvider IpcProvider { get; }
    internal ServerBarProvider ServerBarProvider { get; }

    // Database
    private static LiteDbContext? Database { get; set; }
    internal PlaylistManager PlaylistManager { get; private set; }

    // private int configSaverTick;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<DalamudApi>();
        InstrumentHelper.Initialize();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(this, DalamudApi.PluginInterface);
        Config.Migrate();

        // TODO: find better way to backup before init database to not lock main thread dalamud now have new async plugin load
        if (Config.BackupOnInit)
        {
            BackupService.TryCreateStartupBackup(Config);
        }

        InitDatabase();

        DryWetMidiNativeResolver.Register();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Offsets.Setup();

        Ui = new PluginUi(this);
        PluginCommandManager = new PluginCommandManager(this);

        // Dalamud.Utility.Util.IsWine()
        var platform = Dalamud.Utility.Util.GetHostPlatform();
        IIpcTransport ipcTransport =
            platform == OSPlatform.Linux || platform == OSPlatform.FreeBSD
                ? new LinuxIpcTransport()
                : new TinyIpcTransport();

        IpcProvider = new IpcProvider(this, ipcTransport);
        // Listeners
        PartyWatcher = new PartyWatcher();
        ChatWatcher = new ChatWatcher(this);
        InputDeviceManager = new InputDeviceManager(this);
        PerformanceEvents = new PerformanceEvents(this);
        PlaylistManager = new PlaylistManager(this);
        CurrentBardPlayback = new BardPlayback(this);
        RemotePlaybackLifecycle = new RemotePlaybackLifecycle();
        RemoteControlStatusMonitor = new RemoteControlStatusMonitor(RemotePlaybackLifecycle.Events);
        InstrumentSwitcher = new InstrumentSwitcher(this);
        EnsembleManager = new EnsembleManager(this);
        EnsembleManager.EnsembleStart += RemotePlaybackLifecycle.OnEnsembleStarted;
        EnsembleManager.EnsembleStopped += RemotePlaybackLifecycle.OnEnsembleStopped;
        SoloPerformanceMonitor = new SoloPerformanceMonitor();
        BardPlayDevice = new BardPlayDevice(this);
        MidiPlayerControl = new MidiPlayerControl(this);
        FilePlayback = new FilePlayback(this);
        LyricsPlayer = new LyricsPlayer(this);
        MidiFileConfigManager = new MidiFileConfigManager(this);
        PlaybackUserActions = new PlaybackUserActions(this);
        PerformanceSampleProbe = new PerformanceSampleProbe();
        RemoteSyncManager = new RemoteSyncManager(this);
        // load last
        ServerBarProvider = new ServerBarProvider(this);
        RefreshRemoteControlServer();

        //GuitarTonePatch.InitAndApply();

        OnLanguageChange(Config.UiLanguage ?? DalamudApi.PluginInterface.UiLanguage);
        DalamudApi.PluginInterface.LanguageChanged += OnLanguageChange;
        DalamudApi.PluginInterface.UiBuilder.Draw += Ui.Draw;
        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += Ui.SettingsWindow.Toggle;
        DalamudApi.PluginInterface.UiBuilder.OpenMainUi += Ui.MainWindow.Toggle;
        DalamudApi.Framework.Update += OnFrameworkUpdate;

        XIVMIDI.Instance.Start();
        XIVMIDI.Instance.OnRequestFinished += Ui.BardMusicLibraryWindow.Instance_RequestFinished;

        if (Config.OpenOnStartup)
        {
            Ui.MainWindow.IsOpen = true;
        }
    }

    private void InitDatabase()
    {
        var dbPath = Path.Combine(Config.defaultPlaylistFolder ?? DalamudApi.PluginInterface.GetPluginConfigDirectory(), "midibard.db");
        var useWineLock = Dalamud.Utility.Util.IsWine();
        ServiceContainer.Clear();

        try
        {
            Database = DatabaseInitializationRetryPolicy.Execute(
                () => new LiteDbContext(dbPath, useWineLock),
                retryEnabled: useWineLock,
                timeout: TimeSpan.FromSeconds(30),
                onRetry: (exception, attempt, delay) =>
                    DalamudApi.PluginLog.Warning(
                        exception,
                        $"[Database] Initialization attempt {attempt} failed; retrying in {delay.TotalMilliseconds:0} ms"));

            ServiceContainer.Initialize(
                Config,
                Database,
                Database.PlaylistRepository,
                Database.SongRepository,
                Database.TagRepository);
            DalamudApi.PluginLog.Information("Database services initialized successfully");
        }
        catch (Exception ex)
        {
            Database?.Dispose();
            Database = null;
            ServiceContainer.Clear();
            DalamudApi.PluginLog.Error(ex, "Failed to initialize database - playlist features unavailable");
        }
    }

    internal void CloseDatabase()
    {
        Database?.Dispose();
        Database = null;
        ServiceContainer.Clear();
        DalamudApi.PluginLog.Information("[Database] Connection closed.");
    }

    internal void ReopenDatabase()
    {
        CloseDatabase();
        InitDatabase();
        Ui.RefreshOpenWindows();
        DalamudApi.PluginLog.Information("[Database] Connection reopened.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        PerformanceEvents.InPerformanceMode = AgentPerformanceMode.IsInPerformanceMode;
        UpdateRemoteControlStatusMonitor();

        // if (Ui.MainWindow.IsOpen)
        // {
        //     if (configSaverTick++ == 3600)
        //     {
        //         configSaverTick = 0;
        //         SaveConfig();
        //     }
        // }

        if (!Config.MonitorOnEnsemble) return;

        EnsembleManager.MonitorEnsembleState();

        if (AgentPerformanceMode.IsInPerformanceMode)
        {
            Playlib.ConfirmReceiveReadyCheck();
        }
    }

    private void UpdateRemoteControlStatusMonitor()
    {
        var player = PlaybackControlAvailability.GetPlayerSnapshot();
        var playlist = PlaylistManager.CurrentPlaylist;

        RemoteControlStatusMonitor.Observe(new RemoteControlStatusFingerprint(
            player.PlayerLoaded,
            player.ClassJobId,
            DalamudApi.PartyList.IsInParty(),
            DalamudApi.PartyList.IsPartyLeader(),
            Config.MonitorOnEnsemble,
            Config.SyncClients,
            Config.PlayMode,
            playlist?.Id,
            playlist?.Name ?? string.Empty,
            playlist?.IsTemp ?? false));
    }

    internal string RemoteControlStatus =>
        !Config.RemoteControlEnabled
            ? "Disabled"
            : RemoteControlServer?.IsListening == true
                ? $"Listening on localhost:{Config.RemoteControlPort}"
                : RemoteControlError ?? "Unavailable";

    internal string TunnelStatus =>
        !Config.TunnelEnabled
            ? Language.setting_tunnel_status_stopped
            : TunnelService?.Status switch
            {
                RemoteControl.TunnelStatus.Starting => Language.setting_tunnel_status_starting,
                RemoteControl.TunnelStatus.Running => Language.setting_tunnel_status_running,
                RemoteControl.TunnelStatus.Error => TunnelError ?? Language.setting_tunnel_status_error,
                _ => TunnelError ?? Language.setting_tunnel_status_error,
            };

    internal void RefreshRemoteControlServer()
    {
        RemoteControlServer?.Dispose();
        RemoteControlServer = null;
        RemoteControlError = null;

        // The listener serves both the REST remote control and the /ws/sync
        // endpoint used by the conductor role of remote sync.
        var syncConductorActive = RemoteSyncManager?.IsConductorActive == true;
        if (!Config.RemoteControlEnabled && !syncConductorActive)
            return;

        if (string.IsNullOrWhiteSpace(Config.RemoteControlToken))
        {
            Config.RemoteControlToken = GenerateRemoteControlToken();
            SaveConfig();
        }

        RemoteControlServer? server = null;
        try
        {
            server = new RemoteControlServer(
                new RemoteControlService(this),
                Config.RemoteControlPort,
                Config.RemoteControlToken,
                restEnabled: Config.RemoteControlEnabled,
                wsSyncHandler: syncConductorActive ? RemoteSyncManager.HandleWebSocketAsync : null);
            server.Start();
            RemoteControlServer = server;
            DalamudApi.PluginLog.Information(
                $"[RemoteControl] Listening on localhost:{Config.RemoteControlPort}");
        }
        catch (SocketException socketEx) when (
            socketEx.SocketErrorCode == SocketError.AddressAlreadyInUse ||
            socketEx.SocketErrorCode == SocketError.AccessDenied)
        {
            RemoteControlError = Language.setting_remote_port_in_use;
            server?.Dispose();
            RemoteControlServer = null;
            DalamudApi.PluginLog.Warning(
                socketEx,
                $"[RemoteControl] Port {Config.RemoteControlPort} is already in use.");
        }
        catch (Exception exception)
        {
            RemoteControlError = exception.Message;
            server?.Dispose();
            RemoteControlServer = null;
            DalamudApi.PluginLog.Warning(
                exception,
                "[RemoteControl] Failed to start listener; MidiBard will continue without remote control.");
        }
    }

    internal void RefreshTunnelService()
    {
        TunnelService?.Dispose();
        TunnelService = null;
        TunnelError = null;

        // The tunnel is useful for either feature sharing the listener: the REST
        // remote control or a remote-sync Conductor reachable from the internet.
        var listenerInUse = Config.RemoteControlEnabled || RemoteSyncManager?.IsConductorActive == true;
        if (!Config.TunnelEnabled || !listenerInUse)
            return;

        if (string.IsNullOrWhiteSpace(Config.TunnelCommand))
            return;

        try
        {
            var svc = RemoteControl.TunnelService.TryStart(Config.TunnelCommand, Config.RemoteControlPort);
            if (svc == null)
            {
                TunnelError = Language.setting_remote_port_in_use;
                DalamudApi.PluginLog.Information("[Tunnel] Skipped - another instance is already running the tunnel.");
            }
            else
            {
                TunnelService = svc;
                DalamudApi.PluginLog.Information("[Tunnel] Started.");
            }
        }
        catch (Exception exception)
        {
            TunnelError = exception.Message;
            DalamudApi.PluginLog.Warning(exception, "[Tunnel] Failed to start tunnel process.");
        }
    }

    internal void RegenerateRemoteControlToken()
    {
        Config.RemoteControlToken = GenerateRemoteControlToken();
        SaveConfig();
        RefreshRemoteControlServer();
    }

    private static string GenerateRemoteControlToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    internal void SaveConfig()
    {
        var startNew = Stopwatch.StartNew();
        Task.Run(() =>
        {
            try
            {
                Config.Save();
                DalamudApi.PluginLog.Verbose($"config saved in {startNew.Elapsed.TotalMilliseconds}ms");
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Warning($"error when saving config {e.Message}");
            }
        });
    }

    public static void OnLanguageChange(string langCode)
    {
        Language.Culture = new CultureInfo(langCode);
    }

    void FreeUnmanagedResources()
    {
        try
        {
            try
            {
                CurrentBardPlayback?.Stop();
                CurrentBardPlayback?.Dispose();
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error when disposing playback");
            }

#if DEBUG
            // Testhooks.Instance?.Dispose();
#endif
        }
        catch (Exception e2)
        {
            DalamudApi.PluginLog.Error(e2, "error when disposing midibard");
        }
    }

    public void Dispose()
    {
        DalamudApi.PluginInterface.LanguageChanged -= OnLanguageChange;
        DalamudApi.PluginInterface.UiBuilder.Draw -= Ui.Draw;
        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi -= Ui.SettingsWindow.Toggle;
        DalamudApi.PluginInterface.UiBuilder.OpenMainUi -= Ui.MainWindow.Toggle;
        DalamudApi.Framework.Update -= OnFrameworkUpdate;

        RemoteSyncManager.Dispose();
        RemoteControlServer?.Dispose();
        RemoteControlServer = null;
        TunnelService?.Dispose();
        TunnelService = null;

        IpcProvider.Dispose();
        if (EnsembleManager != null)
        {
            EnsembleManager.EnsembleStart -= RemotePlaybackLifecycle.OnEnsembleStarted;
            EnsembleManager.EnsembleStopped -= RemotePlaybackLifecycle.OnEnsembleStopped;
        }
        EnsembleManager?.Dispose();
        SoloPerformanceMonitor?.Dispose();
        PartyWatcher?.Dispose();
        InputDeviceManager.Dispose();
        ChatWatcher.Dispose();
        LyricsPlayer.Dispose();
        BardPlayDevice?.Dispose();
        PerformanceSampleProbe?.Dispose();
        // GuitarTonePatch.Dispose();
        PluginCommandManager.Dispose();
        DryWetMidiNativeResolver.Unregister();
        ServerBarProvider.Dispose();
        Ui.Dispose();

        XIVMIDI.Instance.OnRequestFinished -= Ui.BardMusicLibraryWindow.Instance_RequestFinished; ;
        XIVMIDI.Instance.Stop();
        SaveConfig();

        FreeUnmanagedResources();
        CloseDatabase();
        GC.SuppressFinalize(this);
    }

    ~Plugin()
    {
        FreeUnmanagedResources();
        Database?.Dispose();
    }
}
