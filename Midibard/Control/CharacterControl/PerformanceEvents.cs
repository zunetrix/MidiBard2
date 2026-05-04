namespace MidiBard.Control.CharacterControl;

internal class PerformanceEvents
{
    private Plugin Plugin { get; }
    private bool inPerformanceMode { get; set; }

    private uint? savedFps;
    private uint? savedFpsInactive;
    private SettingsDisplayObjectLimit? savedDisplayObjectLimit;

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
            GameSettingsManager.SetAutoAfkSwitchingTime(0);
        }

        if (Plugin.Config.AutoSetFps)
        {
            savedFps = GameSettingsManager.GetFps();
            GameSettingsManager.SetFps(SettingsFps.Fps60);
        }

        if (Plugin.Config.AutoSetLimitFpsWhenInactive)
        {
            savedFpsInactive = GameSettingsManager.GetFpsInactive();
            GameSettingsManager.SetFpsInactive(0);
        }

        if (Plugin.Config.AutoSetDisplayObjectLimit)
        {
            savedDisplayObjectLimit = GameSettingsManager.GetDisplayObjectLimit();
            GameSettingsManager.SetDisplayObjectLimit(SettingsDisplayObjectLimit.Minimum);
        }
    }

    private void ExitingPerformance()
    {
        if (Plugin.Config.AutoClosePlayerWhenPerforming)
            if (!Plugin.InstrumentSwitcher.SwitchingInstrument)
                Plugin.Ui.MainWindow.IsOpen = false;

        if (Plugin.Config.AutoSetFps && savedFps.HasValue)
        {
            GameSettingsManager.SetFps((SettingsFps)savedFps.Value);
            savedFps = null;
        }

        if (Plugin.Config.AutoSetLimitFpsWhenInactive && savedFpsInactive.HasValue)
        {
            GameSettingsManager.SetFpsInactive(savedFpsInactive.Value);
            savedFpsInactive = null;
        }

        if (Plugin.Config.AutoSetDisplayObjectLimit && savedDisplayObjectLimit.HasValue)
        {
            GameSettingsManager.SetDisplayObjectLimit(savedDisplayObjectLimit.Value);
            savedDisplayObjectLimit = null;
        }
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
