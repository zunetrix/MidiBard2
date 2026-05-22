using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud;
using MidiBard.Util;

namespace MidiBard;

public readonly record struct IconPickerItem(
    uint Value,
    uint IconId,
    string Tooltip,
    bool BreakAfter = false);

public static class UiComponents
{
    static readonly HashSet<uint> InstrumentGroupBreaks = new() { 4, 9, 14, 19, 23 };

    public static bool IsInstrumentGroupBreak(uint instrumentId)
        => InstrumentGroupBreaks.Contains(instrumentId);

    public static bool InstrumentPicker(string label, ref uint instrumentId, Vector2? size = null)
    {
        uint undefinedInstrumentIconId = 60042;
        var instruments = InstrumentHelper.Instruments;
        var hasInstrument = instruments != null && instrumentId < instruments.Length;
        uint iconId = instrumentId == 0 || !hasInstrument
            ? undefinedInstrumentIconId
            : instruments![instrumentId].IconId;
        var tooltip = hasInstrument ? instruments![instrumentId].InstrumentString : "None";

        var items = new List<IconPickerItem>();
        if (instruments != null)
        {
            for (uint i = 1; i < instruments.Length; i++)
            {
                items.Add(new IconPickerItem(
                    i,
                    instruments[i].IconId,
                    instruments[i].FFXIVDisplayName,
                    InstrumentGroupBreaks.Contains(i)));
            }
        }

        if (!IconGridPicker(
                $"InstrumentPopup_{label}",
                iconId,
                tooltip,
                items,
                out var selectedInstrumentId,
                size,
                allowRightClickReset: true))
        {
            return false;
        }

        instrumentId = selectedInstrumentId;
        return true;
    }

    public static bool IconGridPicker(
        string popupId,
        uint currentIconId,
        string? currentTooltip,
        IReadOnlyList<IconPickerItem> items,
        out uint selectedValue,
        Vector2? size = null,
        bool allowRightClickReset = false,
        uint resetValue = 0)
    {
        selectedValue = resetValue;
        bool changed = false;
        var iconSize = size == null ? ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()) : size.Value;
        DalamudApi.TextureProvider.DrawIcon(currentIconId, iconSize);

        var iconHovered = ImGui.IsItemHovered();
        if (iconHovered && !string.IsNullOrWhiteSpace(currentTooltip))
            ImGuiUtil.ToolTip(currentTooltip);

        var resetRequested = allowRightClickReset &&
                             iconHovered &&
                             ImGui.IsItemClicked(ImGuiMouseButton.Right);

        ImGui.OpenPopupOnItemClick(popupId, ImGuiPopupFlags.MouseButtonLeft);

        using var borderColor = ImRaii.PushColor(ImGuiCol.Border, Style.Components.TooltipBorderColor);
        using var popupBorder = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1)
        .Push(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(ImGui.GetStyle().FramePadding.Y));
        using var popUp = ImRaii.Popup(popupId);
        if (popUp)
        {
            foreach (var item in items)
            {
                DalamudApi.TextureProvider.DrawIcon(item.IconId, ImGuiHelpers.ScaledVector2(40, 40));
                if (ImGui.IsItemClicked())
                {
                    selectedValue = item.Value;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(item.Tooltip))
                    ImGuiUtil.ToolTip(item.Tooltip);

                if (!item.BreakAfter)
                    ImGui.SameLine();
            }
        }

        if (resetRequested)
        {
            selectedValue = resetValue;
            changed = true;
        }

        return changed;
    }
}
