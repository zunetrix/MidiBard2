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

using System;
using System.Numerics;

using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Utility;

using ImGuiNET;

using ImPlotNET;

using MidiBard.Managers.Ipc;
using MidiBard.Util;

using MidiBard2.Resources;

using static MidiBard.MidiBard;

using EnsembleManager = MidiBard.Managers.EnsembleManager;

namespace MidiBard;

public partial class PluginUI
{
    private static bool otherClientsMuted = false;
    private readonly string[] uilangStrings = Enum.GetNames<CultureCode>();
    // private readonly bool TrackViewVisible;
    private bool showMainWindow = false;
    private readonly ThemeManager themeManager = new ThemeManager(MidiBard.config.CurrentTheme);
    public bool MainWindowOpened => showMainWindow;
    private readonly FileDialogService fileDialogService = new FileDialogService(MidiBard.config.PinnedImportFolders);
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
        themeManager.PushTheme();

        fileDialogManager.Draw();

        if (showMainWindow)
        {
            DrawMainPluginWindow();
            DrawTrackVisualizerWindow();
            DrawCompensationEditWindow();
            DrawEnsembleControl();
            LrcEditor.Instance.Draw();
            ImGuiUtil.IconButtonSize.Clear();
        }

        DrawSettigsWindow();
        themeManager.PopTheme();

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
            var ensembleRunning = MidiBard.AgentMetronome.EnsembleModeRunning;
            var playerName = api.ClientState.LocalPlayer?.Name.TextValue ?? "";
            var playerWorld = api.ClientState.LocalPlayer?.HomeWorld.ValueNullable?.Name.ToDalamudString().TextValue ?? "";
            var playerInfo = MidiBard.config.hidePlayerInformationFromUi ? "" : $"{playerName}@{playerWorld}";
            var name = $"♪ MidiBard 2 v{typeof(PluginUI).Assembly.GetName().Version} ♪ {playerInfo} ###MIDIBARD";
            var windowFlags = config.miniPlayer ? ImGuiWindowFlags.NoDecoration : ImGuiWindowFlags.None;

            ImGui.SetNextWindowPos(new Vector2(100, 100), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(ImGuiHelpers.GlobalScale * 357, 0),
                new Vector2(ImGuiHelpers.GlobalScale * 357, float.MaxValue));
            if (ImGui.Begin(name, ref showMainWindow, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | windowFlags))
            {
                var icon = MidiBard.config.miniPlayer ? FontAwesomeIcon.ExpandAlt : FontAwesomeIcon.CompressAlt;
                if (ImGuiUtil.AddHeaderIcon("headerIconMinimode", icon.ToIconString(), Language.icon_button_tooltip_mini_player))
                {
                    config.miniPlayer ^= true;
                }

                // // ImGui.PushStyleColor(ImGuiCol.Text, Style.Colors.Red);
                // if (ImGuiUtil.AddHeaderIcon("heartSupport", FontAwesomeIcon.Heart.ToIconString(), "Support"))
                // {
                //     ImGui.OpenPopup("SupportContextMenu");
                // }
                // // ImGui.PopStyleColor();

                // if (ImGui.BeginPopup("SupportContextMenu"))
                // {
                //     if (ImGui.MenuItem("Join Discord"))
                //     {
                //         Util.Extensions.OpenUrl("https://discord.gg/ejGt2mXHJM");
                //     }

                //     if (ImGui.MenuItem("Support us on Ko-fi!"))
                //     {
                //         Util.Extensions.OpenUrl("https://ko-fi.com/midibard");
                //     }

                //     if (ImGui.MenuItem("MidiBard.org"))
                //     {
                //         Util.Extensions.OpenUrl("https://midibard.org/");
                //     }
                //     ImGui.EndPopup();
                // }

                if (ensembleRunning)
                {
                    ImGuiUtil.DrawColoredBanner(Style.Colors.Red, $"{Language.text_ensemble_mode_running} {EnsembleManager.EnsembleTimer.Elapsed:mm\\:ss\\:ff}");
                }

                if (listeningForEvents)
                {
                    ImGuiUtil.DrawColoredBanner(Style.Colors.Violet, Language.text_listening_midi_device + InputDeviceManager.CurrentInputDevice.DeviceName());
                }

                DrawPlaylist();

                DrawCurrentPlaying();

                ImGui.Spacing();

                if (!config.miniPlayer)
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
                    DrawButtonShowEnsembleControl(disabled: !api.PartyList.IsPartyLeader());

                    if (!api.PartyList.IsPartyLeader())
                    {
                        ShowEnsembleControlWindow = false;
                    }
                }
                ImGuiUtil.PopIconButtonSize();
                ImGui.PopStyleVar();

                if (!config.miniPlayer)
                {
                    ImGui.Separator();
                    DrawTrackSelection();
                    DrawMusicControlPanel();
                    DrawFooter();
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }
}
