using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

using MidiBard.Extensions.Dalamud.Texture;
using MidiBard.Resources;
using MidiBard.Util;

namespace MidiBard;

public partial class MainWindow
{
    private void InstrumentComboBox()
    {
        RefreshUICurrentInstrument();

        if (ImGui.BeginCombo(Language.setting_label_select_instrument, InstrumentHelper.InstrumentStrings[UIcurrentInstrument], ImGuiComboFlags.HeightLarge))
        {
            ImGui.GetWindowDrawList().ChannelsSplit(2);
            for (uint i = 0; i < InstrumentHelper.Instruments.Length; i++)
            {
                var instrument = InstrumentHelper.Instruments[i];
                ImGui.GetWindowDrawList().ChannelsSetCurrent(1);
                DalamudApi.TextureProvider.DrawIcon(instrument.IconId, ImGuiHelpers.ScaledVector2(ImGui.GetTextLineHeightWithSpacing()));

                ImGui.SameLine();
                ImGui.GetWindowDrawList().ChannelsSetCurrent(0);
                ImGui.AlignTextToFramePadding();

                if (ImGui.Selectable($"{instrument.InstrumentString}##{i}", UIcurrentInstrument == i, ImGuiSelectableFlags.SpanAllColumns))
                {
                    UIcurrentInstrument = i;
                    Plugin.InstrumentSwitcher.SwitchToContinue(i);
                }
            }

            ImGui.GetWindowDrawList().ChannelsMerge();
            ImGui.EndCombo();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(0);
            Plugin.MidiPlayerControl.Pause();
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_select_instrument);
    }
}
