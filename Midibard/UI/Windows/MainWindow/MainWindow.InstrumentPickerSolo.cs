using Dalamud.Bindings.ImGui;

using MidiBard.Resources;

namespace MidiBard;

public partial class MainWindow
{
    private void InstrumentPickerSolo()
    {
        RefreshUICurrentInstrument();

        if (UiComponents.InstrumentPicker($"##instrumentPicker", ref UIcurrentInstrument))
        {
            Plugin.InstrumentSwitcher.SwitchToContinue(UIcurrentInstrument);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        ImGuiUtil.ToolTip(Language.setting_perf_instrument_tooltip);
    }
}
