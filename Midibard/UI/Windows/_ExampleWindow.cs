using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;

namespace MidiBard;

public class ExampleWindow : Window
{
    private Plugin Plugin { get; }

    public ExampleWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SettingsTitle}###ExampleWindow")
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
        ImGui.Text("Example Window");
    }
}
