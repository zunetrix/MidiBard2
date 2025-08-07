using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace MidiBard;

public partial class PluginUI
{
    private bool showSettingsWindow = false;
    private bool showCompensationEditWindow = false;
    private bool showInstrumentNameReferenceWindow = false;

    public void ToggleSettingsWindow()
    {
        if (showSettingsWindow)
            CloseSettingsWindow();
        else
            OpenSettingsWindow();
    }

    public void OpenSettingsWindow()
    {
        showSettingsWindow = true;
    }

    public void CloseSettingsWindow()
    {
        showSettingsWindow = false;
    }

    private void DrawSettigsWindow()
    {
        if (!showSettingsWindow) return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(350, 100) * ImGuiHelpers.GlobalScale, ImGuiHelpers.MainViewport.Size);
        ImGui.Begin("MidiBard Settings", ref showSettingsWindow);

        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("General Settings"))
            {
                DrawGeneralSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Performance Settings"))
            {
                DrawPerformanceSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Ensemble Settings"))
            {
                DrawEnsembleSettings();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.End();


    }



}
