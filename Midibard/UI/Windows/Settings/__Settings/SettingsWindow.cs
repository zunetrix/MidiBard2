using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;

namespace MidiBard;

public partial class SettingsWindow2 : Window
{
    private Plugin Plugin { get; }

    private bool showInstrumentNameReferenceWindow = false;

    public SettingsWindow2(Plugin plugin) : base($"{Plugin.Name} {Language.SettingsTitle}###SettingsWindow2")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.None;
        if (!Plugin.Config.AllowMovement)
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }

        if (!Plugin.Config.AllowResize)
        {
            Flags |= ImGuiWindowFlags.NoResize;
        }

        // Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
        var WindowSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(400, 300),
            // MaximumSize = ImGuiHelpers.ScaledVector2(350, float.MaxValue)
        };

        SizeConstraints = WindowSizeConstraints;

        base.PreDraw();
    }

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar($"{Language.SettingsGeneralTab}###ConfigTabBar");
        if (!tabBar) return;

        DrawGeneralSettingsTab();
        DrawPerformanceSettingsTab();
        DrawEnsembleSettingsTab();
    }

    private void DrawGeneralSettingsTab()
    {
        using var tabItem = ImRaii.TabItem($"{Language.setting_group_label_general_settings}##GeneralSettingsTab");
        if (!tabItem) return;

        DrawGeneralSettings();
    }

    private void DrawPerformanceSettingsTab()
    {
        using var tabItem = ImRaii.TabItem($"{Language.setting_group_label_performance_settings}##PerformanceSettingsTab");
        if (!tabItem) return;

        DrawPerformanceSettings();
    }

    private void DrawEnsembleSettingsTab()
    {
        using var tabItem = ImRaii.TabItem($"{Language.setting_group_label_ensemble_settings}##EnsembleSettingsTab");
        if (!tabItem) return;

        DrawEnsembleSettings();
    }
}
