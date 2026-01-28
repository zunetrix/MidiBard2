using Dalamud.Bindings.ImGui;

using MidiBard.Control.CharacterControl;

using MidiBard.Resources;

namespace MidiBard;

public partial class PluginUI
{
    private static void InstrumentPickerSolo()
    {
        UIcurrentInstrument = Plugin.CurrentInstrument;
        if (Plugin.PlayingGuitar)
        {
            UIcurrentInstrument = (uint)(Plugin.AgentPerformance.CurrentGroupTone + Plugin.guitarGroup[0]);
        }

        if (InstrumentPicker($"##instrumentPicker", ref UIcurrentInstrument))
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(UIcurrentInstrument);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        ImGuiUtil.ToolTip(Language.setting_tooltip_select_instrument);
    }
}
