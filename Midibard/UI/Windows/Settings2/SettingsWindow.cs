using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace MidiBard;

public class SettingsWindow2 : Window
{
    private Plugin Plugin { get; }
    private readonly WidgetContext _widgetContext;
    private readonly WidgetManager _widgetManager = new();
    private int _selectedIndex;

    public SettingsWindow2(Plugin plugin) : base($"Settings###SettingsWindow2")
    {
        Plugin = plugin;
        _widgetContext = new WidgetContext(plugin);

        Size = ImGuiHelpers.ScaledVector2(650, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = ImGuiHelpers.ScaledVector2(500, 300) };

        _widgetManager.Add(() => new GeneralSettingsWidget(_widgetContext));
        _widgetManager.Add(() => new AppearanceSettingsWidget(_widgetContext));
        _widgetManager.Add(() => new InterfaceSettingsWidget(_widgetContext));
        _widgetManager.Add(() => new PerformanceSettingsWidget(_widgetContext));
        _widgetManager.Add(() => new EnsembleSettingsWidget(_widgetContext));
        _widgetManager.Add(() => new ChatLyricsSettingsWidget(_widgetContext));
        _widgetManager.Add(() => new ObsSupportWidget(_widgetContext));
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.None;
        if (!Plugin.Config.AllowMovement) Flags |= ImGuiWindowFlags.NoMove;
        if (!Plugin.Config.AllowResize) Flags |= ImGuiWindowFlags.NoResize;
        base.PreDraw();
    }

    public override void Draw()
    {
        DrawSidePanel();
        ImGui.SameLine();
        DrawContent();
    }

    private void DrawSidePanel()
    {
        using var listChild = ImRaii.Child("##List", ImGuiHelpers.ScaledVector2(120, 0), true);
        if (!listChild) return;

        for (int i = 0; i < _widgetManager.Widgets.Count; i++)
        {
            var widget = _widgetManager.Widgets[i];
            bool isSelected = _selectedIndex == i;

            using (ImRaii.PushColor(ImGuiCol.Header, Style.Components.ButtonBlueHovered, isSelected)
                         .Push(ImGuiCol.HeaderHovered, Style.Components.ButtonBlueHovered, isSelected)
                         .Push(ImGuiCol.HeaderActive, Style.Components.ButtonBlueHovered, isSelected))
            {
                if (ImGui.Selectable(widget.Instance.Title, isSelected))
                {
                    _selectedIndex = i;
                    _widgetManager.Show(i);
                }
            }
        }
    }

    private void DrawContent()
    {
        if (_widgetManager.Widgets.Count == 0) return;
        var widget = _widgetManager.Widgets[_selectedIndex];

        using var contentChild = ImRaii.Child("##Content", new Vector2(0, 0));
        if (!contentChild) return;

        ImGuiUtil.DrawColoredBanner(widget.Instance.Title, Style.Components.ButtonBlueHovered);
        ImGui.Spacing();
        _widgetManager.Draw();
    }
}
