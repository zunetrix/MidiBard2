using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard;

public partial class MainWindow
{
    private void DrawEnsemblePanel()
    {
        if (!Plugin.Config.UiShowEnsemblePanel) return;
        if (!DalamudApi.PartyList.IsPartyLeader()) return;

        ImGui.Separator();
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * 2.5f * ImGuiHelpers.GlobalScale))
        {
            Plugin.Ui.EnsembleWindow.DrawEnsemblePannel();
        }
    }
}
