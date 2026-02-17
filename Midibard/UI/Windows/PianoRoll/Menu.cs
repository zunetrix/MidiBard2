using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.General;
using MidiBard.Util.ImGuiExt;

namespace MidiBard;

public partial class PianoRollWindow
{
    private void DrawMenuBar()
    {
        using (ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor))
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1))
            {
                using (var menu = ImRaii.MenuBar())
                {
                    if (ImGui.BeginMenu("Menu"))
                    {
                        ImGuiUtil.IconButtonToggle("##HandToolBtn", ref _panMode, FontAwesomeIcon.HandPaper, FontAwesomeIcon.MousePointer, "Hand Tool");
                        ImGui.EndMenu();
                    }

                    DrawViewMenu();

                    DrawOptionsMenu();

                    // if (ImGui.MenuItem("Menu Item"))
                    // {
                    //     //
                    // }
                }
            }
        }
    }

    private void DrawViewMenu()
    {
        using (var menu = ImRaii.Menu("View"))
        {
            if (!menu) return;
            ImGui.Checkbox($"Left Panel", ref _showLeftPanel);

            ImGui.Checkbox($"Note Label", ref _showNoteLabel);

            ImGui.Checkbox($"Note Border", ref _showNoteBorder);

            ImGui.Checkbox($"Time Markers", ref _showSeconds);

            ImGui.Checkbox($"C3-C6 Markers", ref _showC3C6Range);

            ImGui.Checkbox($"Voice Limit Markers", ref _showVoiceLimit);
        }
    }

    private void DrawOptionsMenu()
    {
        using (var menu = ImRaii.Menu("Options"))
        {
            if (!menu) return;
            ImGui.Checkbox($"Follow Playback", ref _autoFollowPlayback);

            ImGuiGroupPanel.BeginGroupPanel("Voice Limit");
            {
                ImGui.Checkbox($"Group Voice Limit Regions", ref _groupVoiceLimitRegions);
                ImGuiUtil.ToolTip("Group voice limit markers to max 1 per second");


                ImGui.Text("Max Voice Limit:");
                ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt("##InputMaxVoiceLimit", ref _maxVoiceLimit, 1, 1, flags: ImGuiInputTextFlags.AutoSelectAll))
                {
                    _maxVoiceLimit = _maxVoiceLimit.Clamp(1, 30);
                }
                ImGui.SameLine();
                if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetVoiceLimit", "Reset"))
                {
                    _maxVoiceLimit = 16;
                }


            }
            ImGuiGroupPanel.EndGroupPanel();

            ImGuiGroupPanel.BeginGroupPanel("Grid");
            {
                // ImGui.Text("Grid");
                ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
                ImGuiUtil.EnumCombo("##BeatDivision", ref _beatDivision);
            }
            ImGuiGroupPanel.EndGroupPanel();
        }
    }

    private void DrawToolsArea()
    {
        ImGui.Text($"Song: {songName}");

        DrawTimelineSlider();

        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(5, 0);
        ImGui.SameLine();

        // Time scale slider
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        ImGui.DragFloat("Time Scale##InputTimeScale", ref _timePixelsPerSecond, 0.1f, 25f, 500f);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetTimeScale", "Reset"))
        {
            _timePixelsPerSecond = 25f;
        }

        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(10, 0);
        ImGui.SameLine();

        // Note scale slider
        ImGui.SetNextItemWidth(150 * ImGuiHelpers.GlobalScale);
        ImGui.DragFloat("Note Scale##InputNoteScale", ref _noteMinHeight, 0.1f, 10f, 40f);
        ImGuiUtil.ToolTip("Drag or double-click to type");
        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.Undo, "##BtnResetNoteScale", "Reset"))
        {
            _noteMinHeight = 10f;
        }

        ImGuiHelpers.ScaledDummy(0, 5);
    }

}
