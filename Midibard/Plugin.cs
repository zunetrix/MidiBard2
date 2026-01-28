using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// using BardMusicPlayer.XIVMIDI;

using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

using Lumina.Excel;
using Lumina.Excel.Sheets;

using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Interaction;

using MidiBard.Control;
using MidiBard.Control.CharacterControl;
using MidiBard.Control.MidiControl;
using MidiBard.Control.MidiControl.PlaybackInstance;
using MidiBard.IPC;
using MidiBard.Managers;
using MidiBard.Managers.Agents;
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
    internal BardPlayback CurrentBardPlayback { get; }
    internal InstrumentSwitcher InstrumentSwitcher { get; }
    internal PartyChatCommand PartyChatCommand { get; }
    internal PlaylistManager PlaylistManager { get; }
    internal FilePlayback FilePlayback { get; }
    internal MidiPlayerControl MidiPlayerControl { get; }
    internal static PartyWatcher PartyWatcher;



    // internal PluginUI Ui { get; set; }
    internal static AgentMetronome AgentMetronome { get; set; }
    internal static AgentPerformance AgentPerformance { get; set; }
    internal static IPCManager IpcManager { get; set; }
    internal static PluginIPC PluginIpc { get; set; }
    private int configSaverTick;
    private static bool wasEnsembleModeRunning = false;
    internal static ExcelSheet<Perform> InstrumentSheet;
    internal static Instrument[] Instruments;
    internal static Instrument[] Guitars;
    public static string[] InstrumentStrings;
    internal static readonly byte[] guitarGroup = { 24, 25, 26, 27, 28 };
    internal static IDictionary<SevenBitNumber, uint> ProgramInstruments;

    internal static bool SlaveMode = false;
    internal static int CurrentInstrumentWithTone => CurrentInstrument >= 24 ? 24 + CurrentTone : CurrentInstrument;
    internal static unsafe byte CurrentInstrument => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset);
    internal static unsafe byte CurrentTone => *(byte*)(Offsets.PerformanceStructPtr + 3 + Offsets.InstrumentOffset + 1);
    internal static bool PlayingGuitar => InstrumentHelper.IsGuitar(CurrentInstrument);
    internal static bool IsPlaying => CurrentBardPlayback?.IsRunning == true;
    internal static TimeSpan? CurrentPlaybackTime => CurrentBardPlayback?.GetCurrentTime<MetricTimeSpan>().GetTimeSpan();
    internal static TimeSpan? CurrentPlaybackDuration => CurrentBardPlayback?.GetDuration<MetricTimeSpan>().GetTimeSpan();

    public unsafe Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<DalamudApi>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(DalamudApi.PluginInterface);

        DryWetMidiNativeResolver.Register();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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

        MidiFileConfigManager.Init();
        PluginCommandManager = new PluginCommandManager(this);
        Ui = new PluginUi(this);
        // Ui = new PluginUI();
        IpcManager = new IPCManager();
        PartyWatcher = new PartyWatcher();
        PluginIpc = new PluginIPC();
        InputDeviceManager = new InputDeviceManager(this);
        PerformanceEvents = new PerformanceEvents(this);
        CurrentBardPlayback = new BardPlayback(this);
        InstrumentSwitcher = new InstrumentSwitcher(this);
        PartyChatCommand = new PartyChatCommand(this);
        EnsembleManager = new EnsembleManager(this);
        BardPlayDevice = new BardPlayDevice(this);
        MidiPlayerControl = new MidiPlayerControl(this);
        PlaylistManager = new PlaylistManager(this);
        FilePlayback = new FilePlayback(this);



        OffsetManager.Setup(DalamudApi.SigScanner);
        //GuitarTonePatch.InitAndApply();

        var raptureAtkModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule->GetRaptureAtkModule();
        var pAgentPerformanceMetronome = raptureAtkModule->AgentModule.GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.PerformanceMetronome);
        var pAgentPerformance = raptureAtkModule->AgentModule.GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.PerformanceMode);

        AgentMetronome = new AgentMetronome((IntPtr)pAgentPerformanceMetronome);
        AgentPerformance = new AgentPerformance((IntPtr)pAgentPerformance);

        OnLanguageChange(Config.UiLanguage ?? DalamudApi.PluginInterface.UiLanguage);
        DalamudApi.PluginInterface.LanguageChanged += OnLanguageChange;

        DalamudApi.PluginInterface.UiBuilder.Draw += Ui.Draw;
        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += Ui.SettingsWindow.Toggle;
        DalamudApi.PluginInterface.UiBuilder.OpenMainUi += Ui.MainWindow.Toggle;
        DalamudApi.ChatGui.ChatMessage += PartyChatCommand.OnChatMessage;
        DalamudApi.Framework.Update += OnFrameworkUpdate;
        DalamudApi.Framework.Update += LyricsPlayer.Tick;
        // XIVMIDI.Instance.Start();
        // XIVMIDI.Instance.OnRequestFinished += Ui.Instance_RequestFinished;

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

    internal static void SaveConfig()
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
                //ImGuiUtil.AddNotification(NotificationType.Error, "Error when saving config");
            }
        });
    }

    public static void OnLanguageChange(string langCode)
    {
        Language.Culture = new CultureInfo(langCode);
    }

    #region IDisposable Support

    void FreeUnmanagedResources()
    {
        try
        {
            PlaylistManager.CurrentContainer.Save();

            PluginIpc?.Dispose();
            EnsembleManager?.Dispose();
            PartyWatcher?.Dispose();
            IpcManager?.Dispose();
            InputDeviceManager.Dispose();
            PartyChatCommand.Dispose();
            // NetworkManager.Instance.Dispose();

            try
            {
                CurrentBardPlayback?.Stop();
                CurrentBardPlayback?.Dispose();
                CurrentPlayback = null;
            }
            catch (Exception e)
            {
                DalamudApi.PluginLog.Error(e, "error when disposing playback");
            }

            BardPlayDevice?.Dispose();
            //GuitarTonePatch.Dispose();
            PluginCommandManager.Dispose();
            DryWetMidiNativeResolver.Unregister();
            Ui.Dispose();

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
        DalamudApi.ChatGui.ChatMessage -= PartyChatCommand.OnChatMessage;
        DalamudApi.Framework.Update -= OnFrameworkUpdate;
        DalamudApi.Framework.Update -= LyricsPlayer.Tick;
        // XIVMIDI.Instance.OnRequestFinished -= Ui.Instance_RequestFinished;
        // XIVMIDI.Instance.Stop();

        try
        {
            SaveConfig();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "error when saving config file");
        }

        FreeUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~Plugin()
    {
        FreeUnmanagedResources();
    }
    #endregion
}
