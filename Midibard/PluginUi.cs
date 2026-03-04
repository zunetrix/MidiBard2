using System;

using Dalamud.Interface.Windowing;

using MidiBard.Util;

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
    public PianoRollWindow PianoRollWindow { get; }
    public LyricsEditorWindow LyricsEditorWindow { get; }
    public PlaylistWindow PlaylistWindow { get; }
    public SongsWindow SongsWindow { get; }
    public TagsWindow TagsWindow { get; }
    public PlaylistSongEditWindow PlaylistSongEditWindow { get; }
    public SongEditWindow SongEditWindow { get; }
    public BardMusicLibraryWindow BardMusicLibraryWindow { get; }
    public DebugWindow DebugWindow { get; }

    public PluginUi(Plugin plugin)
    {
        Plugin = plugin;
        ThemeManager = new ThemeManager(Plugin.Config.CurrentTheme);
        this.FileDialogService = new FileDialogService(Plugin.Config.PinnedImportFolders);

        MainWindow = this.AddWindow(new MainWindow(Plugin, this));
        SettingsWindow = this.AddWindow(new SettingsWindow(Plugin));
        TrackVisualizerWindow = this.AddWindow(new TrackVisualizerWindow(Plugin));
        PianoRollWindow = this.AddWindow(new PianoRollWindow(Plugin));
        EnsembleWindow = this.AddWindow(new EnsembleWindow(Plugin));
        LyricsEditorWindow = this.AddWindow(new LyricsEditorWindow(Plugin));
        PlaylistWindow = this.AddWindow(new PlaylistWindow(Plugin));
        SongsWindow = this.AddWindow(new SongsWindow(Plugin));
        TagsWindow = this.AddWindow(new TagsWindow(Plugin));
        PlaylistSongEditWindow = this.AddWindow(new PlaylistSongEditWindow(Plugin));
        SongEditWindow = this.AddWindow(new SongEditWindow(Plugin));
        BardMusicLibraryWindow = this.AddWindow(new BardMusicLibraryWindow(Plugin));
        DebugWindow = this.AddWindow(new DebugWindow(Plugin));
    }

    private T AddWindow<T>(T window) where T : Window
    {
        this.WindowSystem.AddWindow(window);
        return window;
    }

    public void Dispose()
    {
        this.WindowSystem.RemoveAllWindows();
    }

    public void Draw()
    {
        // if (!DalamudApi.PlayerState.IsLoaded) return;
        // var player = DalamudApi.ObjectTable.LocalPlayer;
        // if (player == null) return;
        this.ThemeManager.PushThemeStyles();
        this.WindowSystem.Draw();
        this.ThemeManager.PopThemeStyles();
    }

    public void RefreshOpenWindows()
    {
        if (SongsWindow.IsOpen) _ = SongsWindow.LoadSongsAsync();
        if (PlaylistWindow.IsOpen) _ = PlaylistWindow.LoadPlaylistsAsync();
    }
}
