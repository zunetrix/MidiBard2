using Dalamud.Bindings.ImGui;

namespace MidiBard;

internal static partial class MidiEditorOperationHelp
{
    public static void DrawDescription(string description)
    {
        ImGui.TextWrapped(description);
        ImGui.Spacing();
    }
}
