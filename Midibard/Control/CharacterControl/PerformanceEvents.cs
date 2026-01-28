namespace MidiBard.Control.CharacterControl;

internal class PerformanceEvents
{
    private Plugin Plugin { get; }
    private bool inPerformanceMode { get; set; }

    public PerformanceEvents(Plugin plugin)
    {
        Plugin = plugin;
    }

    private void EnteringPerformance()
    {
        if (Plugin.Config.AutoOpenPlayerWhenPerforming)
            if (!Plugin.InstrumentSwitcher.SwitchingInstrument)
                Plugin.Ui.MainWindow.IsOpen = true;

        if (Plugin.Config.AutoSetOffAFKSwitchingTime)
        {
            DalamudApi.GameConfig.System.Set("AutoAfkSwitchingTime", 0);
        }
    }

    private void ExitingPerformance()
    {
        if (Plugin.Config.AutoClosePlayerWhenPerforming)
            if (!Plugin.InstrumentSwitcher.SwitchingInstrument)
                Plugin.Ui.MainWindow.IsOpen = false;
    }

    public bool InPerformanceMode
    {
        set
        {
            if (value && !inPerformanceMode)
            {
                EnteringPerformance();
            }

            if (!value && inPerformanceMode)
            {
                ExitingPerformance();
            }

            inPerformanceMode = value;
        }
    }
}
