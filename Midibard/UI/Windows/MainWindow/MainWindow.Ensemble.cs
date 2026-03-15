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

        Plugin.Ui.EnsembleWindow.DrawEnsemblePannel(zoom: 1.5f, instrumentIconSize: 27f);
    }
}
