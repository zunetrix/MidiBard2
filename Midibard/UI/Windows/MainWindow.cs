using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

using MidiBard.Resources;

namespace MidiBard;

public class MainWindow : Window
{
    private Plugin Plugin { get; }
    private PluginUi Ui { get; }
    public bool IsVisible { get; private set; }
    private static readonly Version Version = typeof(MainWindow).Assembly.GetName().Version;
    // private static readonly string VersionString = Version?.ToString();

    internal MainWindow(Plugin plugin, PluginUi ui) : base($"{Plugin.Name} {Version}###MainWindow")
    {
        Plugin = plugin;
        Ui = ui;

        Size = ImGuiHelpers.ScaledVector2(600, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
        UpdateWindowConfig();
    }

    public override void Update()
    {
        IsVisible = false;
        base.Update();
    }

    public override void PreDraw()
    {
        // Flags = ImGuiWindowFlags.None;
        Flags = ImGuiWindowFlags.MenuBar;
        if (!Plugin.Config.AllowMovement)
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }

        if (!Plugin.Config.AllowResize)
        {
            Flags |= ImGuiWindowFlags.NoResize;
        }

        base.PreDraw();
    }

    public override bool DrawConditions()
    {
        // var inCombat = DalamudApi.Condition[ConditionFlag.InCombat];
        // var inInstance = DalamudApi.Condition[ConditionFlag.BoundByDuty]
        //                  || DalamudApi.Condition[ConditionFlag.BoundByDuty56]
        //                  || DalamudApi.Condition[ConditionFlag.BoundByDuty95];
        // var inCutscene = DalamudApi.Condition[ConditionFlag.WatchingCutscene]
        //                  || DalamudApi.Condition[ConditionFlag.WatchingCutscene78]
        //                  || DalamudApi.Condition[ConditionFlag.OccupiedInCutSceneEvent];

        // if (inCombat && !Plugin.Config.ShowInCombat) return false;
        // if (inInstance && !Plugin.Config.ShowInInstance) return false;
        // if (inCutscene && !Plugin.Config.ShowInCutscenes) return false;

        return true;
    }

    public override void Draw()
    {
        IsVisible = true;


        ImGui.Text("Main window");
    }

    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;

        TitleBarButtons.Clear();
        if (Plugin.Config.ShowSettingsButton)
        {
            TitleBarButtons.Add(new TitleBarButton()
            {
                AvailableClickthrough = false,
                Icon = FontAwesomeIcon.Cog,
                ShowTooltip = () => ImGuiUtil.ToolTip(Language.SettingsTitle),
                Click = _ => Ui.SettingsWindow.Toggle()
            });

            TitleBarButtons.Add(new TitleBarButton()
            {
                AvailableClickthrough = false,
                Icon = FontAwesomeIcon.Heart,
                ShowTooltip = () => ImGuiUtil.ToolTip("Discord"),
                Click = _ => WindowsApi.OpenUrl("https://discord.gg/ejGt2mXHJM")
            });

#if DEBUG
            TitleBarButtons.Add(new TitleBarButton()
            {
                AvailableClickthrough = false,
                Icon = FontAwesomeIcon.Bug,
                ShowTooltip = () => ImGuiUtil.ToolTip("Debug"),
                Click = _ => Plugin.Ui.DebugWindow.Toggle()
            });
#endif
        }
    }
}
