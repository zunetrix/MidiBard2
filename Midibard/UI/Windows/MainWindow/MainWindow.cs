using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public partial class MainWindow : Window
{
    private Plugin Plugin { get; }
    private PluginUi Ui { get; }
    private readonly SongImportHelper _importHelper;

    private static readonly Version Version = typeof(MainWindow).Assembly.GetName().Version;

    internal MainWindow(Plugin plugin, PluginUi ui) : base($"{Plugin.Name} {Version}###MainWindow")
    {
        Plugin = plugin;
        Ui = ui;
        _importHelper = new SongImportHelper(plugin);
        Size = ImGuiHelpers.ScaledVector2(350, 630);
        SizeCondition = ImGuiCond.FirstUseEver;
        UpdateWindowConfig();
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.None;
        if (!Plugin.Config.AllowMovement)
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }

        if (!Plugin.Config.AllowResize)
        {
            Flags |= ImGuiWindowFlags.NoResize;
        }

        // Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
        var WindowSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(350, 100),
            // MaximumSize = ImGuiHelpers.ScaledVector2(350, float.MaxValue)
        };

        SizeConstraints = WindowSizeConstraints;

        var playerName = DalamudApi.PlayerState.CharacterName;
        var playerWorld = DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToString() ?? "";
        var playerInfo = Plugin.Config.hidePlayerInformationFromUi ? "" : $"{playerName}@{playerWorld}";
        var windowName = $"♪ MidiBard 2 v{Plugin.Version} ♪ {playerInfo}###MainWindow";
        this.WindowName = windowName;

        base.PreDraw();
    }

    public override void Draw()
    {
        DrawPlayer();
    }

    private void DrawPlayer()
    {
        var ensembleRunning = AgentManager.AgentMetronome.EnsembleModeRunning;
        if (Plugin.InputDeviceManager.IsListeningForEvents)
        {
            ImGuiUtil.DrawColoredBanner(Language.text_listening_midi_device + InputDeviceManager.CurrentInputDevice.DeviceName(), Style.Colors.Violet);
        }

        DrawCurrentPlaylist();
        DrawCurrentPlaying();

        ImGui.Spacing();

        if (!Plugin.Config.miniPlayer)
        {
            SliderProgressBar();
        }
        else
        {
            ProgressBar();
        }

        ImGui.Spacing();

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4)))
        {
            DrawButtonPlayPause(ensembleRunning);
            DrawButtonStop();
            DrawButtonFastForward(disabled: ensembleRunning);
            DrawButtonPlayMode();
            DrawButtonShowSettingsWindow();
            DrawButtonShowElements();
            DrawButtonPianoRollVisualization();
            DrawButtonShowEnsembleWindow(disabled: !DalamudApi.PartyList.IsPartyLeader());
        }

        if (!Plugin.Config.miniPlayer)
        {
            ImGui.Separator();
            DrawTrackSelection();
            DrawMusicControlPanel();
            DrawEnsemblePanel();
            if (Plugin.Config.UiShowAdsLinks)
            {
                DrawFooter();
            }
        }
    }

    internal void UpdateWindowConfig()
    {
        RespectCloseHotkey = Plugin.Config.AllowCloseWithEscape;
        TitleBarButtons.Clear();

        TitleBarButtons.Add(new TitleBarButton()
        {
            AvailableClickthrough = false,
            Icon = Plugin.Config.miniPlayer ? FontAwesomeIcon.ExpandAlt : FontAwesomeIcon.CompressAlt,
            ShowTooltip = () => ImGuiUtil.ToolTip(Language.icon_button_tooltip_mini_player),
            Click = _ =>
            {
                Plugin.Config.miniPlayer = !Plugin.Config.miniPlayer;
                this.UpdateWindowConfig();
            }
        });

        if (Plugin.Config.ShowSettingsButton)
        {
            TitleBarButtons.Add(new TitleBarButton()
            {
                AvailableClickthrough = false,
                Icon = FontAwesomeIcon.Cog,
                ShowTooltip = () => ImGuiUtil.ToolTip(Language.SettingsTitle),
                Click = _ => Ui.SettingsWindow2.Toggle()
            });
        }

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
            // Priority = int.MinValue,
            ShowTooltip = () => ImGuiUtil.ToolTip("Debug"),
            Click = _ => Plugin.Ui.DebugWindow.Toggle()
        });
#endif
    }
}
