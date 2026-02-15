using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

using MidiBard.Extensions.Dalamud.Texture;

namespace MidiBard;

public static class UiComponents
{
    public static bool InstrumentPicker(string label, ref uint instrumentId, Vector2? size = null)
    {
        bool changed = false;
        uint undefinedInstrumentIconId = 60042;
        uint iconId = instrumentId == 0 ? undefinedInstrumentIconId : Plugin.Instruments[instrumentId].IconId;
        var iconSize = size == null ? ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()) : size.Value;
        DalamudApi.TextureProvider.DrawIcon(iconId, iconSize);

        if (ImGui.IsItemHovered())
            ImGuiUtil.ToolTip(Plugin.Instruments[instrumentId].InstrumentString);

        ImGui.OpenPopupOnItemClick($"instrument{label}", ImGuiPopupFlags.MouseButtonLeft);

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGuiHelpers.ScaledVector2(ImGui.GetStyle().FramePadding.Y)))
        {
            if (ImGui.BeginPopup($"instrument{label}"))
            {
                HashSet<uint> InstrumentGroupBreaks = new() { 4, 9, 14, 19, 23 };

                for (uint i = 1; i < Plugin.Instruments.Length; i++)
                {
                    DalamudApi.TextureProvider.DrawIcon(Plugin.Instruments[i].IconId, ImGuiHelpers.ScaledVector2(40, 40));
                    if (ImGui.IsItemClicked())
                    {
                        instrumentId = i;
                        changed = true;
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGui.IsItemHovered())
                        ImGuiUtil.ToolTip(Plugin.Instruments[i].InstrumentString);

                    if (!InstrumentGroupBreaks.Contains(i))
                        ImGui.SameLine();
                }
                ImGui.EndPopup();
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            instrumentId = 0;
            changed = true;
        }

        return changed;
    }
}
