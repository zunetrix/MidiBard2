using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BardMusicPlayer.XIVMIDI;

using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

using Lumina.Excel;
using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;

using MidiBard.Control;
using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.Ipc;
using MidiBard.Managers;
using MidiBard.Managers.Agents;
using MidiBard.Playlist;
using MidiBard.Util;
using MidiBard.Util.Lyrics;
using MidiBard.Resources;

namespace MidiBard;

public class Plugin : IDalamudPlugin
{
    public static string Name => "MidiBard 2";
    internal static readonly Version Version = typeof(Plugin).Assembly.GetName().Version;
    internal static readonly string VersionString = Version?.ToString();
    internal Configuration Config { get; }
    internal PluginUi Ui { get; }
    internal PluginCommandManager PluginCommandManager { get; }
    internal BardPlayDevice BardPlayDevice { get; }
    internal EnsembleManager EnsembleManager { get; }
    internal InputDeviceManager InputDeviceManager { get; }
    internal PerformanceEvents PerformanceEvents { get; }
    internal BardPlayback CurrentBardPlayback { get; set; }
    internal InstrumentSwitcher InstrumentSwitcher { get; }
    internal PartyChatCommand PartyChatCommand { get; }
    internal FilePlayback FilePlayback { get; }
    internal MidiPlayerControl MidiPlayerControl { get; }
    internal LyricsPlayer LyricsPlayer { get; }
    internal MidiFileConfigManager MidiFileConfigManager { get; }
    internal static PartyWatcher PartyWatcher;
    internal IpcProvider IpcProvider { get; }
    internal static AgentMetronome AgentMetronome { get; set; }
    internal static AgentPerformance AgentPerformance { get; set; }
    internal static PluginIPC PluginIpc { get; set; }

    // Database
    internal static LiteDbInitializer? Database { get; private set; }
    internal PlaylistManager? PlaylistManager { get; private set; }

