// Copyright (C) 2022 akira0245
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see https://github.com/akira0245/MidiBard/blob/master/LICENSE.
//
// This code is written by akira0245 and was originally used in the MidiBard project. Any usage of this code must prominently credit the author, akira0245, and indicate that it was originally used in the MidiBard project.

using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Utility;

using MidiBard.Managers.Ipc;
using MidiBard.Util;

using MidiBard.Resources;

namespace MidiBard;

public partial class PluginUI
{
    private bool showMainWindow = false;
    public bool MainWindowOpened => showMainWindow;
    private readonly ThemeManager themeManager = new ThemeManager(Plugin.Config.CurrentTheme);
    private readonly FileDialogService fileDialogService = new FileDialogService(Plugin.Config.PinnedImportFolders);
    private FileDialogManager fileDialogManager => fileDialogService.DialogManager;

    public PluginUI()
    {
        ImPlot.SetImGuiContext(ImGui.GetCurrentContext());
        var _context = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(_context);
    }

    public void ToggleMainWindow()
    {
        if (showMainWindow)
            CloseMainWindow();
        else
            OpenMainWindow();
    }

    public void OpenMainWindow()
    {
        showMainWindow = true;
    }

    public void CloseMainWindow()
    {
        showMainWindow = false;
    }

    public unsafe void Draw()
    {
        fileDialogManager.Draw();

        // TODO: find a better way to apply the theme without interfering with other plugins
        themeManager.PushThemeStyles();

        if (showMainWindow)
        {
            DrawMainPluginWindow();
            DrawTrackVisualizerWindow();
            DrawCompensationEditWindow();
            DrawEnsembleWindow();
            DrawBMLWindow();
            LrcEditor.Instance.Draw();
            ImGuiUtil.IconButtonSize.Clear();
        }

        DrawSettigsWindow();
        themeManager.PopThemeStyles();

#if DEBUG
        DrawDebugWindow();
#endif
    }

    private void DrawMainPluginWindow()
    {
        var listeningForEvents = InputDeviceManager.IsListeningForEvents;
        // var ensemblePreparing = AgentMetronome.MetronomeBeatsElapsed < 0;
        try
        {
            var ensembleRunning = Plugin.AgentMetronome.EnsembleModeRunning;
            var playerName = DalamudApi.Player.CharacterName;
            var playerWorld = DalamudApi.Player.HomeWorld.ValueNullable?.Name.ToDalamudString().TextValue ?? "";
            var playerInfo = Plugin.Config.hidePlayerInformationFromUi ? "" : $"{playerName}@{playerWorld}";
            var name = $"♪ MidiBard 2 v{Plugin.VersionString} ♪ {playerInfo} ###MIDIBARD";
            var windowFlags = Plugin.Config.miniPlayer ? ImGuiWindowFlags.NoDecoration : ImGuiWindowFlags.None;

            ImGui.SetNextWindowPos(new Vector2(100, 100), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(ImGuiHelpers.GlobalScale * 357, 0),
                new Vector2(ImGuiHelpers.GlobalScale * 357, float.MaxValue));
            if (ImGui.Begin(name, ref showMainWindow, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | windowFlags))
            {
                var icon = Plugin.Config.miniPlayer ? FontAwesomeIcon.ExpandAlt : FontAwesomeIcon.CompressAlt;
                if (ImGuiUtil.AddHeaderIcon("headerIconMinimode", icon.ToIconString(), Language.icon_button_tooltip_mini_player))
                {
                    Plugin.Config.miniPlayer ^= true;
                }

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
                    if (!DalamudApi.PartyList.IsPartyLeader())
                    {
                        ShowEnsembleWindow = false;
                    }
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
        }
        finally
        {
            ImGui.End();
        }
    }
}
