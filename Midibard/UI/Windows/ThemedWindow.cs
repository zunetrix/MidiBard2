using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MidiBard;

public abstract class ThemedWindow : Window
{
    private IDisposable? _themeScope;

    public bool ApplyTheme { get; set; } = true;

    protected ThemedWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
        : base(name, flags, forceMainWindow)
    {
    }

    public override void PreDraw()
    {
        _themeScope?.Dispose();
        _themeScope = null;

        if (ApplyTheme)
        {
            _themeScope = ThemeManager.PushTheme();
        }

        base.PreDraw();
    }

    public override void PostDraw()
    {
        _themeScope?.Dispose();
        _themeScope = null;

        base.PostDraw();
    }
}
