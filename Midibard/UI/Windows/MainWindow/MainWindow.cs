using System;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

using MidiBard.Extensions.Dalamud.Party;
using MidiBard.Extensions.DryWetMidi;
using MidiBard.Resources;
using MidiBard.Util2;

namespace MidiBard;

public partial class MainWindow : Window
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
        Flags = ImGuiWindowFlags.None;
        if (!Plugin.Config.AllowMovement)
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }

        if (!Plugin.Config.AllowResize)
        {
            Flags |= ImGuiWindowFlags.NoResize;
        }

        // var WindowSizeConstraints = new WindowSizeConstraints();
        // WindowSizeConstraints.MinimumSize = new Vector2(ImGuiHelpers.GlobalScale * 357, 0);
        // WindowSizeConstraints.MaximumSize = new Vector2(ImGuiHelpers.GlobalScale * 357, float.MaxValue);
        // SizeConstraints = WindowSizeConstraints;

        // var windowFlag = Plugin.Config.miniPlayer ? ImGuiWindowFlags.NoDecoration : ImGuiWindowFlags.None;
        // Flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | windowFlag;

        Ui.FileDialogService.FileDialogManager.Draw();
        base.PreDraw();
    }

    public override void Draw()
    {
        IsVisible = true;

        // FileDialogManager.OpenFolderDialog(
        //         title: "Select Folder",
        //         startPath: Plugin.Config.MacroExportPath,
        //         callback: (result, selectedPath) =>
        //         {
        //             if (!result) return;
        //             if (!Path.Exists(Plugin.Config.MacroExportPath)) return;
        //             Plugin.Config.MacroExportPath = selectedPath;
        //             Plugin.IpcProvider.SyncConfiguration();
        //         });

        // FileDialogManager.SaveFileDialog("Export", ".json", exportFileName, ".json", (result, selectedPath) =>
        // {
        //     if (!result) return;

        //     Plugin.MacroManager.ExportMacrosToFile(selectedPath, Plugin.Config.IncludeCidOnExport);

        //     Plugin.Config.MacroExportPath = Path.GetDirectoryName(selectedPath);
        //     Plugin.Config.Save();
        //     Plugin.IpcProvider.SyncConfiguration();
        // }, exportFolder);

        // if (ImGui.Button("Import File"))
        // {
        //     FileDialogManager.OpenFileDialog(
        //         title: "Import",
        //         filters: ".json",
        //         startPath: Plugin.Config.MacroExportPath,
        //         selectionCountMax: 1,
        //         callback: (result, selectedPaths) =>
        //         {
        //             if (!result || selectedPaths.Count == 0) return;
        //             if (!File.Exists(selectedPaths[0])) return;

        //             Plugin.MacroManager.ImportMacrosFromFile(selectedPaths[0], Plugin.Config.MacroImportMode, Plugin.Config.IncludeCidOnImport, backupBeforeImport);
        //         }
        //     );
        // }

        DrawPlayer();
    }

    private void DrawPlayer()
    {
        var listeningForEvents = Plugin.InputDeviceManager.IsListeningForEvents;
        var ensembleRunning = Plugin.AgentMetronome.EnsembleModeRunning;
        var playerName = DalamudApi.PlayerState.CharacterName;
        var playerWorld = DalamudApi.PlayerState.HomeWorld.ValueNullable?.Name.ToDalamudString().TextValue ?? "";
        var playerInfo = Plugin.Config.hidePlayerInformationFromUi ? "" : $"{playerName}@{playerWorld}";
        var name = $"♪ MidiBard 2 v{Plugin.VersionString} ♪ {playerInfo}###MainWindow";
        this.WindowName = name;

        if (listeningForEvents)
        {
            ImGuiUtil.DrawColoredBanner(Language.text_listening_midi_device + InputDeviceManager.CurrentInputDevice.DeviceName(), Style.Colors.Violet);
        }

        DrawPlaylist();

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

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(4, 4));
        ImGuiUtil.PushIconButtonSize(ImGuiHelpers.ScaledVector2(45.5f, 25));
        {
            DrawButtonPlayPause(disabled: ensembleRunning);
            DrawButtonStop();
            DrawButtonFastForward(disabled: ensembleRunning);
            DrawButtonPlayMode(disabled: ensembleRunning);
            DrawButtonShowSettingsWindow();
            DrawButtonVisualization();
            DrawButtonShowEnsembleWindow(disabled: !DalamudApi.PartyList.IsPartyLeader());
        }
        ImGuiUtil.PopIconButtonSize();
        ImGui.PopStyleVar();

        if (!Plugin.Config.miniPlayer)
        {
            ImGui.Separator();
            DrawTrackSelection();
            DrawMusicControlPanel();
            // DrawFooter();
        }
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
                Icon = Plugin.Config.miniPlayer ? FontAwesomeIcon.ExpandAlt : FontAwesomeIcon.CompressAlt,
                ShowTooltip = () => ImGuiUtil.ToolTip(Language.icon_button_tooltip_mini_player),
                Click = _ =>
                {
                    Plugin.Config.miniPlayer = !Plugin.Config.miniPlayer;
                    this.UpdateWindowConfig();
                }
            });

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
