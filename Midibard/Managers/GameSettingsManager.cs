using Dalamud.Game.Config;

namespace MidiBard;

public static class GameSettingsManager
{
    public static SettingsDisplayObjectLimit GetDisplayObjectLimit()
    {
        DalamudApi.GameConfig.TryGet(SystemConfigOption.DisplayObjectLimitType2, out uint displayObjectLimitType2);
        // DalamudApi.PluginLog.Debug($"displayObjectLimitType2 {displayObjectLimitType2}");
        return (SettingsDisplayObjectLimit)displayObjectLimitType2;
    }

    public static void SetDisplayObjectLimit(SettingsDisplayObjectLimit displayObjectLimitType)
    {
        DalamudApi.GameConfig.Set(SystemConfigOption.DisplayObjectLimitType2, (uint)displayObjectLimitType);
    }

    public static void SetSoundMaster(uint value)
    {
        // 0 = enabled
        // 1 = muted
        if (value is 0u or 1u)
        {
            DalamudApi.GameConfig.Set(SystemConfigOption.IsSndMaster, value);
            // DalamudApi.GameConfig.System.Set("IsSndMaster", value);
            // DalamudApi.GameConfig.System.GetUInt("IsSndMaster");
        }
    }

    public static void SetAutoAfkSwitchingTime(uint value)
    {
        DalamudApi.GameConfig.Set(SystemConfigOption.AutoAfkSwitchingTime, value);
    }

    public static uint GetFps()
    {
        DalamudApi.GameConfig.TryGet(SystemConfigOption.Fps, out uint value);
        // DalamudApi.GameConfig.UiConfig.GetUInt("Fps");
        return value;
    }

    public static void SetFps(SettingsFps value)
    {
        DalamudApi.GameConfig.Set(SystemConfigOption.Fps, (uint)value);
    }

    public static uint GetFpsInactive()
    {
        DalamudApi.GameConfig.TryGet(SystemConfigOption.FPSInActive, out uint value);
        return value;
    }

    public static void SetFpsInactive(uint value)
    {
        if (value is 0u or 1u)
        {
            DalamudApi.GameConfig.Set(SystemConfigOption.FPSInActive, value);
        }
    }

    public static void EnableDebug()
    {
        DalamudApi.GameConfig.UiConfigChanged += OnUiConfigChanged;
        DalamudApi.GameConfig.UiControlChanged += OnUiControlChanged;
        DalamudApi.GameConfig.SystemChanged += OnSystemConfigChange;
    }

    public static void DisableDebug()
    {
        DalamudApi.GameConfig.UiConfigChanged -= OnUiConfigChanged;
        DalamudApi.GameConfig.UiControlChanged -= OnUiControlChanged;
        DalamudApi.GameConfig.SystemChanged -= OnSystemConfigChange;
    }

    private static void OnUiConfigChanged(object? sender, ConfigChangeEvent e)
    {
        var option = e.Option;
        var optionName = e.Option.ToString();

        DalamudApi.PluginLog.Warning($"UiConfigChanged: {option}");

        try
        {
            var value = DalamudApi.GameConfig.UiConfig.GetUInt(optionName);
            DalamudApi.PluginLog.Warning($"{optionName} [{option}] (UInt) = {value}");
            return;
        }
        catch { }

        try
        {
            var value = DalamudApi.GameConfig.UiConfig.GetFloat(optionName);
            DalamudApi.PluginLog.Warning($"{optionName} [{option}] (Float) = {value}");
            return;
        }
        catch { }

        try
        {
            var value = DalamudApi.GameConfig.UiConfig.GetString(optionName);
            DalamudApi.PluginLog.Warning($"{optionName} [{option}] (String) = {value}");
            return;
        }
        catch { }
    }

    private static void OnUiControlChanged(object? sender, ConfigChangeEvent e)
    {
        var option = e.Option;
        var optionName = e.Option.ToString();

        DalamudApi.PluginLog.Warning($"UiControlChanged: {option}");

        try
        {
            var value = DalamudApi.GameConfig.UiControl.GetUInt(optionName);
            DalamudApi.PluginLog.Warning($"{optionName} [{option}] (UInt - Control) = {value}");
            return;
        }
        catch { }
    }

    private static void OnSystemConfigChange(object? sender, ConfigChangeEvent e)
    {
        var option = e.Option;
        var optionName = e.Option.ToString();

        DalamudApi.PluginLog.Warning($"SystemChanged: {optionName} [{option}]");

        // try {
        //     DalamudApi.GameConfig.TryGet(SystemConfigOption.?, out uint value);
        //     DalamudApi.PluginLog.Warning($"{optionName} [{option}] (UInt - Control) = {value}");
        //     return;
        // } catch { }
    }
}

public enum SettingsDisplayObjectLimit
{
    Automatic = 0,
    Maximum = 1,
    High = 2,
    Normal = 3,
    Low = 4,
    Minimum = 5
}

public enum SettingsFps
{
    None = 0,
    MainDisplayRefreshRate = 1,
    Fps60 = 2,
    Fps30 = 3
}
