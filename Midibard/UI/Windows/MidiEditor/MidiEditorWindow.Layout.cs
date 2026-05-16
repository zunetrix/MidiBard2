using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawEditorPanels(Vector2 available)
    {
        if (available.X <= 0f || available.Y <= 0f)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        EnsureEditorPanelWidths(scale);

        var layout = MidiEditorPanelLayout.Calculate(
            available.X,
            _showTrackPanel,
            _showEventPanel,
            _trackPanelWidth,
            _eventPanelWidth,
            scale);

        if (_showTrackPanel)
        {
            DrawEditorPanelChild("##MidiEditorTrackPanel", layout.TrackWidth, available.Y, DrawTrackListPanel);
            ImGui.SameLine(0f, 0f);
            DrawEditorPanelSplitter(
                "##MidiEditorTrackPanelSplitter",
                ref _trackPanelWidth,
                MidiEditorPanelLayout.MinTrackWidth(scale),
                MidiEditorPanelLayout.MaxTrackResizeWidth(
                    available.X,
                    _showEventPanel,
                    _eventPanelWidth,
                    scale));
            ImGui.SameLine(0f, 0f);
        }

        if (_showEventPanel)
        {
            DrawEditorPanelChild("##MidiEditorEventPanel", layout.EventWidth, available.Y, DrawEventListPanel);
            ImGui.SameLine(0f, 0f);
            DrawEditorPanelSplitter(
                "##MidiEditorEventPanelSplitter",
                ref _eventPanelWidth,
                MidiEditorPanelLayout.MinEventWidth(scale),
                MidiEditorPanelLayout.MaxEventResizeWidth(
                    available.X,
                    _showTrackPanel,
                    _trackPanelWidth,
                    scale));
            ImGui.SameLine(0f, 0f);
        }

        DrawEditorPanelChild(
            "##MidiEditorPianoRollPanel",
            layout.PianoRollWidth,
            available.Y,
            DrawPianoRollPanel,
            ImGuiWindowFlags.NoScrollbar);
    }

    private void EnsureEditorPanelWidths(float scale)
    {
        if (_trackPanelWidth <= 0f)
            _trackPanelWidth = MidiEditorPanelLayout.DefaultTrackWidth(scale);
        if (_eventPanelWidth <= 0f)
            _eventPanelWidth = MidiEditorPanelLayout.DefaultEventWidth(scale);
    }

    private static void DrawEditorPanelChild(
        string id,
        float width,
        float height,
        Action draw,
        ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        using var child = ImRaii.Child(id, new Vector2(MathF.Max(1f, width), height), false, flags);
        if (child)
            draw();
    }

    private static void DrawEditorPanelSplitter(
        string id,
        ref float panelWidth,
        float minWidth,
        float maxWidth)
    {
        maxWidth = MathF.Max(minWidth, maxWidth);
        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(0f, 0f));
        ImGui.InvisibleButton(
            id,
            new Vector2(MidiEditorPanelLayout.SplitterWidth * ImGuiHelpers.GlobalScale, -1f));

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            panelWidth += ImGui.GetIO().MouseDelta.X;
            panelWidth = Math.Clamp(panelWidth, minWidth, maxWidth);
        }
    }
}