    private int configSaverTick;
    private static bool wasEnsembleModeRunning = false;
    // TODO: move to instrumentHelper
    internal static ExcelSheet<Perform> InstrumentSheet;
    // TODO: move to instrumentHelper
    internal static Instrument[] Instruments;
    // TODO: move to instrumentHelper
    internal static Instrument[] Guitars;
    // TODO: move to instrumentHelper
    public static string[] InstrumentStrings;
    // TODO: move to instrumentHelper
    internal static readonly byte[] guitarGroup = { 24, 25, 26, 27, 28 };
    // TODO: move to instrumentHelper
    internal static IDictionary<SevenBitNumber, uint> ProgramInstruments;
    internal static bool SlaveMode = false;
    internal static int CurrentInstrumentWithTone => CurrentInstrument >= 24 ? 24 + CurrentTone : CurrentInstrument;
    public static unsafe byte CurrentInstrument => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset);
    internal static unsafe byte CurrentTone => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset + 1);
    internal bool PlayingGuitar => InstrumentHelper.IsGuitar(CurrentInstrument);

    public unsafe Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<DalamudApi>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(DalamudApi.PluginInterface);

        // Initialize database
        var dbPath = Path.Combine(Config.defaultPlaylistFolder ?? DalamudApi.PluginInterface.GetPluginConfigDirectory(), "midibard.db");
        Database = new LiteDbInitializer(dbPath);
        var songRepo = new LiteDbSongRepository(Database.Database);
        var playlistRepo = new LiteDbPlaylistRepository(Database.Database);

        // Register services in Container
        ServiceContainer.Register<ISongRepository>(songRepo);
        ServiceContainer.Register<IPlaylistRepository>(playlistRepo);

        PlaylistManager = new PlaylistManager(this);

        // Lock after all registrations
        ServiceContainer.Lock();

        DryWetMidiNativeResolver.Register();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // TODO: move to instrumentHelper
        InstrumentSheet = DalamudApi.DataManager.Excel.GetSheet<Perform>();
        Instruments = InstrumentSheet!
            .Where(i => !string.IsNullOrWhiteSpace(i.Instrument.ToDalamudString().TextValue) || i.RowId == 0)
            .Select(i => new Instrument(i))
            .ToArray();

        Guitars = Instruments.Where(i => i.IsGuitar).ToArray();
        InstrumentStrings = Instruments.Select(i => i.InstrumentString).ToArray();

        ProgramInstruments = new Dictionary<SevenBitNumber, uint>();
        foreach (var (programNumber, instrument) in Instruments.Select((i, index) => (i.ProgramNumber, index)))
        {
            ProgramInstruments[programNumber] = (uint)instrument;
        }

        var raptureAtkModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetRaptureAtkModule();
        var pAgentPerformanceMetronome = raptureAtkModule->AgentModule.GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.PerformanceMetronome);
        var pAgentPerformance = raptureAtkModule->AgentModule.GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.PerformanceMode);

        AgentMetronome = new AgentMetronome((IntPtr)pAgentPerformanceMetronome);
        AgentPerformance = new AgentPerformance((IntPtr)pAgentPerformance);
        OffsetManager.Setup();

        Ui = new PluginUi(this);
        PluginCommandManager = new PluginCommandManager(this);
        IpcProvider = new IpcProvider(this);
        PartyWatcher = new PartyWatcher();
        PluginIpc = new PluginIPC();
        // TODO: refactor to not listen/scan devices if settings is disabled
        InputDeviceManager = new InputDeviceManager(this);
        PerformanceEvents = new PerformanceEvents(this);
        CurrentBardPlayback = new BardPlayback(this);
        InstrumentSwitcher = new InstrumentSwitcher(this);
        PartyChatCommand = new PartyChatCommand(this);
        EnsembleManager = new EnsembleManager(this);
        BardPlayDevice = new BardPlayDevice(this);
        MidiPlayerControl = new MidiPlayerControl(this);
        FilePlayback = new FilePlayback(this);
        LyricsPlayer = new LyricsPlayer(this);
        MidiFileConfigManager = new MidiFileConfigManager(this);

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

    private void OnFrameworkUpdate(IFramework framework)
    {
        PerformanceEvents.InPerformanceMode = AgentPerformance.InPerformanceMode;

        if (Ui.MainWindow.IsOpen)
        {
            if (configSaverTick++ == 3600)
            {
                configSaverTick = 0;
                SaveConfig();
            }
        }

        if (!Config.MonitorOnEnsemble) return;

        if (wasEnsembleModeRunning)
        {
            if (!AgentMetronome.EnsembleModeRunning || !AgentPerformance.InPerformanceMode)
            {
                EnsembleManager.InvokeEnsembleStop();
                if (Config.StopPlayingWhenEnsembleEnds)
                {
                    MidiPlayerControl.Pause();
                }
            }
        }

        wasEnsembleModeRunning = AgentMetronome.EnsembleModeRunning && AgentPerformance.InPerformanceMode;

        if (AgentPerformance.InPerformanceMode)
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
                DalamudApi.PluginInterface.SavePluginConfig(Config);
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
            Testhooks.Instance?.Dispose();
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
        PluginIpc?.Dispose();
        EnsembleManager?.Dispose();
        PartyWatcher?.Dispose();
        InputDeviceManager.Dispose();
        PartyChatCommand.Dispose();
        LyricsPlayer.Dispose();
        BardPlayDevice?.Dispose();
        // GuitarTonePatch.Dispose();
        PluginCommandManager.Dispose();
        DryWetMidiNativeResolver.Unregister();
        Ui.Dispose();

        XIVMIDI.Instance.OnRequestFinished -= Ui.BardMusicLibraryWindow.Instance_RequestFinished; ;
        XIVMIDI.Instance.Stop();
        SaveConfig();

        FreeUnmanagedResources();
        Database?.Dispose();
        GC.SuppressFinalize(this);
    }

    ~Plugin()
    {
        FreeUnmanagedResources();
        Database?.Dispose();
    }
}
