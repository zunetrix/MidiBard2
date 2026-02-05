using Dalamud.Bindings.ImGui;

namespace MidiBard;

public sealed class IpcDebugWidget : Widget
{
    public override string Title => "IPC";

    public IpcDebugWidget(WidgetContext ctx) : base(ctx)
    {
    }

    public override void Draw()
    {
        ImGui.Text(Title);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("SyncAllSettings"))
        {
            Context.Plugin.IpcProvider.SyncAllSettings();
            DalamudApi.PluginLog.Warning($"IpcProvider.SyncAllSettings");
        }
    }
}

