using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MidiBard;

public partial class MidiEditorWindow
{
    private void DrawPianoRollPanel()
    {
        using var child = ImRaii.Child("##PianoRollChild", System.Numerics.Vector2.Zero, false);
        if (!child) return;

        ImGui.TextDisabled("Preview");
    }
}
