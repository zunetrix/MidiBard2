using Dalamud.Bindings.ImGui;

namespace MidiBard;

public sealed class ExampleDebugWidget : Widget
{
    public override string Title => "ExampleDebug";

    public ExampleDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        ImGui.Text(Title);

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

