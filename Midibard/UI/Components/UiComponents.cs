using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Texture;
using MidiBard.Util;

namespace MidiBard;

public static class UiComponents
{
    static readonly HashSet<uint> InstrumentGroupBreaks = new() { 4, 9, 14, 19, 23 };

    public static bool InstrumentPicker(string label, ref uint instrumentId, Vector2? size = null)
    {
        bool changed = false;
        uint undefinedInstrumentIconId = 60042;
        uint iconId = instrumentId == 0 ? undefinedInstrumentIconId : InstrumentHelper.Instruments[instrumentId].IconId;
        var iconSize = size == null ? ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()) : size.Value;
        DalamudApi.TextureProvider.DrawIcon(iconId, iconSize);

        if (ImGui.IsItemHovered())
            ImGuiUtil.ToolTip(InstrumentHelper.Instruments[instrumentId].InstrumentString);

        ImGui.OpenPopupOnItemClick($"InstrumentPopup_{label}", ImGuiPopupFlags.MouseButtonLeft);

        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1)
        .Push(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(ImGui.GetStyle().FramePadding.Y));
        using var popUp = ImRaii.Popup($"InstrumentPopup_{label}");
        if (popUp)
        {
            for (uint i = 1; i < InstrumentHelper.Instruments.Length; i++)
            {
                DalamudApi.TextureProvider.DrawIcon(InstrumentHelper.Instruments[i].IconId, ImGuiHelpers.ScaledVector2(40, 40));
                if (ImGui.IsItemClicked())
                {
                    instrumentId = i;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.IsItemHovered())
                    ImGuiUtil.ToolTip(InstrumentHelper.Instruments[i].InstrumentString);

                if (!InstrumentGroupBreaks.Contains(i))
                    ImGui.SameLine();
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            instrumentId = 0;
            changed = true;
        }

        return changed;
    }
}
