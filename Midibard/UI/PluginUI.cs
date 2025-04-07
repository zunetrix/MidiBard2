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
using MidiBard2.Resources;

using EnsembleManager = MidiBard.Managers.EnsembleManager;

using static MidiBard.MidiBard;

namespace MidiBard;

public partial class PluginUI
{
    private static bool otherClientsMuted = false;
    private readonly string[] uilangStrings = Enum.GetNames<CultureCode>();
    private readonly bool TrackViewVisible;
    private bool mainWindowOpen = false;
    public bool MainWindowOpened => mainWindowOpen;
    private readonly FileDialogManager fileDialogManager = new FileDialogManager();
    public PluginUI()
    {
        ImPlot.SetImGuiContext(ImGui.GetCurrentContext());
        var _context = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(_context);
    }

    public void ToggleMainWindow()
    {
        if (mainWindowOpen)
            CloseMainWindow();
        else
            OpenMainWindow();
    }

    public void OpenMainWindow()
    {
        mainWindowOpen = true;
    }

    public void CloseMainWindow()
    {
        mainWindowOpen = false;
    }

    public unsafe void Draw()
    {
        fileDialogManager.Draw();

        if (mainWindowOpen)
        {
            DrawMainPluginWindow();
            DrawPlotWindow();
            DrawCompensationEditWindow();
            DrawEnsembleControl();
            //LrcEditor.Instance.Draw();
            ImGuiUtil.IconButtonSize.Clear();
        }

        DrawSettigsWindow();
    }

    private void DrawMainPluginWindow()
    {
        ImGui.SetNextWindowPos(new Vector2(100, 100), ImGuiCond.FirstUseEver);
        var ensembleModeRunning = AgentMetronome.EnsembleModeRunning;
        // var ensemblePreparing = AgentMetronome.MetronomeBeatsElapsed < 0;
        var listeningForEvents = InputDeviceManager.IsListeningForEvents;

        try
        {
            //  var title = string.Format("MidiBard{0}{1}###midibard",
            //  ensembleModeRunning ? " - Ensemble Running" : string.Empty,
            //  isListeningForEvents ? " - Listening Events" : string.Empty);
            var flag = config.miniPlayer ? ImGuiWindowFlags.NoDecoration : ImGuiWindowFlags.None;
            ImGui.SetNextWindowSizeConstraints(new Vector2(ImGuiHelpers.GlobalScale * 357, 0),
                new Vector2(ImGuiHelpers.GlobalScale * 357, float.MaxValue));

            var playerName = api.ClientState.LocalPlayer?.Name.TextValue ?? "";
            var playerWorld = api.ClientState.LocalPlayer?.HomeWorld.ValueNullable?.Name.ToDalamudString().TextValue ?? "";
            var playerInfo = MidiBard.config.hidePlayerInformationFromUi ? "" : $"{playerName}@{playerWorld}";
            var ensembleRunning = MidiBard.AgentMetronome.EnsembleModeRunning;

            var name = $"♪ MidiBard 2 v{typeof(PluginUI).Assembly.GetName().Version} ♪ {playerInfo} ###MIDIBARD";
            if (ImGui.Begin(name, ref mainWindowOpen, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | flag))
            {
                var icon = MidiBard.config.miniPlayer ? FontAwesomeIcon.ExpandAlt : FontAwesomeIcon.CompressAlt;
                if (ImGuiUtil.AddHeaderIcon("headerIconMinimode", icon.ToIconString(), Language.icon_button_tooltip_mini_player)) config.miniPlayer ^= true;

                if (ensembleModeRunning)
                {
                    {
                        ImGuiUtil.DrawColoredBanner(ImGuiUtil.red, $"{Language.text_ensemble_mode_running} {EnsembleManager.EnsembleTimer.Elapsed:mm\\:ss\\:ff}");
                    }
                }

                if (listeningForEvents)
                {
                    ImGuiUtil.DrawColoredBanner(ImGuiUtil.violet, Language.text_listening_midi_device + InputDeviceManager.CurrentInputDevice.DeviceName());
                }

                DrawPlaylist();
                DrawCurrentPlaying();

                ImGui.Spacing();
                DrawProgressBar();
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
                    DrawTrackTrunkSelectionWindow();
                    DrawPanelMusicControl();
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    // private static unsafe void ToggleButton(ref bool b)
    // {
    //     PushStyleColor(ImGuiCol.Text, b ? MidiBard.config.themeColor : *GetStyleColorVec4(ImGuiCol.Text));
    //     if (Button(FontAwesomeIcon.Stream.ToIconString())) b ^= true;
    //     PopStyleColor();
    // }
}
