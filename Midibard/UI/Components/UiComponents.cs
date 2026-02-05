using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Extensions.Dalamud.Texture;

namespace MidiBard;

public static class UiComponents
{
    public static bool InstrumentPicker(string label, ref uint instrumentId)
    {
        bool changed = false;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().FramePadding.Y));

        uint undefinedInstrumentTexture = 60042;

        uint iconId = instrumentId == 0 ? undefinedInstrumentTexture : Plugin.Instruments[instrumentId].IconId;
        DalamudApi.TextureProvider.DrawIcon(iconId, ImGuiHelpers.ScaledVector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));

        if (ImGui.IsItemHovered())
            ImGuiUtil.ToolTip(Plugin.Instruments[instrumentId].InstrumentString);

        ImGui.OpenPopupOnItemClick($"instrument{label}", ImGuiPopupFlags.MouseButtonLeft);

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

        ImGui.PopStyleVar();

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            instrumentId = 0;
            changed = true;
        }

        return changed;
    }
}
