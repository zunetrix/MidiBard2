using Dalamud.Bindings.ImGui;

namespace MidiBard;

public sealed class GeneralDebugWidget : Widget
{
    public override string Title => "General";

    public GeneralDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        ImGui.Text("Debug");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SameLine();
        if (ImGui.Button("Test"))
        {
            DalamudApi.PluginLog.Warning($"{Context.Plugin.Config.AlignMidi}");
        }

    }
}

