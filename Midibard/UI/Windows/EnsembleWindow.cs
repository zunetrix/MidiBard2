using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Resources;

namespace MidiBard;

public class EnsembleWindow : Window
{
    private Plugin Plugin { get; }

    public EnsembleWindow(Plugin plugin) : base($"{Plugin.Name} {Language.SettingsTitle}###EnsembleWindow")
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

    public override bool DrawConditions()
    {
        if (!DalamudApi.PartyList.IsPartyLeader()) return false;

        return true;
    }


    public override void Draw()
    {
        ImGui.Text("Example Window");
    }
}
