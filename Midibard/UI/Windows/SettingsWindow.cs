using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;

namespace MidiBard;

public class SettingsWindow : Window
{
    private Plugin Plugin { get; }

    public SettingsWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SettingsTitle}###SettingsWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;
    }

    public override void OnOpen()
    {
        base.OnOpen();
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##SettingsTabs")) return;
        DrawGeneralTab();
        ImGui.EndTabBar();
    }

    private void DrawGeneralTab()
    {
        if (ImGui.BeginTabItem($"{Language.SettingsGeneralTab}###GeneralTab"))
        {

            ImGui.Text("Tab");

            ImGui.EndTabItem();
        }
    }
}
