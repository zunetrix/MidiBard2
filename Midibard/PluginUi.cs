using System;

using Dalamud.Interface.Windowing;

namespace MidiBard;

public class PluginUi : IDisposable
{
    private Plugin Plugin { get; }

    private WindowSystem WindowSystem { get; } = new();
    public MainWindow MainWindow { get; }
    public SettingsWindow SettingsWindow { get; }
    public TrackVisualizerWindow TrackVisualizerWindow { get; }
    public DebugWindow DebugWindow { get; }

    public PluginUi(Plugin plugin)
    {
        Plugin = plugin;

        MainWindow = AddWindow(new MainWindow(Plugin, this));
        SettingsWindow = AddWindow(new SettingsWindow(Plugin));
        TrackVisualizerWindow = AddWindow(new TrackVisualizerWindow(Plugin));

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

        WindowSystem.Draw();
    }
}
