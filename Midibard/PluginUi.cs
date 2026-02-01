using System;

using Dalamud.Interface.Windowing;

using MidiBard.Util2;

namespace MidiBard;

public class PluginUi : IDisposable
{
    private Plugin Plugin { get; }
    public ThemeManager ThemeManager { get; }
    public FileDialogService FileDialogService { get; }
    private WindowSystem WindowSystem { get; } = new();
    public MainWindow MainWindow { get; }
    public EnsembleWindow EnsembleWindow { get; }
    public SettingsWindow SettingsWindow { get; }
    public TrackVisualizerWindow TrackVisualizerWindow { get; }
    // public LyricsEditorWindow LyricsEditorWindow { get; }
    public BardMusicLibraryWindow BardMusicLibraryWindow { get; }
    public DebugWindow DebugWindow { get; }

    public PluginUi(Plugin plugin)
    {
        Plugin = plugin;
        ThemeManager = new ThemeManager(Plugin.Config.CurrentTheme);
        this.FileDialogService = new FileDialogService(Plugin.Config.PinnedImportFolders);

        MainWindow = AddWindow(new MainWindow(Plugin, this));
        SettingsWindow = AddWindow(new SettingsWindow(Plugin));
        TrackVisualizerWindow = AddWindow(new TrackVisualizerWindow(Plugin));
        EnsembleWindow = AddWindow(new EnsembleWindow(Plugin));
        // LyricsEditorWindow = AddWindow(new LyricsEditorWindow(Plugin));
        BardMusicLibraryWindow = AddWindow(new BardMusicLibraryWindow(Plugin));
        DebugWindow = AddWindow(new DebugWindow(Plugin));
    }

    private T AddWindow<T>(T window) where T : Window
    {
        WindowSystem.AddWindow(window);
        return window;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
    }

    public void Draw()
    {
        // if (!DalamudApi.PlayerState.IsLoaded) return;
        // var player = DalamudApi.ObjectTable.LocalPlayer;
        // if (player == null) return;
        ThemeManager.PushThemeStyles();
        WindowSystem.Draw();
        ThemeManager.PopThemeStyles();
    }
}
