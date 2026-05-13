using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
using MidiBard.Managers;
using MidiBard.Playlist;
using MidiBard.Playlist.Helpers;
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
    internal InputDeviceManager InputDeviceManager { get; }
    internal PerformanceEvents PerformanceEvents { get; }
    internal BardPlayback CurrentBardPlayback { get; set; }
    internal InstrumentSwitcher InstrumentSwitcher { get; }
    internal ChatWatcher ChatWatcher { get; }
    internal FilePlayback FilePlayback { get; }
    internal MidiPlayerControl MidiPlayerControl { get; }
    internal LyricsPlayer LyricsPlayer { get; }
    internal MidiFileConfigManager MidiFileConfigManager { get; }
    internal PerformanceSampleProbe PerformanceSampleProbe { get; }
    internal static PartyWatcher PartyWatcher;
    internal IpcProvider IpcProvider { get; }

    // Database
    private static LiteDbContext? Database { get; set; }
    internal PlaylistManager PlaylistManager { get; private set; }

    private int configSaverTick;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<DalamudApi>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(this, DalamudApi.PluginInterface);
        Config.Migrate();

        // TODO: find better way to backup before init database to not lock main thread
        if (Config.BackupOnInit)
        {
            BackupService.TryCreateStartupBackup(Config);
        }

        InitDatabase();

        DryWetMidiNativeResolver.Register();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        InstrumentHelper.Initialize();
        AgentManager.Initialize();
        OffsetManager.Setup();

        Ui = new PluginUi(this);
        PluginCommandManager = new PluginCommandManager(this);
        IpcProvider = new IpcProvider(this, Dalamud.Utility.Util.IsWine() ? new LinuxIpcTransport() : new TinyIpcTransport());
        // Listeners
        PartyWatcher = new PartyWatcher();
        ChatWatcher = new ChatWatcher(this);
        // TODO: refactor to not listen/scan devices if settings is disabled
        InputDeviceManager = new InputDeviceManager(this);
        PerformanceEvents = new PerformanceEvents(this);
        PlaylistManager = new PlaylistManager(this);
        CurrentBardPlayback = new BardPlayback(this);
        InstrumentSwitcher = new InstrumentSwitcher(this);
        EnsembleManager = new EnsembleManager(this);
        BardPlayDevice = new BardPlayDevice(this);
        MidiPlayerControl = new MidiPlayerControl(this);
        FilePlayback = new FilePlayback(this);
        LyricsPlayer = new LyricsPlayer(this);
        MidiFileConfigManager = new MidiFileConfigManager(this);
        PerformanceSampleProbe = new PerformanceSampleProbe();

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
        PerformanceEvents.InPerformanceMode = AgentManager.AgentPerformance.InPerformanceMode;

        if (Ui.MainWindow.IsOpen)
        {
            if (configSaverTick++ == 3600)
            {
                configSaverTick = 0;
                SaveConfig();
            }
        }

        if (!Config.MonitorOnEnsemble) return;

        EnsembleManager.MonitorEnsembleState();

        if (AgentManager.AgentPerformance.InPerformanceMode)
        {
            Playlib.ConfirmReceiveReadyCheck();
        }
    }

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

        IpcProvider.Dispose();
        EnsembleManager?.Dispose();
        PartyWatcher?.Dispose();
        InputDeviceManager.Dispose();
        ChatWatcher.Dispose();
        LyricsPlayer.Dispose();
        BardPlayDevice?.Dispose();
        PerformanceSampleProbe?.Dispose();
        // GuitarTonePatch.Dispose();
        PluginCommandManager.Dispose();
        DryWetMidiNativeResolver.Unregister();
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
