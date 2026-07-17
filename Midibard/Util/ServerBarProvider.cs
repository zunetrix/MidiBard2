using System;

using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace MidiBard;

internal sealed class ServerBarProvider : IDisposable
{
    private Plugin Plugin { get; }

    public readonly IDtrBarEntry _midibardDtrBarEntry = DalamudApi.DtrBar.Get("midibard-serverbar");

    public ServerBarProvider(Plugin plugin)
    {
        Plugin = plugin;

        _midibardDtrBarEntry.OnClick += OnIconClick;
        _midibardDtrBarEntry.Shown = Plugin.Config.ShowServerBarIcon;
        // _midibardDtrBarEntry.Text = "MBard";
        _midibardDtrBarEntry.Tooltip = "Open MidiBard";

        var icon = new IconPayload(BitmapFontIcon.WatchingCutscene);
        _midibardDtrBarEntry.Text = new SeString(icon);
        // var payloadText = new TextPayload("MBard");
        // _midibardDtrBarEntry.Text = new SeString(icon, payloadText);
    }

    private void OnIconClick(DtrInteractionEvent ev)
    {
        if (ev.ClickType == MouseClickType.Left)
        {
            Plugin.Ui.MainWindow.Toggle();

        }
    }

    public void Dispose()
    {
        _midibardDtrBarEntry.OnClick -= OnIconClick;
        _midibardDtrBarEntry.Remove();
    }

    public void Update()
    {
        _midibardDtrBarEntry.Shown = Plugin.Config.ShowServerBarIcon;
    }
}
