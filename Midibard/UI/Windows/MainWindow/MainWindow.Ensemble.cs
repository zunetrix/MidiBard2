using MidiBard.Extensions.Dalamud.Party;

namespace MidiBard;

public partial class MainWindow
{
    private bool _ensemblePanelVisible = true;

    private void DrawEnsemblePanel()
    {
        if (!Plugin.Config.UiShowEnsemblePanel) return;
        if (!_ensemblePanelVisible) return;
        if (!DalamudApi.PartyList.IsPartyLeader()) return;

        Plugin.Ui.EnsembleWindow.DrawEnsemblePannel(useSmallSize: true, instrumentIconSize: 23f);
    }
}
