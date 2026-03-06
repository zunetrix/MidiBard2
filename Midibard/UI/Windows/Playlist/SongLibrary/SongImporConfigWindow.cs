using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;


namespace MidiBard;

public class SongImporConfigWindow : Window
{
    private Plugin Plugin { get; }

    public SongImporConfigWindow(Plugin plugin) : base($"{Plugin.Name} Song Import Config###SongImporConfigWindow")
    {
        Plugin = plugin;

        Size = ImGuiHelpers.ScaledVector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        // SizeCondition = ImGuiCond.Always;
        // Flags = ImGuiWindowFlags.NoResize;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ImGuiHelpers.ScaledVector2(400, 300),
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();
    }

    public override void Draw()
    {

    }
}
